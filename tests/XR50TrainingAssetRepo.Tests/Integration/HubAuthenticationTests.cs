using XR50TrainingAssetRepo.Infrastructure.Auth;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Integration;

/// <summary>
/// Exercises the XR5.0 Hub session token authentication path end-to-end through the HTTP
/// pipeline: header-based scheme selection, decrypt outcomes (valid / invalid / secret rejected /
/// unavailable), tenant scoping via the enricher, DB-derived roles, and the Development-only
/// dev-token short-circuit. The decrypt client and enricher are fakes; everything else is real.
/// </summary>
public class HubAuthenticationTests : IClassFixture<HubAuthWebApplicationFixture>
{
    private const string Tenant = HubAuthWebApplicationFixture.TestTenant;

    private static readonly Guid MappedTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AdminTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SysAdminTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UnmappedTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid DevTenantId = Guid.Parse("976092b0-0ca8-404d-99b8-30a8c755719c");

    private readonly HubAuthWebApplicationFixture _factory;
    private readonly HttpClient _client;

    public HubAuthenticationTests(HubAuthWebApplicationFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        factory.Enricher.SetIdentity(MappedTenantId, new HubLocalIdentity(Tenant, "hubuser", false, false));
        factory.Enricher.SetIdentity(AdminTenantId, new HubLocalIdentity(Tenant, "hubadmin", true, false));
        factory.Enricher.SetIdentity(SysAdminTenantId, new HubLocalIdentity(Tenant, "hubsysadmin", false, true));
        factory.Enricher.SetIdentity(DevTenantId, new HubLocalIdentity(Tenant, "devtester", false, false));
    }

    private static HubClaims ClaimsFor(Guid tenantId, string email = "hubuser@example.com") => new()
    {
        Version = 1,
        UserId = Guid.NewGuid(),
        TenantId = tenantId,
        SessionId = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        User = new HubUser { FirstName = "Hub", LastName = "User", Email = email, SkillLevel = "Advanced" },
        IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
    };

    private string RegisterToken(HubDecryptResult result)
    {
        var token = $"tok-{Guid.NewGuid():N}";
        _factory.TokenService.SetResult(token, result);
        return token;
    }

    private static HttpRequestMessage Request(HttpMethod method, string uri, string? token = null)
    {
        var request = new HttpRequestMessage(method, uri);
        if (token != null)
        {
            request.Headers.Add(HubSessionTokenDefaults.HeaderName, token);
        }
        return request;
    }

    // --- Scheme selection ---

    [Fact]
    public async Task NoHubHeader_InDevelopment_FallsThroughToJwt_AndReturns401()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/materials"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");
    }

    // --- Valid tokens ---

    [Fact]
    public async Task ValidToken_MappedTenant_MatchingRoute_Returns200()
    {
        var token = RegisterToken(HubDecryptResult.ValidToken(ClaimsFor(MappedTenantId)));

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/materials", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidToken_MappedTenant_MismatchedRoute_Returns403()
    {
        var token = RegisterToken(HubDecryptResult.ValidToken(ClaimsFor(MappedTenantId)));

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/otherTenant/materials", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ValidToken_UnmappedHubTenant_Returns403_OnTenantRoute()
    {
        var token = RegisterToken(HubDecryptResult.ValidToken(ClaimsFor(UnmappedTenantId)));

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/materials", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ValidToken_UnmappedHubTenant_StillAuthenticated_OnFallbackOnlyEndpoint()
    {
        var token = RegisterToken(HubDecryptResult.ValidToken(ClaimsFor(UnmappedTenantId)));

        // Endpoint with no explicit policy: only the fallback "authenticated" requirement applies.
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/xr50/trainingAssetRepository/Tenants/examples/create-requests", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- Invalid tokens ---

    [Theory]
    [InlineData("MALFORMED")]
    [InlineData("EXPIRED")]
    [InlineData("SESSION_INACTIVE")]
    public async Task InvalidToken_Returns401(string reason)
    {
        var token = RegisterToken(HubDecryptResult.InvalidToken(reason));

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/materials", token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SecretRejected_FailsClosed_Returns401()
    {
        var token = RegisterToken(HubDecryptResult.SecretRejected());

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/materials", token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HubUnavailable_Returns503()
    {
        var token = RegisterToken(HubDecryptResult.Unavailable());

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/materials", token));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task RejectedToken_IsNeverEchoedInTheResponse()
    {
        var token = RegisterToken(HubDecryptResult.InvalidToken("MALFORMED"));

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/materials", token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(token);
        response.Headers.ToString().Should().NotContain(token);
    }

    // --- Roles from the local registry ---

    [Fact]
    public async Task TenantAdminFromDb_PassesTenantAdminPolicy()
    {
        var token = RegisterToken(HubDecryptResult.ValidToken(ClaimsFor(AdminTenantId, "admin@example.com")));

        var response = await _client.SendAsync(
            Request(HttpMethod.Delete, $"/api/{Tenant}/materials/999999", token));

        // Authorization must pass; the action itself then reports the missing material.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MemberWithoutDbRoles_FailsTenantAdminPolicy()
    {
        var token = RegisterToken(HubDecryptResult.ValidToken(ClaimsFor(MappedTenantId)));

        var response = await _client.SendAsync(
            Request(HttpMethod.Delete, $"/api/{Tenant}/materials/999999", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SystemAdminFromDb_PassesSystemAdminPolicy()
    {
        var token = RegisterToken(HubDecryptResult.ValidToken(ClaimsFor(SysAdminTenantId, "root@example.com")));

        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/xr50/trainingAssetRepository/Tenants", token));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // --- Development token ---

    [Fact]
    public async Task DevelopmentToken_InDevelopment_AuthenticatesWithFixedIdentity()
    {
        var response = await _client.SendAsync(Request(
            HttpMethod.Get, $"/api/{Tenant}/materials", HubAuthWebApplicationFixture.DevelopmentToken));

        // The dev identity flows through the enricher (DevTenantId mapped to the test tenant)
        // without touching the decrypt client.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>Production-environment behavior: Hub-only surface, dev token disabled.</summary>
public class HubAuthenticationProductionTests : IClassFixture<HubAuthProductionFixture>
{
    private const string Tenant = HubAuthWebApplicationFixture.TestTenant;

    private readonly HubAuthProductionFixture _factory;
    private readonly HttpClient _client;

    public HubAuthenticationProductionTests(HubAuthProductionFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NoHeader_InProduction_Returns401_FromHubScheme()
    {
        var response = await _client.GetAsync($"/api/{Tenant}/materials");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain(HubSessionTokenDefaults.HeaderName);
    }

    [Fact]
    public async Task DevelopmentToken_InProduction_IsNotHonored()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/{Tenant}/materials");
        request.Headers.Add(HubSessionTokenDefaults.HeaderName, HubAuthWebApplicationFixture.DevelopmentToken);

        var callsBefore = _factory.TokenService.DecryptCallCount;
        var response = await _client.SendAsync(request);

        // Outside Development the fixed token is just another opaque token: it must reach the
        // decrypt flow (where the fake rejects it) instead of short-circuiting.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.TokenService.DecryptCallCount.Should().BeGreaterThan(callsBefore);
    }
}
