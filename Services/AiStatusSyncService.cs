using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Background service that syncs AI processing status from Siemens API.
    ///
    /// Uses a database-driven approach:
    /// - Queries database to check if any jobs are processing
    /// - When idle (no jobs), checks infrequently (default: every 5 minutes)
    /// - When active (jobs processing), polls frequently (default: every 15 seconds)
    /// - No in-memory state - survives restarts, works across multiple instances
    /// </summary>
    public class AiStatusSyncService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AiStatusSyncService> _logger;
        private readonly IConfiguration _configuration;

        // Polling intervals
        private readonly TimeSpan _activePollingInterval;
        private readonly TimeSpan _idleCheckInterval;

        public AiStatusSyncService(
            IServiceProvider serviceProvider,
            ILogger<AiStatusSyncService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;

            // Active polling: when jobs are being processed (default 15 seconds)
            var activeSeconds = configuration.GetValue<int>("AiStatusSync:ActiveIntervalSeconds", 15);
            _activePollingInterval = TimeSpan.FromSeconds(activeSeconds);

            // Idle check: when no jobs are processing (default 5 minutes)
            var idleMinutes = configuration.GetValue<int>("AiStatusSync:IdleIntervalMinutes", 5);
            _idleCheckInterval = TimeSpan.FromMinutes(idleMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "AI Status Sync Service starting. Active interval: {Active}s, Idle interval: {Idle}m",
                _activePollingInterval.TotalSeconds, _idleCheckInterval.TotalMinutes);

            // Initial delay to let the application start up
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var hasProcessingJobs = await SyncAllTenantsAsync(stoppingToken);

                    // Adaptive interval: poll frequently when jobs exist, slowly when idle
                    var nextInterval = hasProcessingJobs ? _activePollingInterval : _idleCheckInterval;

                    if (!hasProcessingJobs)
                    {
                        _logger.LogDebug("No processing jobs found. Next check in {Minutes} minutes",
                            _idleCheckInterval.TotalMinutes);
                    }

                    await Task.Delay(nextInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during AI status sync");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }

            _logger.LogInformation("AI Status Sync Service stopping");
        }

        /// <summary>
        /// Sync all tenants and return whether any jobs are still processing.
        /// </summary>
        private async Task<bool> SyncAllTenantsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();

            var tenantManagementService = scope.ServiceProvider.GetRequiredService<IXR50TenantManagementService>();
            var siemensApiService = scope.ServiceProvider.GetRequiredService<ISiemensApiService>();

            // Check if Siemens API is available
            if (!await siemensApiService.IsAvailableAsync())
            {
                _logger.LogDebug("Siemens API not available, skipping sync");
                return false; // Treat as no jobs to avoid tight loop when API is down
            }

            var tenants = await tenantManagementService.GetAllTenantsAsync();
            var totalProcessingJobs = 0;

            foreach (var tenant in tenants)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    using var tenantScope = _serviceProvider.CreateScope();
                    var dbContextFactory = tenantScope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();

                    var processingCount = await SyncTenantAssetsAsync(
                        dbContextFactory, siemensApiService, tenant.TenantName);

                    totalProcessingJobs += processingCount;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error syncing tenant {TenantName}", tenant.TenantName);
                }
            }

            return totalProcessingJobs > 0;
        }

        /// <summary>
        /// Sync assets for a single tenant. Returns count of jobs still processing.
        /// </summary>
        private async Task<int> SyncTenantAssetsAsync(
            IXR50TenantDbContextFactory dbContextFactory,
            ISiemensApiService siemensApiService,
            string tenantName)
        {
            using var context = dbContextFactory.CreateDbContext();

            // Query database for processing assets
            var processingAssets = await context.Assets
                .Where(a => a.AiAvailable == "process" && !string.IsNullOrEmpty(a.JobId))
                .ToListAsync();

            if (!processingAssets.Any())
            {
                return 0;
            }

            _logger.LogDebug("Checking {Count} processing assets for tenant {Tenant}",
                processingAssets.Count, tenantName);

            var updatedAssetIds = new List<int>();
            var stillProcessingCount = 0;

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
                    else
                    {
                        // Still pending or processing
                        stillProcessingCount++;
                    }
                }
                catch (SiemensApiException ex)
                {
                    _logger.LogDebug(ex, "Failed to check status for asset {AssetId}", asset.Id);
                    stillProcessingCount++; // Assume still processing on error
                }
            }

            if (updatedAssetIds.Any())
            {
                await context.SaveChangesAsync();

                // Update any voice materials that reference these assets
                await UpdateVoiceMaterialStatusesAsync(context, updatedAssetIds);
            }

            return stillProcessingCount;
        }

        private async Task UpdateVoiceMaterialStatusesAsync(XR50TrainingContext context, List<int> updatedAssetIds)
        {
            // Get all voice materials in process state
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
                var assetStatuses = await context.Assets
                    .Where(a => assetIds.Contains(a.Id))
                    .Select(a => a.AiAvailable)
                    .ToListAsync();

                if (!assetStatuses.Any())
                {
                    continue;
                }

                // Determine new status
                string newStatus;
                if (assetStatuses.Any(s => s == "process"))
                {
                    newStatus = "process";
                }
                else if (assetStatuses.All(s => s == "ready"))
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
