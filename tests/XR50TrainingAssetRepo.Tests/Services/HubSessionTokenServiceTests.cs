using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Unit tests for the Hub decrypt API client: request shape (path, secret header, JSON body),
/// outcome mapping for every response class, and the hashed-key result cache.
/// </summary>
public class HubSessionTokenServiceTests
{
    private const string ValidBody = """
        {
          "valid": true,
          "reason": null,
          "claims": {
            "version": 1,
            "userId": "3f1c9b2e-0000-0000-0000-000000000001",
            "tenantId": "976092b0-0ca8-404d-99b8-30a8c755719c",
            "sessionId": "8ac30000-0000-0000-0000-000000000002",
            "applicationId": "b21f0000-0000-0000-0000-000000000003",
            "user": { "firstName": "Ada", "lastName": "Lovelace", "email": "ada@example.com", "skillLevel": "Advanced" },
            "issuedAt": 1785328929,
            "expiresAt": 99999999999
          }
        }
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HubSessionTokenService CreateService(
        StubHandler handler, int cacheSeconds = 60, IMemoryCache? cache = null)
    {
        var options = Options.Create(new XR50HubOptions
        {
            BaseUrl = "https://hub.test",
            SharedSecret = "shared-secret",
            CacheSeconds = cacheSeconds,
        });

        return new HubSessionTokenService(
            new HttpClient(handler),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<HubSessionTokenService>.Instance,
            options);
    }

    [Fact]
    public async Task Decrypt_SendsSecretHeaderAndTokenBody_ToDecryptEndpoint()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var service = CreateService(handler);

        await service.DecryptAsync("the-token");

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be("https://hub.test/api/v1/session-token/decrypt");
        request.Headers.GetValues("hl-hub-external-service-secret").Should().ContainSingle("shared-secret");
        handler.RequestBodies.Single().Should().Contain("\"token\":\"the-token\"");
    }

    [Fact]
    public async Task Decrypt_ValidResponse_MapsClaims()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var service = CreateService(handler);

        var result = await service.DecryptAsync("the-token");

        result.Outcome.Should().Be(HubDecryptOutcome.Valid);
        result.Claims.Should().NotBeNull();
        result.Claims!.TenantId.Should().Be(Guid.Parse("976092b0-0ca8-404d-99b8-30a8c755719c"));
        result.Claims.User.Email.Should().Be("ada@example.com");
        result.Claims.User.SkillLevel.Should().Be("Advanced");
    }

    [Theory]
    [InlineData("MALFORMED")]
    [InlineData("EXPIRED")]
    [InlineData("SESSION_INACTIVE")]
    public async Task Decrypt_InvalidResponse_MapsReason(string reason)
    {
        var body = $$"""{ "valid": false, "reason": "{{reason}}", "claims": null }""";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        var service = CreateService(handler);

        var result = await service.DecryptAsync("bad-token");

        result.Outcome.Should().Be(HubDecryptOutcome.Invalid);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task Decrypt_401_MapsToSecretRejected()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var service = CreateService(handler);

        var result = await service.DecryptAsync("any-token");

        result.Outcome.Should().Be(HubDecryptOutcome.SecretRejected);
    }

    [Fact]
    public async Task Decrypt_400_MapsToMalformed()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var service = CreateService(handler);

        var result = await service.DecryptAsync("any-token");

        result.Outcome.Should().Be(HubDecryptOutcome.Invalid);
        result.Reason.Should().Be("MALFORMED");
    }

    [Fact]
    public async Task Decrypt_503_MapsToUnavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(handler);

        var result = await service.DecryptAsync("any-token");

        result.Outcome.Should().Be(HubDecryptOutcome.Unavailable);
    }

    [Fact]
    public async Task Decrypt_NetworkFailure_MapsToUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var service = CreateService(handler);

        var result = await service.DecryptAsync("any-token");

        result.Outcome.Should().Be(HubDecryptOutcome.Unavailable);
    }

    [Fact]
    public async Task Decrypt_EmptyToken_IsMalformed_WithoutCallingTheHub()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var service = CreateService(handler);

        var result = await service.DecryptAsync("");

        result.Outcome.Should().Be(HubDecryptOutcome.Invalid);
        handler.Requests.Should().BeEmpty();
    }

    // --- Caching ---

    [Fact]
    public async Task Decrypt_ValidResult_IsCached_NoSecondHttpCall()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var service = CreateService(handler);

        var first = await service.DecryptAsync("the-token");
        var second = await service.DecryptAsync("the-token");

        first.Outcome.Should().Be(HubDecryptOutcome.Valid);
        second.Outcome.Should().Be(HubDecryptOutcome.Valid);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Decrypt_InvalidResult_IsCached_NoSecondHttpCall()
    {
        var handler = new StubHandler(_ =>
            Json(HttpStatusCode.OK, """{ "valid": false, "reason": "EXPIRED", "claims": null }"""));
        var service = CreateService(handler);

        await service.DecryptAsync("bad-token");
        await service.DecryptAsync("bad-token");

        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Decrypt_Unavailable_IsNotCached_RetriesOnNextCall()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(handler);

        await service.DecryptAsync("any-token");
        await service.DecryptAsync("any-token");

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Decrypt_TokenAlreadyExpired_IsRejectedEvenIfHubSaysValid()
    {
        var expiredBody = ValidBody.Replace("99999999999", "1000000000"); // 2001, long past
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, expiredBody));
        var service = CreateService(handler);

        var result = await service.DecryptAsync("stale-token");

        result.Outcome.Should().Be(HubDecryptOutcome.Invalid);
        result.Reason.Should().Be("EXPIRED");
    }

    [Fact]
    public async Task Decrypt_CacheKeys_AreHashes_NotRawTokens()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var service = CreateService(handler, cache: cache);

        const string token = "super-secret-token-value";
        await service.DecryptAsync(token);

        // MemoryCache does not expose keys publicly; assert through lookup behavior instead:
        // the raw token must not be a usable key, while a second call still hits the cache.
        cache.TryGetValue(token, out _).Should().BeFalse();
        cache.TryGetValue("hubtok:" + token, out _).Should().BeFalse();
        await service.DecryptAsync(token);
        handler.Requests.Should().HaveCount(1);
    }
}
