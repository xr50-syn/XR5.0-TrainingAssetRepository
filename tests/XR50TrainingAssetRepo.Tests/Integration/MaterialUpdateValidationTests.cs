using XR50TrainingAssetRepo.Tests.Fixtures;
using XR50TrainingAssetRepo.Tests.Factories;

namespace XR50TrainingAssetRepo.Tests.Integration;

/// <summary>
/// PUT /materials/{id} replaces the material, so a body that leaves out the name used to store
/// a null name. The update now applies the create-time validation: no name or an unknown type is
/// a 400 and the stored material is left untouched.
/// </summary>
public class MaterialUpdateValidationTests : IClassFixture<WebApplicationFixture>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly List<int> _createdMaterialIds = new();
    private const string TenantName = WebApplicationFixture.TestTenant;

    public MaterialUpdateValidationTests(WebApplicationFixture factory)
    {
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var id in _createdMaterialIds)
        {
            await _client.DeleteAsync($"/api/{TenantName}/materials/{id}");
        }
    }

    private async Task<int> CreateVideoAsync(string name)
    {
        var response = await _client.PostAsJsonAsync($"/api/{TenantName}/materials", MaterialFactory.CreateVideoRequest(name));
        response.EnsureSuccessStatusCode();

        var idElement = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id");
        // API returns ID as string due to custom JSON converter
        var id = idElement.ValueKind == JsonValueKind.String ? int.Parse(idElement.GetString()!) : idElement.GetInt32();
        _createdMaterialIds.Add(id);
        return id;
    }

    private async Task<string?> GetNameAsync(int id)
    {
        var material = await _client.GetFromJsonAsync<JsonElement>($"/api/{TenantName}/materials/{id}");
        return material.GetProperty("name").GetString();
    }

    [Fact]
    public async Task PUT_WithoutName_Returns400_AndKeepsStoredName()
    {
        var id = await CreateVideoAsync("Keep This Name");

        var response = await _client.PutAsJsonAsync($"/api/{TenantName}/materials/{id}",
            new { description = "description only", type = "video" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetNameAsync(id)).Should().Be("Keep This Name");
    }

    [Fact]
    public async Task PUT_WithInvalidType_Returns400()
    {
        var id = await CreateVideoAsync("Typed Video");

        var response = await _client.PutAsJsonAsync($"/api/{TenantName}/materials/{id}",
            new { name = "Typed Video", type = "NotAType" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_WithName_StillUpdates()
    {
        var id = await CreateVideoAsync("Before");

        var response = await _client.PutAsJsonAsync($"/api/{TenantName}/materials/{id}",
            new { name = "After", description = "updated", type = "video", videoPath = "/videos/test.mp4" });

        response.EnsureSuccessStatusCode();
        (await GetNameAsync(id)).Should().Be("After");
    }
}
