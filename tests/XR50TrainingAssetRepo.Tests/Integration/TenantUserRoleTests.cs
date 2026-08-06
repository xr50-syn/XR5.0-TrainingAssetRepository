using System.Net.Http.Json;
using System.Text.Json;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Integration;

/// <summary>
/// Covers the local half of the Hub integration: because the XR5.0 Hub session token carries no
/// roles, users exist on both systems and roles are granted here. These tests pin the grant API,
/// the roster it feeds, and the two escalation paths that stay closed - minting a system admin
/// from a tenant-scoped role, and a tenant admin locking themselves out.
/// </summary>
public class TenantUserRoleTests : IClassFixture<WebApplicationFixture>
{
    private const string Tenant = WebApplicationFixture.TestTenant;

    private readonly HttpClient _client;

    public TenantUserRoleTests(WebApplicationFixture factory)
    {
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage Request(
        HttpMethod method, string uri, string? user = null, string? roles = null, string? tenant = null)
    {
        var request = new HttpRequestMessage(method, uri);
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

    /// <summary>Creates a user the way a Hub identity is pre-provisioned: id only, no password.</summary>
    private async Task<string> CreateUserAsync(string userName)
    {
        var request = Request(HttpMethod.Post, $"/api/{Tenant}/users");
        request.Content = JsonContent.Create(new { userName, fullName = "Test Person", userEmail = $"{userName}@test.local" });

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        return userName;
    }

    private async Task<HttpResponseMessage> SetRoleAsync(
        string userName, string role, string? actor = null, string? actorRoles = null)
    {
        var request = Request(HttpMethod.Put, $"/api/{Tenant}/users/{userName}/role",
            user: actor, roles: actorRoles, tenant: actorRoles == null ? null : Tenant);
        request.Content = JsonContent.Create(new { role });
        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> GetUserAsync(string userName)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/users/{userName}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task NewUser_IsAPlainMember()
    {
        var userName = await CreateUserAsync($"member-{Guid.NewGuid():N}");

        var user = await GetUserAsync(userName);

        user.GetProperty("role").GetString().Should().Be("member");
        user.GetProperty("isTenantAdmin").GetBoolean().Should().BeFalse();
        user.GetProperty("admin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SetRole_ToTenantAdmin_ThenBackToMember()
    {
        var userName = await CreateUserAsync($"promote-{Guid.NewGuid():N}");

        var promote = await SetRoleAsync(userName, "tenantadmin");
        promote.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserAsync(userName)).GetProperty("isTenantAdmin").GetBoolean().Should().BeTrue();

        var demote = await SetRoleAsync(userName, "member");
        demote.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserAsync(userName)).GetProperty("isTenantAdmin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SetRole_IsIdempotent()
    {
        var userName = await CreateUserAsync($"idem-{Guid.NewGuid():N}");

        (await SetRoleAsync(userName, "tenantadmin")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SetRoleAsync(userName, "tenantadmin")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetUserAsync(userName)).GetProperty("isTenantAdmin").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GrantedRole_ShowsInTheRoster()
    {
        var userName = await CreateUserAsync($"roster-{Guid.NewGuid():N}");
        await SetRoleAsync(userName, "tenantadmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/users"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var roster = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entry = roster.EnumerateArray().Single(u => u.GetProperty("userName").GetString() == userName);
        entry.GetProperty("role").GetString().Should().Be("tenantadmin");
    }

    [Fact]
    public async Task Roster_NeverExposesStoredPasswords()
    {
        await CreateUserAsync($"secret-{Guid.NewGuid():N}");

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/{Tenant}/users"));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("password", "the stored password is not part of the user representation");
    }

    [Fact]
    public async Task SetRole_ByPlainMember_Returns403()
    {
        var userName = await CreateUserAsync($"outsider-{Guid.NewGuid():N}");

        var response = await SetRoleAsync(userName, "tenantadmin", actor: "someone", actorRoles: "user");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SetRole_ByTenantAdmin_Succeeds()
    {
        var userName = await CreateUserAsync($"peer-{Guid.NewGuid():N}");

        var response = await SetRoleAsync(userName, "tenantadmin", actor: "boss", actorRoles: "tenantadmin");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetRole_WithUnknownRole_Returns400()
    {
        var userName = await CreateUserAsync($"badrole-{Guid.NewGuid():N}");

        var response = await SetRoleAsync(userName, "systemadmin");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetRole_ForUnknownUser_Returns404()
    {
        var response = await SetRoleAsync("nobody-at-all", "tenantadmin");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantAdmin_CannotDemoteThemselves()
    {
        var userName = await CreateUserAsync($"selfdemote-{Guid.NewGuid():N}");
        await SetRoleAsync(userName, "tenantadmin");

        var response = await SetRoleAsync(userName, "member", actor: userName, actorRoles: "tenantadmin");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetUserAsync(userName)).GetProperty("isTenantAdmin").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task TenantAdmin_CannotCreateASystemAdmin()
    {
        var request = Request(HttpMethod.Post, $"/api/{Tenant}/users", user: "boss", roles: "tenantadmin", tenant: Tenant);
        request.Content = JsonContent.Create(new { userName = $"escalate-{Guid.NewGuid():N}", admin = true });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantAdmin_CannotPromoteAnExistingUserToSystemAdmin()
    {
        var userName = await CreateUserAsync($"escalate2-{Guid.NewGuid():N}");

        var request = Request(HttpMethod.Put, $"/api/{Tenant}/users/{userName}",
            user: "boss", roles: "tenantadmin", tenant: Tenant);
        request.Content = JsonContent.Create(new User { UserName = userName, admin = true });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetUserAsync(userName)).GetProperty("admin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task DeletingAUser_AlsoDropsTheirRoleGrant()
    {
        var userName = await CreateUserAsync($"gone-{Guid.NewGuid():N}");
        await SetRoleAsync(userName, "tenantadmin");

        var delete = await _client.SendAsync(Request(HttpMethod.Delete, $"/api/{Tenant}/users/{userName}"));
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Re-creating the same name must not resurrect the old grant.
        await CreateUserAsync(userName);
        (await GetUserAsync(userName)).GetProperty("isTenantAdmin").GetBoolean().Should().BeFalse();
    }
}
