using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Unit tests for validating a user's Hub login JWT through GET /api/v1/user/limited-info: request
/// shape (path, bearer, no shared secret), outcome mapping for each response class, claim mapping
/// from the body, and the hashed-key cache bounded by the token's exp.
/// </summary>
public class HubUserTokenServiceTests
{
    private const string UserId = "3f1c9b2e-0000-0000-0000-000000000001";
    private const string TenantId = "976092b0-0ca8-404d-99b8-30a8c755719c";

    private static readonly string ValidBody =
        $$"""{ "id": "{{UserId}}", "tenantId": "{{TenantId}}", "email": "ada@example.com", "firstName": "Ada", "lastName": "Lovelace" }""";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static string Jwt(DateTimeOffset? exp = null)
    {
        static string Segment(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var payload = exp.HasValue
            ? $"{{\"sub\":\"x\",\"exp\":{exp.Value.ToUnixTimeSeconds()},\"jti\":\"{Guid.NewGuid():N}\"}}"
            : $"{{\"sub\":\"x\",\"jti\":\"{Guid.NewGuid():N}\"}}";
        return $"{Segment("{\"alg\":\"HS256\"}")}.{Segment(payload)}.c2ln";
    }

    private static HubUserTokenService CreateService(StubHandler handler, int cacheSeconds = 60) =>
        new(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<HubUserTokenService>.Instance,
            Options.Create(new XR50HubOptions
            {
                BaseUrl = "https://hub.test",
                SharedSecret = "shared-secret",
                CacheSeconds = cacheSeconds,
            }));

    [Fact]
    public async Task Validate_SendsTheTokenAsBearer_ToLimitedInfo_WithoutTheSharedSecret()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var token = Jwt(DateTimeOffset.UtcNow.AddHours(1));

        await CreateService(handler).ValidateAsync(token);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.ToString().Should().Be("https://hub.test/api/v1/user/limited-info");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be(token);
        request.Headers.Contains("hl-hub-external-service-secret").Should().BeFalse();
    }

    [Fact]
    public async Task Validate_200_MapsIdTenantAndProfile()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var exp = DateTimeOffset.UtcNow.AddHours(1);

        var result = await CreateService(handler).ValidateAsync(Jwt(exp));

        result.Outcome.Should().Be(HubDecryptOutcome.Valid);
        result.Claims!.UserId.Should().Be(Guid.Parse(UserId));
        result.Claims.TenantId.Should().Be(Guid.Parse(TenantId));
        result.Claims.User.Email.Should().Be("ada@example.com");
        result.Claims.User.FirstName.Should().Be("Ada");
        result.Claims.ExpiresAt.Should().Be(exp.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Validate_RejectedByHub_IsInvalid(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Json(status, """{"title":"ExpiredJwtException","status":"401"}"""));

        var result = await CreateService(handler).ValidateAsync(Jwt(DateTimeOffset.UtcNow.AddHours(1)));

        result.Outcome.Should().Be(HubDecryptOutcome.Invalid);
    }

    [Fact]
    public async Task Validate_ServerError_IsUnavailable_AndNotCached()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);
        var token = Jwt(DateTimeOffset.UtcNow.AddHours(1));

        (await service.ValidateAsync(token)).Outcome.Should().Be(HubDecryptOutcome.Unavailable);
        (await service.ValidateAsync(token)).Outcome.Should().Be(HubDecryptOutcome.Unavailable);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Validate_200_WithoutUsableId_FailsClosed()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""{ "tenantId": "{{TenantId}}" }"""));

        var result = await CreateService(handler).ValidateAsync(Jwt(DateTimeOffset.UtcNow.AddHours(1)));

        result.Outcome.Should().Be(HubDecryptOutcome.Invalid);
    }

    [Fact]
    public async Task Validate_200_WithoutTenant_IsValid_WithEmptyTenant()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""{ "id": "{{UserId}}" }"""));

        var result = await CreateService(handler).ValidateAsync(Jwt(DateTimeOffset.UtcNow.AddHours(1)));

        result.Outcome.Should().Be(HubDecryptOutcome.Valid);
        result.Claims!.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Validate_ExpiredOnItsFace_IsInvalid_WithoutCallingTheHub()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));

        var result = await CreateService(handler).ValidateAsync(Jwt(DateTimeOffset.UtcNow.AddMinutes(-1)));

        result.Outcome.Should().Be(HubDecryptOutcome.Invalid);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_CachesAValidAnswer_ByToken()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var service = CreateService(handler);
        var token = Jwt(DateTimeOffset.UtcNow.AddHours(1));

        await service.ValidateAsync(token);
        await service.ValidateAsync(token);
        await service.ValidateAsync(Jwt(DateTimeOffset.UtcNow.AddHours(1)));

        handler.Requests.Should().HaveCount(2, "a repeated token is served from cache, a different token is not");
    }

    [Fact]
    public async Task Validate_CacheNeverOutlivesTheToken()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ValidBody));
        var service = CreateService(handler, cacheSeconds: 600);
        var token = Jwt(DateTimeOffset.UtcNow.AddSeconds(2));

        (await service.ValidateAsync(token)).Outcome.Should().Be(HubDecryptOutcome.Valid);
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        (await service.ValidateAsync(token)).Outcome.Should().Be(HubDecryptOutcome.Invalid);
    }
}
