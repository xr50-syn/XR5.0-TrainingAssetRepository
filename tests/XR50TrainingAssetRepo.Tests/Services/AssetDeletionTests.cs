using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Tests for dependency-aware asset deletion: the blocking guard, the force cascade to dependent
/// materials, AI Assistant detach-vs-delete semantics, and DataLens document/collection cleanup.
/// Uses the InMemory web host with a recording DataLens client so cleanup calls can be asserted.
/// </summary>
public class AssetDeletionTests : IClassFixture<AssetDeletionTests.RecordingChatbotFixture>
{
    private readonly RecordingChatbotFixture _factory;
    private readonly HttpClient _client;
    private const string TenantName = WebApplicationFixture.TestTenant;

    public AssetDeletionTests(RecordingChatbotFixture factory)
    {
        _factory = factory;
        _factory.Chatbot.Reset();
        _client = factory.CreateClient();
    }

    private void Seed(Action<XR50TrainingContext> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();
        using var context = contextFactory.CreateDbContext();
        seed(context);
        context.SaveChanges();
    }

    private T? Query<T>(Func<XR50TrainingContext, T> query)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();
        using var context = contextFactory.CreateDbContext();
        return query(context);
    }

    private static Asset NewAsset(int id, string aiAvailable = "notready") => new()
    {
        Id = id,
        Filename = $"file-{id}.pdf",
        Filetype = "pdf",
        URL = $"http://localhost/files/{TenantName}/file-{id}.pdf",
        Type = AssetType.PDF,
        AiAvailable = aiAvailable
    };

    [Fact]
    public async Task Delete_UnreferencedAsset_SucceedsAndRemovesRow()
    {
        Seed(ctx => ctx.Assets.Add(NewAsset(1001)));

        var response = await _client.DeleteAsync($"/api/{TenantName}/assets/1001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Query(ctx => ctx.Assets.Any(a => a.Id == 1001)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_ReferencedAsset_WithoutForce_Returns409AndDeletesNothing()
    {
        Seed(ctx =>
        {
            ctx.Assets.Add(NewAsset(1002));
            ctx.Materials.Add(new ImageMaterial
            {
                id = 2002,
                Name = "Blocking image material 2002",
                AssetId = 1002
            });
        });

        var response = await _client.DeleteAsync($"/api/{TenantName}/assets/1002");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Blocking image material 2002");

        // Nothing removed.
        Query(ctx => ctx.Assets.Any(a => a.Id == 1002)).Should().BeTrue();
        Query(ctx => ctx.Materials.Any(m => m.id == 2002)).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_ReferencedAsset_WithForce_CascadesSingleAssetMaterial()
    {
        Seed(ctx =>
        {
            ctx.Assets.Add(NewAsset(1003));
            ctx.Materials.Add(new ImageMaterial
            {
                id = 2003,
                Name = "Image material 2003",
                AssetId = 1003
            });
        });

        var response = await _client.DeleteAsync($"/api/{TenantName}/assets/1003?force=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Query(ctx => ctx.Assets.Any(a => a.Id == 1003)).Should().BeFalse();
        Query(ctx => ctx.Materials.Any(m => m.id == 2003)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_AiAssistantAssetWithSiblings_DetachesAndDeletesDocument()
    {
        Seed(ctx =>
        {
            ctx.Assets.Add(NewAsset(1004));
            ctx.Assets.Add(NewAsset(1005));

            var ai = new AIAssistantMaterial
            {
                id = 2004,
                Name = "AI Assistant 2004",
                CollectionName = "aiassist_2004",
                AIAssistantStatus = "ready"
            };
            ai.SetAssetIdsList(new List<int> { 1004, 1005 });
            ctx.Materials.Add(ai);

            ctx.AIAssistantMaterialAssetJobs.Add(new AIAssistantMaterialAssetJob
            {
                AIAssistantMaterialId = 2004,
                AssetId = 1004,
                CollectionName = "aiassist_2004",
                DocumentName = "file-1004.pdf",
                Status = "completed"
            });
        });

        var response = await _client.DeleteAsync($"/api/{TenantName}/assets/1004?force=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Asset gone, material survives with the remaining asset.
        Query(ctx => ctx.Assets.Any(a => a.Id == 1004)).Should().BeFalse();
        var remaining = Query(ctx => ctx.Materials.OfType<AIAssistantMaterial>().First(m => m.id == 2004).GetAssetIdsList());
        remaining.Should().BeEquivalentTo(new[] { 1005 });

        // The asset's document was removed from the assistant's collection; the collection itself stays.
        _factory.Chatbot.DeletedDocuments.Should().Contain(("aiassist_2004", "file-1004.pdf"));
        _factory.Chatbot.DeletedCollections.Should().NotContain("aiassist_2004");

        // The stale per-(material, asset) job row for the detached asset is gone.
        Query(ctx => ctx.AIAssistantMaterialAssetJobs.Any(j => j.AIAssistantMaterialId == 2004 && j.AssetId == 1004))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Delete_AiAssistantLastAsset_DeletesMaterialAndCollection()
    {
        Seed(ctx =>
        {
            ctx.Assets.Add(NewAsset(1006));

            var ai = new AIAssistantMaterial
            {
                id = 2005,
                Name = "AI Assistant 2005",
                CollectionName = "aiassist_2005",
                AIAssistantStatus = "ready"
            };
            ai.SetAssetIdsList(new List<int> { 1006 });
            ctx.Materials.Add(ai);
        });

        var response = await _client.DeleteAsync($"/api/{TenantName}/assets/1006?force=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        Query(ctx => ctx.Assets.Any(a => a.Id == 1006)).Should().BeFalse();
        Query(ctx => ctx.Materials.Any(m => m.id == 2005)).Should().BeFalse();
        _factory.Chatbot.DeletedCollections.Should().Contain("aiassist_2005");
    }

    /// <summary>
    /// InMemory web host that also swaps the DataLens client for a recording fake.
    /// </summary>
    public class RecordingChatbotFixture : WebApplicationFixture
    {
        public RecordingChatbotApiService Chatbot { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IChatbotApiService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<IChatbotApiService>(Chatbot);
            });
        }
    }

    /// <summary>
    /// Records DataLens delete calls so tests can assert cleanup happened. Never throws.
    /// </summary>
    public class RecordingChatbotApiService : IChatbotApiService
    {
        public List<(string Collection, string Document)> DeletedDocuments { get; } = new();
        public List<string> DeletedCollections { get; } = new();

        public void Reset()
        {
            DeletedDocuments.Clear();
            DeletedCollections.Clear();
        }

        public Task<string> SubmitDocumentAsync(int assetId, string assetUrl, string filetype, string collectionName)
            => Task.FromResult($"job-{assetId}");

        public Task<ChatbotJobStatus> GetJobStatusAsync(string jobId, string collectionName)
            => Task.FromResult(new ChatbotJobStatus { JobId = jobId, CollectionName = collectionName, Status = "completed" });

        public Task<bool> EnsureCollectionExistsAsync(string collectionName) => Task.FromResult(true);

        public Task<bool> DocumentExistsAsync(string collectionName, string documentName) => Task.FromResult(true);

        public Task<bool> DeleteDocumentAsync(string collectionName, string documentName)
        {
            DeletedDocuments.Add((collectionName, documentName));
            return Task.FromResult(true);
        }

        public Task<bool> DeleteCollectionAsync(string collectionName, bool force = true)
        {
            DeletedCollections.Add(collectionName);
            return Task.FromResult(true);
        }

        public string GetDocumentName(string assetUrl, string filetype)
            => System.IO.Path.GetFileName(new Uri(assetUrl).LocalPath);

        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
    }
}
