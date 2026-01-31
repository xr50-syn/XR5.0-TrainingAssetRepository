using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models;
using System.Text.RegularExpressions;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Background service that syncs AI processing status from Chatbot API.
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
            var chatbotApiService = scope.ServiceProvider.GetRequiredService<IChatbotApiService>();

            // Check if Chatbot API is available
            if (!await chatbotApiService.IsAvailableAsync())
            {
                _logger.LogDebug("Chatbot API not available, skipping sync");
                return false; // Treat as no jobs to avoid tight loop when API is down
            }

            var tenants = await tenantManagementService.GetAllTenantsAsync();
            var totalProcessingJobs = 0;

            foreach (var tenant in tenants)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var processingCount = await SyncTenantAssetsAsync(
                        chatbotApiService, tenant.TenantName);

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
            IChatbotApiService chatbotApiService,
            string tenantName)
        {
            // Create context directly with fixed tenant (background service has no HttpContext)
            using var context = CreateTenantContext(tenantName);

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
                    var status = await chatbotApiService.GetJobStatusAsync(asset.JobId!);

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
                catch (ChatbotApiException ex)
                {
                    _logger.LogDebug(ex, "Failed to check status for asset {AssetId}", asset.Id);
                    stillProcessingCount++; // Assume still processing on error
                }
            }

            if (updatedAssetIds.Any())
            {
                await context.SaveChangesAsync();

                // Update any AI assistant materials that reference these assets
                await UpdateAIAssistantMaterialStatusesAsync(context, updatedAssetIds);
            }

            return stillProcessingCount;
        }

        private async Task UpdateAIAssistantMaterialStatusesAsync(XR50TrainingContext context, List<int> updatedAssetIds)
        {
            // Get all AI assistant materials in process state
            var aiAssistantMaterials = await context.Materials
                .OfType<AIAssistantMaterial>()
                .Where(a => a.AIAssistantStatus == "process")
                .ToListAsync();

            foreach (var aiAssistant in aiAssistantMaterials)
            {
                var assetIds = aiAssistant.GetAssetIdsList();

                // Check if any of the updated assets are in this AI assistant material
                if (!assetIds.Intersect(updatedAssetIds).Any())
                {
                    continue;
                }

                // Check the status of all assets for this AI assistant material
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

                if (aiAssistant.AIAssistantStatus != newStatus)
                {
                    aiAssistant.AIAssistantStatus = newStatus;
                    aiAssistant.Updated_at = DateTime.UtcNow;

                    _logger.LogInformation("Updated AI assistant material {AIAssistantId} status to {Status}",
                        aiAssistant.id, newStatus);
                }
            }

            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Creates a DbContext for a specific tenant (used in background service without HttpContext)
        /// </summary>
        private XR50TrainingContext CreateTenantContext(string tenantName)
        {
            var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
            var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
            var tenantDbName = GetTenantSchema(tenantName);
            // Use case-insensitive replacement for connection string (database= vs Database=)
            var tenantConnectionString = Regex.Replace(
                baseConnectionString ?? "",
                $"database={Regex.Escape(baseDatabaseName)}",
                $"database={tenantDbName}",
                RegexOptions.IgnoreCase);

            _logger.LogDebug("Created tenant connection for {Tenant}: base={Base}, tenant={TenantDb}",
                tenantName, baseDatabaseName, tenantDbName);

            var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
            optionsBuilder.UseMySql(tenantConnectionString!, ServerVersion.AutoDetect(tenantConnectionString!));

            var tenantService = new DirectTenantService(tenantName);
            return new XR50TrainingContext(optionsBuilder.Options, tenantService, _configuration);
        }

        private static string GetTenantSchema(string tenantName)
        {
            var sanitized = Regex.Replace(tenantName, @"[^a-zA-Z0-9_]", "_");
            return $"xr50_tenant_{sanitized}";
        }

        /// <summary>
        /// Helper class for direct tenant service (no HttpContext required)
        /// </summary>
        private class DirectTenantService : IXR50TenantService
        {
            private readonly string _tenantName;

            public DirectTenantService(string tenantName)
            {
                _tenantName = tenantName;
            }

            public string GetCurrentTenant() => _tenantName;
            public Task<bool> ValidateTenantAsync(string tenantId) => Task.FromResult(true);
            public Task<bool> TenantExistsAsync(string tenantName) => Task.FromResult(true);
            public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
            public string GetTenantSchema(string tenantName)
            {
                var sanitized = Regex.Replace(tenantName, @"[^a-zA-Z0-9_]", "_");
                return $"xr50_tenant_{sanitized}";
            }
        }
    }
}
