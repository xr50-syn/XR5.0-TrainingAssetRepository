using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Verifies that an explicit <c>collectionName</c> on AI Assistant materials is honored end-to-end
/// through the real controller JSON parsing (hermetic: InMemory DB + MockStorageService, no DataLens).
///
/// Motivation: a tenant may keep separate document sets (different document types) in distinct
/// DataLens collections and want an assistant to ingest into / chat against a chosen one rather than
/// the auto-assigned per-material collection (aiassist_{id}).
///
/// These cases use Mode B (no assets): the create path then calls CreateAsync, which honors a
/// pre-set CollectionName and never auto-assigns aiassist_{id} or calls DataLens — keeping the
/// test free of external dependencies.
/// </summary>
public class AIAssistantCollectionNameTests : IClassFixture<WebApplicationFixture>
{
    private readonly WebApplicationFixture _factory;
    private readonly HttpClient _client;

    public AIAssistantCollectionNameTests(WebApplicationFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string MaterialsUrl => $"/api/{WebApplicationFixture.TestTenant}/materials";

    private async Task<AIAssistantMaterial> GetMaterialFromDbAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();
        using var context = dbFactory.CreateDbContext();
        return await context.Materials.OfType<AIAssistantMaterial>().FirstAsync(m => m.id == id);
    }

    [Fact]
    public async Task Create_WithExplicitCollectionName_BindsToThatCollection()
    {
        var response = await _client.PostAsJsonAsync(MaterialsUrl, new
        {
            name = "Explicit collection assistant",
            description = "binds to a chosen collection",
            type = "ai_assistant",
            collectionName = "engineering_manuals"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<CreateMaterialResponse>();
        body!.CollectionName.Should().Be("engineering_manuals",
            "the create response must echo the bound collection");

        var material = await GetMaterialFromDbAsync(body.id);
        material.CollectionName.Should().Be("engineering_manuals",
            "the persisted material must keep the explicit collection, not the tenant default");
    }

    [Fact]
    public async Task Create_WithSnakeCaseCollectionName_IsHonored()
    {
        var response = await _client.PostAsJsonAsync(MaterialsUrl, new
        {
            name = "Snake case collection assistant",
            type = "ai_assistant",
            collection_name = "safety_docs"
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<CreateMaterialResponse>();
        body!.CollectionName.Should().Be("safety_docs");
    }

    [Fact]
    public async Task Update_WithoutCollectionName_PreservesExistingBinding()
    {
        var create = await _client.PostAsJsonAsync(MaterialsUrl, new
        {
            name = "Preserve me",
            type = "ai_assistant",
            collectionName = "original_collection"
        });
        var created = await create.Content.ReadFromJsonAsync<CreateMaterialResponse>();

        var put = await _client.PutAsJsonAsync($"{MaterialsUrl}/{created!.id}", new
        {
            type = "ai_assistant",
            name = "Renamed but same collection"
        });
        put.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var material = await GetMaterialFromDbAsync(created.id);
        material.CollectionName.Should().Be("original_collection",
            "a PUT that omits collectionName must not wipe the existing binding");
        material.Name.Should().Be("Renamed but same collection");
    }

    [Fact]
    public async Task Update_WithNewCollectionName_RepointsBinding()
    {
        var create = await _client.PostAsJsonAsync(MaterialsUrl, new
        {
            name = "Repoint me",
            type = "ai_assistant",
            collectionName = "first_collection"
        });
        var created = await create.Content.ReadFromJsonAsync<CreateMaterialResponse>();

        var put = await _client.PutAsJsonAsync($"{MaterialsUrl}/{created!.id}", new
        {
            type = "ai_assistant",
            collectionName = "second_collection"
        });
        put.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var material = await GetMaterialFromDbAsync(created.id);
        material.CollectionName.Should().Be("second_collection",
            "an explicit collectionName on PUT must override the prior binding");
    }
}
