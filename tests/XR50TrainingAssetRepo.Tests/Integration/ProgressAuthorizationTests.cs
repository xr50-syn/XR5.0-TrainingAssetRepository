using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Integration;

/// <summary>
/// Pins the progress permission model: a member owns their progress - they may read and write
/// their own records and nobody else's - while tenant-wide visibility is a management view
/// behind a tenant-administration role.
/// </summary>
public class ProgressAuthorizationTests : IClassFixture<WebApplicationFixture>
{
    private const string Tenant = WebApplicationFixture.TestTenant;
    private const string Member = "learner";
    private const string Other = "colleague";

    private readonly HttpClient _client;

    public ProgressAuthorizationTests(WebApplicationFixture factory)
    {
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage Request(HttpMethod method, string uri, string user, string roles)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        request.Headers.Add(TestAuthHandler.TenantHeader, Tenant);
        return request;
    }

    private Task<HttpResponseMessage> AsMember(string uri) =>
        _client.SendAsync(Request(HttpMethod.Get, uri, Member, "user"));

    private Task<HttpResponseMessage> AsTenantAdmin(string uri) =>
        _client.SendAsync(Request(HttpMethod.Get, uri, "manager", "tenantadmin"));

    [Fact]
    public async Task Member_ReadingAnotherUsersProgress_Returns403()
    {
        var response = await AsMember($"/api/{Tenant}/users/{Other}/progress");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_ReadingOwnProgress_IsAllowed()
    {
        var response = await AsMember($"/api/{Tenant}/users/{Member}/progress");

        // The record may not exist in the hermetic database; what matters is that the
        // authorization check does not stand in the way.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Member_ReadingAnotherUsersMaterialDetail_Returns403()
    {
        var response = await AsMember($"/api/{Tenant}/users/{Other}/materials/1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_ReadingAnotherUsersProgramMaterials_Returns403()
    {
        var response = await AsMember($"/api/{Tenant}/users/{Other}/programs/1/materials");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_ListingEveryUsersProgress_Returns403()
    {
        var response = await AsMember($"/api/{Tenant}/users/progress");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantAdmin_ListingEveryUsersProgress_IsAllowed()
    {
        var response = await AsTenantAdmin($"/api/{Tenant}/users/progress");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TenantAdmin_ReadingAnotherUsersProgress_IsAllowed()
    {
        var response = await AsTenantAdmin($"/api/{Tenant}/users/{Other}/progress");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
