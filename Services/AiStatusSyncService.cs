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
        private readonly string _defaultCollectionName;

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

            _defaultCollectionName = configuration["ChatbotApi:DefaultCollectionName"]
                ?? Environment.GetEnvironmentVariable("CHATBOT_API_DEFAULT_COLLECTION")
                ?? "pdf_knowledge_base";
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
        /// Sync this tenant's in-flight per-(material, asset) DataLens jobs.
        /// Returns count of jobs still processing (so the outer loop can adapt its interval).
        /// </summary>
        private async Task<int> SyncTenantAssetsAsync(
            IChatbotApiService chatbotApiService,
            string tenantName)
        {
            using var context = CreateTenantContext(tenantName);

            // Poll only the rows where DataLens is still working. "pending" and "processing"
            // both represent non-terminal states from DataLens' ProcessingStatus enum.
            var inFlightJobs = await context.AIAssistantMaterialAssetJobs
                .Where(j => (j.Status == "pending" || j.Status == "processing")
                            && !string.IsNullOrEmpty(j.JobId))
                .ToListAsync();

            if (!inFlightJobs.Any())
            {
                return 0;
            }

            _logger.LogDebug("Checking {Count} in-flight AI Assistant jobs for tenant {Tenant}",
                inFlightJobs.Count, tenantName);

            var now = DateTime.UtcNow;
            var touchedMaterialIds = new HashSet<int>();
            var stillProcessingCount = 0;

            foreach (var job in inFlightJobs)
            {
                try
                {
                    var status = await chatbotApiService.GetJobStatusAsync(job.JobId!, job.CollectionName);

                    if (status.Status == "completed")
                    {
                        job.Status = "completed";
                        job.ErrorMessage = null;
                        job.UpdatedAt = now;
                        touchedMaterialIds.Add(job.AIAssistantMaterialId);
                        _logger.LogInformation("Job {JobId} completed for material {MaterialId} asset {AssetId} ({Collection})",
                            job.JobId, job.AIAssistantMaterialId, job.AssetId, job.CollectionName);
                    }
                    else if (status.Status == "failed")
                    {
                        job.Status = "failed";
                        job.ErrorMessage = status.Error;
                        job.UpdatedAt = now;
                        touchedMaterialIds.Add(job.AIAssistantMaterialId);
                        _logger.LogWarning("Job {JobId} failed for material {MaterialId} asset {AssetId} ({Collection}): {Error}",
                            job.JobId, job.AIAssistantMaterialId, job.AssetId, job.CollectionName, status.Error);
                    }
                    else if (status.Status == "processing" && job.Status != "processing")
                    {
                        // Reflect DataLens' transition from pending → processing
                        job.Status = "processing";
                        job.UpdatedAt = now;
                        stillProcessingCount++;
                    }
                    else
                    {
                        stillProcessingCount++;
                    }
                }
                catch (ChatbotApiException ex)
                {
                    _logger.LogDebug(ex, "Failed to check status for job {JobId} (material {MaterialId} asset {AssetId})",
                        job.JobId, job.AIAssistantMaterialId, job.AssetId);
                    stillProcessingCount++; // Assume still processing on transient error
                }
            }

            if (touchedMaterialIds.Any())
            {
                await context.SaveChangesAsync();
                await UpdateAIAssistantMaterialStatusesAsync(context, touchedMaterialIds);
            }
            else
            {
                // "processing" transitions still need a flush
                await context.SaveChangesAsync();
            }

            return stillProcessingCount;
        }

        /// <summary>
        /// Recompute AIAssistantStatus for each material whose job rows just changed.
        /// Mirrors AIAssistantMaterialService.CalculateAggregateStatusAsync.
        /// </summary>
        private async Task UpdateAIAssistantMaterialStatusesAsync(XR50TrainingContext context, HashSet<int> materialIds)
        {
            if (!materialIds.Any()) return;

            var materials = await context.Materials
                .OfType<AIAssistantMaterial>()
                .Where(m => materialIds.Contains(m.id))
                .ToListAsync();

            foreach (var material in materials)
            {
                var assetIds = material.GetAssetIdsList();
                var jobStatuses = await context.AIAssistantMaterialAssetJobs
                    .Where(j => j.AIAssistantMaterialId == material.id && assetIds.Contains(j.AssetId))
                    .Select(j => j.Status)
                    .ToListAsync();

                string newStatus;
                if (!assetIds.Any() || jobStatuses.Count < assetIds.Count)
                {
                    newStatus = "notready";
                }
                else if (jobStatuses.Any(s => s == "pending" || s == "processing"))
                {
                    newStatus = "process";
                }
                else if (jobStatuses.All(s => s == "completed"))
                {
                    newStatus = "ready";
                }
                else
                {
                    newStatus = "notready";
                }

                if (material.AIAssistantStatus != newStatus)
                {
                    material.AIAssistantStatus = newStatus;
                    material.Updated_at = DateTime.UtcNow;

                    _logger.LogInformation("Updated AI assistant material {AIAssistantId} status to {Status}",
                        material.id, newStatus);
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
