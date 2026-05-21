using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services.Chatbot;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// INNOV chatbot material service. Mirrors the AIAssistant ingest/status pattern (per-(material,
    /// asset) job rows, aggregate status) but targets the INNOV "LLM Engine" through the
    /// <see cref="IChatbotProvider"/> seam, with connection details resolved per tenant.
    /// </summary>
    public class InnovChatbotMaterialService : IInnovChatbotMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly IChatbotIngestionProvider _ingestionProvider;
        private readonly IChatbotChatProvider _chatProvider;
        private readonly IXR50TenantService _tenantService;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly ILogger<InnovChatbotMaterialService> _logger;

        public InnovChatbotMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            IEnumerable<IChatbotProvider> providers,
            IXR50TenantService tenantService,
            IXR50TenantManagementService tenantManagementService,
            ILogger<InnovChatbotMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _tenantService = tenantService;
            _tenantManagementService = tenantManagementService;
            _logger = logger;

            // Resolve the INNOV backend through the shared provider seam.
            var innov = providers.FirstOrDefault(p => p.ProviderKey == "innov")
                ?? throw new InvalidOperationException("No chatbot provider registered with key 'innov'.");
            _ingestionProvider = innov as IChatbotIngestionProvider
                ?? throw new InvalidOperationException("INNOV provider does not support ingestion.");
            _chatProvider = innov as IChatbotChatProvider
                ?? throw new InvalidOperationException("INNOV provider does not support chat.");
        }

        #region Tenant connection / pilot resolution

        private async Task<XR50Tenant> GetTenantAsync()
        {
            var tenantName = _tenantService.GetCurrentTenant();
            var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
            if (tenant == null)
            {
                throw new InvalidOperationException($"Tenant '{tenantName}' not found.");
            }
            return tenant;
        }

        private static ChatbotConnection ToConnection(XR50Tenant tenant)
        {
            if (string.IsNullOrEmpty(tenant.InnovChatbotBaseUrl))
            {
                throw new InvalidOperationException(
                    $"Tenant '{tenant.TenantName}' has no INNOV chatbot base URL configured.");
            }
            return new ChatbotConnection
            {
                BaseUrl = tenant.InnovChatbotBaseUrl,
                ApiToken = tenant.InnovChatbotApiToken
            };
        }

        private static string ResolvePilot(InnovChatbotMaterial material, XR50Tenant tenant)
        {
            var pilot = !string.IsNullOrEmpty(material.Pilot) ? material.Pilot : tenant.InnovChatbotDefaultPilot;
            if (string.IsNullOrEmpty(pilot))
            {
                throw new InvalidOperationException(
                    $"INNOV chatbot material {material.id} has no pilot and tenant '{tenant.TenantName}' has no default pilot configured.");
            }
            return pilot;
        }

        #endregion

        #region CRUD Operations

        public async Task<IEnumerable<InnovChatbotMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<InnovChatbotMaterial>()
                .ToListAsync();
        }

        public async Task<InnovChatbotMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == id);
        }

        public async Task<InnovChatbotMaterial> CreateAsync(InnovChatbotMaterial material)
        {
            using var context = _dbContextFactory.CreateDbContext();

            material.Created_at = DateTime.UtcNow;
            material.Updated_at = DateTime.UtcNow;
            material.Type = MaterialType.InnovChatbot;

            // Bind to the tenant default pilot when not explicitly set, so chat/ingest route there.
            if (string.IsNullOrEmpty(material.Pilot))
            {
                var tenant = await GetTenantAsync();
                if (!string.IsNullOrEmpty(tenant.InnovChatbotDefaultPilot))
                {
                    material.Pilot = tenant.InnovChatbotDefaultPilot;
                }
            }
            if (string.IsNullOrEmpty(material.InnovStatus))
            {
                material.InnovStatus = "notready";
            }

            context.Materials.Add(material);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created INNOV chatbot material: {Name} with ID: {Id} bound to pilot {Pilot}",
                material.Name, material.id, material.Pilot);

            return material;
        }

        public async Task<InnovChatbotMaterial> UpdateAsync(InnovChatbotMaterial material)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var existing = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == material.id);

            if (existing == null)
            {
                throw new KeyNotFoundException($"INNOV chatbot material {material.id} not found");
            }

            material.Created_at = existing.Created_at;
            material.Unique_id = existing.Unique_id;
            material.Type = MaterialType.InnovChatbot;
            material.Updated_at = DateTime.UtcNow;

            context.Entry(existing).CurrentValues.SetValues(material);
            await context.SaveChangesAsync();

            _logger.LogInformation("Updated INNOV chatbot material: {Id} ({Name})", material.id, material.Name);

            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == id);

            if (material == null)
            {
                return false;
            }

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            // Asset job rows are removed by cascade on the Materials FK.

            context.Materials.Remove(material);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted INNOV chatbot material: {Id}", id);

            return true;
        }

        #endregion

        #region Ingestion / asset management

        public async Task<(InnovChatbotMaterial Material, List<string> Warnings)> CreateWithAssetsAsync(
            InnovChatbotMaterial material, List<int> assetIds)
        {
            var warnings = new List<string>();

            using (var context = _dbContextFactory.CreateDbContext())
            {
                material.Created_at = DateTime.UtcNow;
                material.Updated_at = DateTime.UtcNow;
                material.Type = MaterialType.InnovChatbot;
                material.InnovStatus = "notready";
                material.SetAssetIdsList(assetIds);

                if (string.IsNullOrEmpty(material.Pilot))
                {
                    var tenant = await GetTenantAsync();
                    if (!string.IsNullOrEmpty(tenant.InnovChatbotDefaultPilot))
                    {
                        material.Pilot = tenant.InnovChatbotDefaultPilot;
                    }
                }

                context.Materials.Add(material);
                await context.SaveChangesAsync();
            }

            _logger.LogInformation("Created INNOV chatbot material: {Name} with ID: {Id}, pilot: {Pilot}, and {AssetCount} assets",
                material.Name, material.id, material.Pilot, assetIds.Count);

            if (assetIds.Any())
            {
                try
                {
                    await SubmitForProcessingAsync(material.id);
                    _logger.LogInformation("Auto-submitted INNOV chatbot material {Id} for processing", material.id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Auto-submission failed for INNOV chatbot material {Id}. Manual submission required.", material.id);
                    warnings.Add($"Auto-submission failed: {ex.Message}");
                }
            }

            var reloaded = await GetByIdAsync(material.id) ?? material;
            return (reloaded, warnings);
        }

        public async Task<bool> AddAssetAsync(int materialId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == materialId);

            if (material == null)
            {
                return false;
            }

            var assetIds = material.GetAssetIdsList();
            if (!assetIds.Contains(assetId))
            {
                assetIds.Add(assetId);
                material.SetAssetIdsList(assetIds);
                material.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();

                _logger.LogInformation("Added asset {AssetId} to INNOV chatbot material {MaterialId}", assetId, materialId);
            }

            return true;
        }

        public async Task<bool> RemoveAssetAsync(int materialId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == materialId);

            if (material == null)
            {
                return false;
            }

            var assetIds = material.GetAssetIdsList();
            if (assetIds.Remove(assetId))
            {
                material.SetAssetIdsList(assetIds);
                material.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();

                _logger.LogInformation("Removed asset {AssetId} from INNOV chatbot material {MaterialId}", assetId, materialId);
            }

            return true;
        }

        public async Task<List<int>> GetAssetIdsAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == materialId);

            return material?.GetAssetIdsList() ?? new List<int>();
        }

        public async Task<IEnumerable<Asset>> GetAssetsAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == materialId);

            if (material == null)
            {
                return Enumerable.Empty<Asset>();
            }

            var assetIds = material.GetAssetIdsList();
            if (!assetIds.Any())
            {
                return Enumerable.Empty<Asset>();
            }

            return await context.Assets
                .Where(a => assetIds.Contains(a.Id))
                .ToListAsync();
        }

        #endregion

        #region Status operations

        public async Task<InnovChatbotMaterial> SubmitForProcessingAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == materialId);

            if (material == null)
            {
                throw new KeyNotFoundException($"INNOV chatbot material {materialId} not found");
            }

            var assetIds = material.GetAssetIdsList();

            // Load existing per-asset job rows; prune orphans whose AssetId was removed (e.g. via PUT).
            var allJobs = await context.InnovChatbotMaterialAssetJobs
                .Where(j => j.InnovChatbotMaterialId == materialId)
                .ToListAsync();
            var orphanJobs = allJobs.Where(j => !assetIds.Contains(j.AssetId)).ToList();
            if (orphanJobs.Any())
            {
                context.InnovChatbotMaterialAssetJobs.RemoveRange(orphanJobs);
                _logger.LogInformation("Pruned {Count} orphan job row(s) for INNOV chatbot material {MaterialId}",
                    orphanJobs.Count, materialId);
            }
            var existingJobs = allJobs.Except(orphanJobs).ToList();

            if (!assetIds.Any())
            {
                if (material.InnovStatus != "notready")
                {
                    material.InnovStatus = "notready";
                    material.Updated_at = DateTime.UtcNow;
                }
                await context.SaveChangesAsync();
                return material;
            }

            var tenant = await GetTenantAsync();
            var connection = ToConnection(tenant);
            var pilot = ResolvePilot(material, tenant);

            await _ingestionProvider.EnsureGroupingAsync(connection, pilot);

            var assets = await context.Assets
                .Where(a => assetIds.Contains(a.Id))
                .ToListAsync();

            var now = DateTime.UtcNow;
            var successCount = 0;
            var failedAssets = new List<(int AssetId, string Error)>();
            var skippedCount = 0;

            foreach (var asset in assets)
            {
                var existingJob = existingJobs.FirstOrDefault(j => j.AssetId == asset.Id);

                // Skip assets already ingested into this pilot (non-failed). Failed jobs and
                // pilot changes are re-submitted.
                if (existingJob != null
                    && existingJob.Status != "failed"
                    && string.Equals(existingJob.Pilot, pilot, StringComparison.Ordinal))
                {
                    skippedCount++;
                    continue;
                }

                if (string.IsNullOrEmpty(asset.URL))
                {
                    failedAssets.Add((asset.Id, "asset has no URL"));
                    continue;
                }

                try
                {
                    var result = await _ingestionProvider.IngestDocumentAsync(new ChatbotIngestRequest
                    {
                        Connection = connection,
                        Grouping = pilot,
                        FileName = asset.Filename ?? $"asset-{asset.Id}",
                        ContentType = null,
                        Filetype = asset.Filetype,
                        SourceUrl = asset.URL,
                        DocType = asset.Filetype,
                        DocTitle = asset.Filename
                    });

                    var status = result.Status == "failed" ? "failed" : result.Status;

                    if (existingJob == null)
                    {
                        context.InnovChatbotMaterialAssetJobs.Add(new InnovChatbotMaterialAssetJob
                        {
                            InnovChatbotMaterialId = materialId,
                            AssetId = asset.Id,
                            Pilot = pilot,
                            CollectionName = result.CollectionName,
                            Status = status,
                            ErrorMessage = result.ErrorMessage,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                    else
                    {
                        existingJob.Pilot = pilot;
                        existingJob.CollectionName = result.CollectionName;
                        existingJob.Status = status;
                        existingJob.ErrorMessage = result.ErrorMessage;
                        existingJob.UpdatedAt = now;
                    }

                    if (status == "failed")
                    {
                        failedAssets.Add((asset.Id, result.ErrorMessage ?? "ingestion failed"));
                    }
                    else
                    {
                        successCount++;
                        _logger.LogInformation("Submitted asset {AssetId} for INNOV material {MaterialId} to pilot {Pilot} (status {Status})",
                            asset.Id, materialId, pilot, status);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to submit asset {AssetId} for INNOV material {MaterialId}", asset.Id, materialId);
                    failedAssets.Add((asset.Id, ex.Message));

                    if (existingJob == null)
                    {
                        context.InnovChatbotMaterialAssetJobs.Add(new InnovChatbotMaterialAssetJob
                        {
                            InnovChatbotMaterialId = materialId,
                            AssetId = asset.Id,
                            Pilot = pilot,
                            CollectionName = null,
                            Status = "failed",
                            ErrorMessage = ex.Message,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                    else
                    {
                        existingJob.Pilot = pilot;
                        existingJob.Status = "failed";
                        existingJob.ErrorMessage = ex.Message;
                        existingJob.UpdatedAt = now;
                    }
                }
            }

            await context.SaveChangesAsync();

            if (successCount == 0 && failedAssets.Any())
            {
                var errorDetails = string.Join("; ", failedAssets.Select(f => $"Asset {f.AssetId}: {f.Error}"));
                throw new InvalidOperationException($"Failed to submit all assets for processing. Errors: {errorDetails}");
            }

            var aggregateStatus = await CalculateAggregateStatusAsync(context, material);
            if (material.InnovStatus != aggregateStatus)
            {
                material.InnovStatus = aggregateStatus;
                material.Updated_at = now;
                await context.SaveChangesAsync();
            }

            if (failedAssets.Any())
            {
                _logger.LogWarning("Partial submission for INNOV material {MaterialId}: {SuccessCount} succeeded, {FailedCount} failed, {SkippedCount} already ingested",
                    materialId, successCount, failedAssets.Count, skippedCount);
            }

            return material;
        }

        public async Task<InnovChatbotMaterial> UpdateStatusFromAssetsAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == materialId);

            if (material == null)
            {
                throw new KeyNotFoundException($"INNOV chatbot material {materialId} not found");
            }

            var newStatus = await CalculateAggregateStatusAsync(context, material);

            if (material.InnovStatus != newStatus)
            {
                material.InnovStatus = newStatus;
                material.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();

                _logger.LogInformation("Updated INNOV chatbot material {MaterialId} status to {Status}", materialId, newStatus);
            }

            return material;
        }

        public async Task<List<InnovChatbotMaterialAssetJob>> GetAssetJobsAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.InnovChatbotMaterialAssetJobs
                .Where(j => j.InnovChatbotMaterialId == materialId)
                .ToListAsync();
        }

        public async Task<string> GetAggregateStatusAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<InnovChatbotMaterial>()
                .FirstOrDefaultAsync(m => m.id == materialId);

            if (material == null)
            {
                return "notready";
            }

            return await CalculateAggregateStatusAsync(context, material);
        }

        private async Task<string> CalculateAggregateStatusAsync(XR50TrainingContext context, InnovChatbotMaterial material)
        {
            var assetIds = material.GetAssetIdsList();
            if (!assetIds.Any())
            {
                return "notready";
            }

            var jobStatuses = await context.InnovChatbotMaterialAssetJobs
                .Where(j => j.InnovChatbotMaterialId == material.id && assetIds.Contains(j.AssetId))
                .Select(j => j.Status)
                .ToListAsync();

            if (jobStatuses.Count < assetIds.Count)
            {
                return "notready";
            }

            if (jobStatuses.Any(s => s == "pending" || s == "processing"))
            {
                return "process";
            }

            if (jobStatuses.All(s => s == "completed"))
            {
                return "ready";
            }

            return "notready";
        }

        #endregion

        #region Chat / documents

        public async Task<InnovChatResponse> ChatAsync(int materialId, string query, string? expertiseLevel = null)
        {
            var material = await GetByIdAsync(materialId);
            if (material == null)
            {
                throw new KeyNotFoundException($"INNOV chatbot material {materialId} not found");
            }

            var tenant = await GetTenantAsync();
            var connection = ToConnection(tenant);
            var pilot = ResolvePilot(material, tenant);

            var result = await _chatProvider.ChatAsync(new ChatbotChatRequest
            {
                Connection = connection,
                Grouping = pilot,
                Query = query,
                ExpertiseLevel = expertiseLevel ?? material.ExpertiseLevel
            });

            return new InnovChatResponse
            {
                Query = query,
                Text = result.Text,
                Pilot = result.Grouping ?? pilot,
                Sources = result.Sources,
                Images = result.Images,
                TokensUsed = result.TokensUsed,
                ProcessingTime = result.ProcessingTime
            };
        }

        public async Task<InnovDocumentUploadResponse> UploadDocumentAsync(
            int materialId, Stream fileStream, string fileName, string contentType)
        {
            var material = await GetByIdAsync(materialId);
            if (material == null)
            {
                throw new KeyNotFoundException($"INNOV chatbot material {materialId} not found");
            }

            var tenant = await GetTenantAsync();
            var connection = ToConnection(tenant);
            var pilot = ResolvePilot(material, tenant);

            var result = await _ingestionProvider.IngestDocumentAsync(new ChatbotIngestRequest
            {
                Connection = connection,
                Grouping = pilot,
                FileName = fileName,
                ContentType = contentType,
                Content = fileStream,
                DocTitle = fileName
            });

            return new InnovDocumentUploadResponse
            {
                Status = result.Status,
                Message = "Document submitted to INNOV pilot",
                CollectionName = result.CollectionName,
                Pilot = pilot
            };
        }

        public async Task ClearHistoryAsync(int materialId)
        {
            var material = await GetByIdAsync(materialId);
            if (material == null)
            {
                throw new KeyNotFoundException($"INNOV chatbot material {materialId} not found");
            }

            var tenant = await GetTenantAsync();
            var connection = ToConnection(tenant);
            var pilot = ResolvePilot(material, tenant);

            await _chatProvider.ClearHistoryAsync(connection, pilot);
        }

        public async Task<bool> IsEndpointAvailableAsync(int materialId)
        {
            var material = await GetByIdAsync(materialId);
            if (material == null)
            {
                return false;
            }

            try
            {
                var tenant = await GetTenantAsync();
                var connection = ToConnection(tenant);
                return await _ingestionProvider.IsAvailableAsync(connection);
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
