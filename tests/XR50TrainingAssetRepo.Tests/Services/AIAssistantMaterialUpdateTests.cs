using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Tests.Services;

public class AIAssistantMaterialUpdateTests
{
    [Fact]
    public async Task PutStyleUpdate_PreservesCompletedJobs_AndSubmitsOnlyNewAssets()
    {
        var options = new DbContextOptionsBuilder<XR50TrainingContext>()
            .UseInMemoryDatabase($"ai-assistant-update-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new NewContextFactory(options);

        await SeedAssistantWithCompletedAssetJobsAsync(factory);

        var materialService = new MaterialServiceBase(
            factory,
            NullLogger<MaterialServiceBase>.Instance);

        var updated = new AIAssistantMaterial
        {
            id = 10,
            Name = "Assistant with new asset",
            Description = "Updated",
            AIAssistantStatus = "process",
            CollectionName = "assistant_collection"
        };
        updated.SetAssetIdsList(new List<int> { 1, 2, 3 });

        await materialService.UpdateAsync(updated);

        var chatbotApi = new RecordingChatbotApiService();
        var aiAssistantService = new AIAssistantMaterialService(
            factory,
            chatbotApi,
            NullLogger<AIAssistantMaterialService>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ChatbotApi:DefaultCollectionName"] = "assistant_collection"
                })
                .Build());

        await aiAssistantService.SubmitForProcessingAsync(10);

        chatbotApi.SubmittedAssetIds.Should().Equal(3);

        using var context = factory.CreateDbContext();
        var jobs = await context.AIAssistantMaterialAssetJobs
            .Where(j => j.AIAssistantMaterialId == 10)
            .OrderBy(j => j.AssetId)
            .ToListAsync();

        jobs.Should().HaveCount(3);
        jobs[0].Status.Should().Be("completed");
        jobs[1].Status.Should().Be("completed");
        jobs[2].Status.Should().Be("pending");
    }

    private static async Task SeedAssistantWithCompletedAssetJobsAsync(IXR50TenantDbContextFactory factory)
    {
        using var context = factory.CreateDbContext();

        context.Assets.AddRange(
            new Asset
            {
                Id = 1,
                Filename = "existing-1.pdf",
                Filetype = "pdf",
                Type = AssetType.PDF,
                URL = "https://example.test/existing-1.pdf"
            },
            new Asset
            {
                Id = 2,
                Filename = "existing-2.pdf",
                Filetype = "pdf",
                Type = AssetType.PDF,
                URL = "https://example.test/existing-2.pdf"
            },
            new Asset
            {
                Id = 3,
                Filename = "new.pdf",
                Filetype = "pdf",
                Type = AssetType.PDF,
                URL = "https://example.test/new.pdf"
            });

        var assistant = new AIAssistantMaterial
        {
            id = 10,
            Name = "Assistant",
            Description = "Original",
            AIAssistantStatus = "ready",
            CollectionName = "assistant_collection",
            Created_at = DateTime.UtcNow.AddDays(-1),
            Updated_at = DateTime.UtcNow.AddDays(-1)
        };
        assistant.SetAssetIdsList(new List<int> { 1, 2 });

        context.Materials.Add(assistant);
        context.AIAssistantMaterialAssetJobs.AddRange(
            new AIAssistantMaterialAssetJob
            {
                AIAssistantMaterialId = 10,
                AssetId = 1,
                CollectionName = "assistant_collection",
                JobId = "job-existing-1",
                Status = "completed",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new AIAssistantMaterialAssetJob
            {
                AIAssistantMaterialId = 10,
                AssetId = 2,
                CollectionName = "assistant_collection",
                JobId = "job-existing-2",
                Status = "completed",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            });

        await context.SaveChangesAsync();
    }

    private sealed class NewContextFactory : IXR50TenantDbContextFactory
    {
        private readonly DbContextOptions<XR50TrainingContext> _options;

        public NewContextFactory(DbContextOptions<XR50TrainingContext> options)
        {
            _options = options;
        }

        public XR50TrainingContext CreateDbContext() => new(_options);

        public XR50TrainingContext CreateAdminDbContext() => new(_options);
    }

    private sealed class RecordingChatbotApiService : IChatbotApiService
    {
        public List<int> SubmittedAssetIds { get; } = new();

        public Task<string> SubmitDocumentAsync(int assetId, string assetUrl, string filetype, string collectionName)
        {
            SubmittedAssetIds.Add(assetId);
            return Task.FromResult($"job-new-{assetId}");
        }

        public Task<ChatbotJobStatus> GetJobStatusAsync(string jobId, string collectionName)
        {
            return Task.FromResult(new ChatbotJobStatus
            {
                JobId = jobId,
                CollectionName = collectionName,
                Status = "completed"
            });
        }

        public Task<bool> EnsureCollectionExistsAsync(string collectionName) => Task.FromResult(true);

        public Task<bool> DocumentExistsAsync(string collectionName, string documentName) => Task.FromResult(false);

        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
    }
}
