using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Background service that periodically syncs AI processing status from Siemens API.
    /// Checks status of assets in "process" state and updates voice materials accordingly.
    /// </summary>
    public class AiStatusSyncService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AiStatusSyncService> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _syncInterval;

        public AiStatusSyncService(
            IServiceProvider serviceProvider,
            ILogger<AiStatusSyncService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;

            // Default to 30 seconds, configurable via appsettings
            var intervalSeconds = configuration.GetValue<int>("AiStatusSync:IntervalSeconds", 30);
            _syncInterval = TimeSpan.FromSeconds(intervalSeconds);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AI Status Sync Service starting. Sync interval: {Interval}s", _syncInterval.TotalSeconds);

            // Initial delay to let the application start up
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncAllTenantsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during AI status sync");
                }

                await Task.Delay(_syncInterval, stoppingToken);
            }

            _logger.LogInformation("AI Status Sync Service stopping");
        }

        private async Task SyncAllTenantsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();

            var tenantManagementService = scope.ServiceProvider.GetRequiredService<IXR50TenantManagementService>();
            var tenantService = scope.ServiceProvider.GetRequiredService<IXR50TenantService>();
            var siemensApiService = scope.ServiceProvider.GetRequiredService<ISiemensApiService>();

            // Check if Siemens API is available
            if (!await siemensApiService.IsAvailableAsync())
            {
                _logger.LogDebug("Siemens API not available, skipping sync");
                return;
            }

            // Get all tenants from management service
            var tenants = await tenantManagementService.GetAllTenantsAsync();

            foreach (var tenant in tenants)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    // Create a new scope for each tenant to get proper tenant-scoped context
                    using var tenantScope = _serviceProvider.CreateScope();
                    var dbContextFactory = tenantScope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();

                    await SyncTenantAssetsAsync(dbContextFactory, siemensApiService, tenantService, tenant.TenantName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error syncing tenant {TenantName}", tenant.TenantName);
                }
            }
        }

        private async Task SyncTenantAssetsAsync(
            IXR50TenantDbContextFactory dbContextFactory,
            ISiemensApiService siemensApiService,
            IXR50TenantService tenantService,
            string tenantName)
        {
            using var context = dbContextFactory.CreateDbContext();

            // Get all assets currently processing
            var processingAssets = await context.Assets
                .Where(a => a.AiAvailable == "process" && !string.IsNullOrEmpty(a.JobId))
                .ToListAsync();

            if (!processingAssets.Any())
            {
                return;
            }

            _logger.LogDebug("Checking {Count} processing assets for tenant {Tenant}",
                processingAssets.Count, tenantName);

            var updatedAssetIds = new List<int>();

            foreach (var asset in processingAssets)
            {
                try
                {
                    var status = await siemensApiService.GetJobStatusAsync(asset.JobId!);

                    if (status.Status == "success")
                    {
                        asset.AiAvailable = "ready";
                        updatedAssetIds.Add(asset.Id);
                        _logger.LogInformation("Asset {AssetId} AI processing completed for tenant {Tenant}",
                            asset.Id, tenantName);
                    }
                    else if (status.Status == "failed")
                    {
                        asset.AiAvailable = "notready";
                        asset.JobId = null;
                        updatedAssetIds.Add(asset.Id);
                        _logger.LogWarning("Asset {AssetId} AI processing failed for tenant {Tenant}: {Error}",
                            asset.Id, tenantName, status.Error);
                    }
                }
                catch (SiemensApiException ex)
                {
                    _logger.LogDebug(ex, "Failed to check status for asset {AssetId}", asset.Id);
                }
            }

            if (updatedAssetIds.Any())
            {
                await context.SaveChangesAsync();

                // Update any voice materials that reference these assets
                await UpdateVoiceMaterialStatusesAsync(context, updatedAssetIds);
            }
        }

        private async Task UpdateVoiceMaterialStatusesAsync(XR50TrainingContext context, List<int> updatedAssetIds)
        {
            // Get all voice materials
            var voiceMaterials = await context.Materials
                .OfType<VoiceMaterial>()
                .Where(v => v.VoiceStatus == "process")
                .ToListAsync();

            foreach (var voice in voiceMaterials)
            {
                var assetIds = voice.GetAssetIdsList();

                // Check if any of the updated assets are in this voice material
                if (!assetIds.Intersect(updatedAssetIds).Any())
                {
                    continue;
                }

                // Check the status of all assets for this voice material
                var assets = await context.Assets
                    .Where(a => assetIds.Contains(a.Id))
                    .Select(a => a.AiAvailable)
                    .ToListAsync();

                if (!assets.Any())
                {
                    continue;
                }

                // Determine new status
                string newStatus;
                if (assets.Any(s => s == "process"))
                {
                    newStatus = "process";
                }
                else if (assets.All(s => s == "ready"))
                {
                    newStatus = "ready";
                }
                else
                {
                    newStatus = "notready";
                }

                if (voice.VoiceStatus != newStatus)
                {
                    voice.VoiceStatus = newStatus;
                    voice.Updated_at = DateTime.UtcNow;

                    _logger.LogInformation("Updated voice material {VoiceId} status to {Status}",
                        voice.id, newStatus);
                }
            }

            await context.SaveChangesAsync();
        }
    }
}
