using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Chatbot;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Tests.Services;

public class InnovChatbotMaterialTests
{
    [Fact]
    public async Task Submit_IngestsOnlyNewAssets_AndComputesReadyStatus()
    {
        var options = new DbContextOptionsBuilder<XR50TrainingContext>()
            .UseInMemoryDatabase($"innov-chatbot-update-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new NewContextFactory(options);

        await SeedMaterialWithCompletedAssetJobsAsync(factory);

        var provider = new FakeInnovProvider();
        var tenantService = new StubTenantService("test-tenant");
        var tenantManagementService = new StubTenantManagementService("test-tenant", "https://innov.test", "pilot-1");
        var service = new InnovChatbotMaterialService(
            factory,
            new IChatbotProvider[] { provider },
            tenantService,
            tenantManagementService,
            NullLogger<InnovChatbotMaterialService>.Instance);

        var updated = new InnovChatbotMaterial
        {
            id = 10,
            Name = "INNOV chatbot with new asset",
            Description = "Updated",
            Pilot = "pilot-1",
            InnovStatus = "process"
        };
        updated.SetAssetIdsList(new List<int> { 1, 2, 3 });

        await service.UpdateAsync(updated);

        var result = await service.SubmitForProcessingAsync(10);

        // Only the newly added asset (3 -> new.pdf) is ingested; assets 1 & 2 already completed
        // in the same pilot and are skipped.
        provider.IngestedFileNames.Should().Equal("new.pdf");
        result.InnovStatus.Should().Be("ready");

        using var context = factory.CreateDbContext();
        var jobs = await context.InnovChatbotMaterialAssetJobs
            .Where(j => j.InnovChatbotMaterialId == 10)
            .OrderBy(j => j.AssetId)
            .ToListAsync();

        jobs.Should().HaveCount(3);
        jobs.Should().OnlyContain(j => j.Status == "completed");
        jobs.Should().OnlyContain(j => j.Pilot == "pilot-1");
    }

    [Fact]
    public async Task Chat_ReturnsProviderAnswer_ForMaterialPilot()
    {
        var options = new DbContextOptionsBuilder<XR50TrainingContext>()
            .UseInMemoryDatabase($"innov-chatbot-chat-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new NewContextFactory(options);

        using (var context = factory.CreateDbContext())
        {
            var material = new InnovChatbotMaterial
            {
                id = 20,
                Name = "INNOV chatbot",
                Pilot = "pilot-7",
                InnovStatus = "ready"
            };
            material.SetAssetIdsList(new List<int>());
            context.Materials.Add(material);
            await context.SaveChangesAsync();
        }

        var provider = new FakeInnovProvider();
        var service = new InnovChatbotMaterialService(
            factory,
            new IChatbotProvider[] { provider },
            new StubTenantService("test-tenant"),
            new StubTenantManagementService("test-tenant", "https://innov.test", "pilot-default"),
            NullLogger<InnovChatbotMaterialService>.Instance);

        var response = await service.ChatAsync(20, "What is the procedure?", "expert");

        response.Text.Should().Be("answer for pilot-7");
        response.Pilot.Should().Be("pilot-7");
        provider.LastExpertiseLevel.Should().Be("expert");
    }

    private static async Task SeedMaterialWithCompletedAssetJobsAsync(IXR50TenantDbContextFactory factory)
    {
        using var context = factory.CreateDbContext();

        context.Assets.AddRange(
            new Asset { Id = 1, Filename = "existing-1.pdf", Filetype = "pdf", Type = AssetType.PDF, URL = "https://example.test/existing-1.pdf" },
            new Asset { Id = 2, Filename = "existing-2.pdf", Filetype = "pdf", Type = AssetType.PDF, URL = "https://example.test/existing-2.pdf" },
            new Asset { Id = 3, Filename = "new.pdf", Filetype = "pdf", Type = AssetType.PDF, URL = "https://example.test/new.pdf" });

        var material = new InnovChatbotMaterial
        {
            id = 10,
            Name = "INNOV chatbot",
            Description = "Original",
            Pilot = "pilot-1",
            InnovStatus = "ready",
            Created_at = DateTime.UtcNow.AddDays(-1),
            Updated_at = DateTime.UtcNow.AddDays(-1)
        };
        material.SetAssetIdsList(new List<int> { 1, 2 });

        context.Materials.Add(material);
        context.InnovChatbotMaterialAssetJobs.AddRange(
            new InnovChatbotMaterialAssetJob
            {
                InnovChatbotMaterialId = 10,
                AssetId = 1,
                Pilot = "pilot-1",
                CollectionName = "innov_col_1",
                Status = "completed",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new InnovChatbotMaterialAssetJob
            {
                InnovChatbotMaterialId = 10,
                AssetId = 2,
                Pilot = "pilot-1",
                CollectionName = "innov_col_2",
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

    private sealed class FakeInnovProvider : IChatbotIngestionProvider, IChatbotChatProvider
    {
        public List<string> IngestedFileNames { get; } = new();
        public string? LastExpertiseLevel { get; private set; }

        public string ProviderKey => "innov";

        public Task<bool> IsAvailableAsync(ChatbotConnection connection) => Task.FromResult(true);

        public Task<bool> EnsureGroupingAsync(ChatbotConnection connection, string grouping) => Task.FromResult(true);

        public Task<ChatbotIngestResult> IngestDocumentAsync(ChatbotIngestRequest request)
        {
            IngestedFileNames.Add(request.FileName);
            return Task.FromResult(new ChatbotIngestResult
            {
                Status = "completed",
                CollectionName = "innov_col"
            });
        }

        public Task<ChatbotIngestStatus> GetIngestStatusAsync(ChatbotConnection connection, string grouping, ChatbotDocumentRef document)
            => Task.FromResult(new ChatbotIngestStatus { Status = "completed" });

        public Task<ChatbotChatResult> ChatAsync(ChatbotChatRequest request)
        {
            LastExpertiseLevel = request.ExpertiseLevel;
            return Task.FromResult(new ChatbotChatResult
            {
                Text = $"answer for {request.Grouping}",
                Grouping = request.Grouping
            });
        }

        public Task ClearHistoryAsync(ChatbotConnection connection, string grouping) => Task.CompletedTask;
    }

    private sealed class StubTenantService : IXR50TenantService
    {
        private readonly string _tenantName;

        public StubTenantService(string tenantName)
        {
            _tenantName = tenantName;
        }

        public string GetCurrentTenant() => _tenantName;
        public Task<bool> ValidateTenantAsync(string tenantName) => Task.FromResult(true);
        public Task<bool> TenantExistsAsync(string tenantName) => Task.FromResult(true);
        public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
        public string GetTenantSchema(string tenantName) => $"xr50_tenant_{tenantName}";
    }

    private sealed class StubTenantManagementService : IXR50TenantManagementService
    {
        private readonly XR50Tenant _tenant;

        public StubTenantManagementService(string tenantName, string innovBaseUrl, string innovDefaultPilot)
        {
            _tenant = new XR50Tenant
            {
                TenantName = tenantName,
                InnovChatbotBaseUrl = innovBaseUrl,
                InnovChatbotApiToken = "test-token",
                InnovChatbotDefaultPilot = innovDefaultPilot
            };
        }

        public Task<IEnumerable<XR50Tenant>> GetAllTenantsAsync() => Task.FromResult<IEnumerable<XR50Tenant>>(new[] { _tenant });
        public Task<XR50Tenant> GetTenantAsync(string tenantName) => Task.FromResult(_tenant);
        public Task<XR50Tenant?> GetTenantByHubTenantIdAsync(Guid hubTenantId) => Task.FromResult<XR50Tenant?>(null);
        public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
        public Task<XR50Tenant> UpdateTenantAsync(string tenantName, XR50Tenant tenant) => Task.FromResult(tenant);
        public Task<User> GetOwnerUserAsync(string ownerName, string tenantName) => Task.FromResult<User>(null!);
        public Task DeleteTenantAsync(string tenantName) => Task.CompletedTask;
        public Task DeleteTenantCompletelyAsync(string tenantName) => Task.CompletedTask;
    }
}
