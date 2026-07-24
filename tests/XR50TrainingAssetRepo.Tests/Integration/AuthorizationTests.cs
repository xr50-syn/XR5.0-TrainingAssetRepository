using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Integration;

/// <summary>
/// Verifies the RBAC + tenant-binding authorization model end-to-end through the HTTP pipeline:
/// global fallback (401 for anonymous), tenant-route binding (403 on tenant mismatch),
/// role policies (TenantAdmin mutations, SystemAdmin tenant management) and the anonymous
/// health endpoints. Identities are supplied per-request via TestAuthHandler headers.
/// </summary>
public class AuthorizationTests : IClassFixture<WebApplicationFixture>
{
    private const string Tenant = WebApplicationFixture.TestTenant;

    private readonly HttpClient _client;

    public AuthorizationTests(WebApplicationFixture factory)
    {
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage Request(
        HttpMethod method,
        string uri,
        bool anonymous = false,
        string? user = null,
        string? roles = null,
        string? tenant = null)
    {
        var request = new HttpRequestMessage(method, uri);
        if (anonymous)
        {
            request.Headers.Add(TestAuthHandler.AnonymousHeader, "true");
        }
        if (user != null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user);
        }
        if (roles != null)
        {
            request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        }
        if (tenant != null)
        {
            request.Headers.Add(TestAuthHandler.TenantHeader, tenant);
        }
        return request;
    }

    // --- Anonymous access ---

    [Fact]
    public async Task AnonymousRequest_OnProtectedEndpoint_Returns401()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/{Tenant}/materials", anonymous: true));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnonymousRequest_OnUnannotatedEndpoint_Returns401_ViaFallbackPolicy()
    {
        // The examples endpoint carries no explicit [Authorize]; the fallback policy must cover it.
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/xr50/trainingAssetRepository/Tenants/examples/create-requests", anonymous: true));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthEndpoint_AllowsAnonymous()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/health", anonymous: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SwaggerUi_AllowsAnonymous_InDevelopment()
    {
        // Swagger is served before the auth middleware; since .NET 8 the FallbackPolicy
        // also covers non-endpoint requests, so this locks the middleware ordering.
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/swagger/index.html", anonymous: true));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task TestEndpoint_AllowsAnonymous()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/test", anonymous: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- Tenant binding ---

    [Fact]
    public async Task TenantUser_WithMatchingTenant_CanRead()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/{Tenant}/materials", roles: "user", tenant: Tenant));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantUser_WithMismatchedTenant_Returns403()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/{Tenant}/materials", roles: "user", tenant: "otherTenant"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantAdmin_WithMismatchedTenant_Returns403()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Delete, $"/api/{Tenant}/materials/999999", roles: "admin", tenant: "otherTenant"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SystemAdmin_WithoutTenantClaim_CanReadAnyTenant()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/{Tenant}/materials", roles: "systemadmin"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- Role enforcement on mutations ---

    [Fact]
    public async Task TenantUser_OnTenantAdminMutation_Returns403()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Delete, $"/api/{Tenant}/materials/999999", roles: "user", tenant: Tenant));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("tenantadmin")]
    public async Task TenantAdminRoles_OnTenantAdminMutation_PassAuthorization(string role)
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Delete, $"/api/{Tenant}/materials/999999", roles: role, tenant: Tenant));

        // Authorization must pass; the action itself then reports the missing material.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // --- System administration ---

    [Fact]
    public async Task TenantUser_OnTenantManagement_Returns403()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/xr50/trainingAssetRepository/Tenants", roles: "user", tenant: Tenant));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SystemAdmin_OnTenantManagement_PassesAuthorization()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/xr50/trainingAssetRepository/Tenants", roles: "systemadmin"));

        // Authorization must pass; the action itself 500s in the hermetic environment because
        // tenant management uses raw MySQL connections that have no in-memory substitute.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantAdmin_CanReadOwnTenantConfiguration()
    {
        // Per-tenant reads on the Tenants controller are TenantMember-gated, not SystemAdmin-gated.
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, $"/xr50/trainingAssetRepository/Tenants/{Tenant}", roles: "user", tenant: Tenant));

        // Authorization must pass; the tenant may not exist in the in-memory admin DB.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
