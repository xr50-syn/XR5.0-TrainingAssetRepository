using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using System.Security.Cryptography;
using System.Text;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for AI Assistant material operations with multi-asset support, AI processing, and session management.
    /// </summary>
    public class AIAssistantMaterialService : IAIAssistantMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly IChatbotApiService _chatbotApiService;
        private readonly ILogger<AIAssistantMaterialService> _logger;

        public AIAssistantMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            IChatbotApiService chatbotApiService,
            ILogger<AIAssistantMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _chatbotApiService = chatbotApiService;
            _logger = logger;
        }

        #region CRUD Operations

        public async Task<IEnumerable<AIAssistantMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<AIAssistantMaterial>()
                .ToListAsync();
        }

        public async Task<AIAssistantMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == id);
        }

        public async Task<AIAssistantMaterial> CreateAsync(AIAssistantMaterial aiAssistant)
        {
            using var context = _dbContextFactory.CreateDbContext();

            aiAssistant.Created_at = DateTime.UtcNow;
            aiAssistant.Updated_at = DateTime.UtcNow;
            aiAssistant.Type = MaterialType.AIAssistant;

            if (string.IsNullOrEmpty(aiAssistant.AIAssistantStatus))
            {
                aiAssistant.AIAssistantStatus = "notready";
            }

            context.Materials.Add(aiAssistant);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created AI Assistant material: {Name} with ID: {Id}", aiAssistant.Name, aiAssistant.id);

            return aiAssistant;
        }

        public async Task<AIAssistantMaterial> UpdateAsync(AIAssistantMaterial aiAssistant)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var existing = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistant.id);

            if (existing == null)
            {
                throw new KeyNotFoundException($"AI Assistant material {aiAssistant.id} not found");
            }

            // Preserve immutable fields
            aiAssistant.Created_at = existing.Created_at;
            aiAssistant.Unique_id = existing.Unique_id;
            aiAssistant.Type = MaterialType.AIAssistant;
            aiAssistant.Updated_at = DateTime.UtcNow;

            context.Entry(existing).CurrentValues.SetValues(aiAssistant);
            await context.SaveChangesAsync();

            _logger.LogInformation("Updated AI Assistant material: {Id} ({Name})", aiAssistant.id, aiAssistant.Name);

            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == id);

            if (aiAssistant == null)
            {
                return false;
            }

            // Clean up material relationships
            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            // Sessions are deleted by cascade

            context.Materials.Remove(aiAssistant);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted AI Assistant material: {Id}", id);

            return true;
        }

        #endregion

        #region AI Assistant-specific Operations

        public async Task<AIAssistantMaterial> CreateWithAssetsAsync(AIAssistantMaterial aiAssistant, List<int> assetIds)
        {
            using var context = _dbContextFactory.CreateDbContext();

            aiAssistant.Created_at = DateTime.UtcNow;
            aiAssistant.Updated_at = DateTime.UtcNow;
            aiAssistant.Type = MaterialType.AIAssistant;
            aiAssistant.AIAssistantStatus = "notready";
            aiAssistant.SetAssetIdsList(assetIds);

            context.Materials.Add(aiAssistant);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created AI Assistant material: {Name} with ID: {Id} and {AssetCount} assets",
                aiAssistant.Name, aiAssistant.id, assetIds.Count);

            // Automatically submit assets for AI processing
            if (assetIds.Any())
            {
                try
                {
                    await SubmitForProcessingAsync(aiAssistant.id);
                    _logger.LogInformation("Auto-submitted AI Assistant material {Id} for AI processing", aiAssistant.id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Auto-submission failed for AI Assistant material {Id}. Manual submission required.", aiAssistant.id);
                    // Don't throw - material is created, user can retry submission manually
                }
            }

            // Reload to get updated status
            return await GetByIdAsync(aiAssistant.id) ?? aiAssistant;
        }

        public async Task<AIAssistantMaterial?> GetWithAssetsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .Include(a => a.CurrentSession)
                .FirstOrDefaultAsync(a => a.id == id);

            return aiAssistant;
        }

        #endregion

        #region Asset Management

        public async Task<bool> AddAssetAsync(int aiAssistantId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistantId);

            if (aiAssistant == null)
            {
                return false;
            }

            var assetIds = aiAssistant.GetAssetIdsList();
            if (!assetIds.Contains(assetId))
            {
                assetIds.Add(assetId);
                aiAssistant.SetAssetIdsList(assetIds);
                aiAssistant.Updated_at = DateTime.UtcNow;

                // Invalidate session when assets change
                await InvalidateSessionInternalAsync(context, aiAssistantId);

                await context.SaveChangesAsync();

                _logger.LogInformation("Added asset {AssetId} to AI Assistant material {AIAssistantId}. Session invalidated.", assetId, aiAssistantId);
            }

            return true;
        }

        public async Task<bool> RemoveAssetAsync(int aiAssistantId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistantId);

            if (aiAssistant == null)
            {
                return false;
            }

            var assetIds = aiAssistant.GetAssetIdsList();
            if (assetIds.Remove(assetId))
            {
                aiAssistant.SetAssetIdsList(assetIds);
                aiAssistant.Updated_at = DateTime.UtcNow;

                // Invalidate session when assets change
                await InvalidateSessionInternalAsync(context, aiAssistantId);

                await context.SaveChangesAsync();

                _logger.LogInformation("Removed asset {AssetId} from AI Assistant material {AIAssistantId}. Session invalidated.", assetId, aiAssistantId);
            }

            return true;
        }

        public async Task<List<int>> GetAssetIdsAsync(int aiAssistantId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistantId);

            return aiAssistant?.GetAssetIdsList() ?? new List<int>();
        }

        public async Task<IEnumerable<Asset>> GetAssetsAsync(int aiAssistantId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistantId);

            if (aiAssistant == null)
            {
                return Enumerable.Empty<Asset>();
            }

            var assetIds = aiAssistant.GetAssetIdsList();
            if (!assetIds.Any())
            {
                return Enumerable.Empty<Asset>();
            }

            return await context.Assets
                .Where(a => assetIds.Contains(a.Id))
                .ToListAsync();
        }

        #endregion

        #region AI Status Operations

        public async Task<AIAssistantMaterial> SubmitForProcessingAsync(int aiAssistantId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistantId);

            if (aiAssistant == null)
            {
                throw new KeyNotFoundException($"AI Assistant material {aiAssistantId} not found");
            }

            var assetIds = aiAssistant.GetAssetIdsList();
            if (!assetIds.Any())
            {
                throw new InvalidOperationException("AI Assistant material has no assets to submit");
            }

            // Get assets and submit each for processing
            var assets = await context.Assets
                .Where(a => assetIds.Contains(a.Id))
                .ToListAsync();

            var successCount = 0;
            var failedAssets = new List<(int AssetId, string Error)>();
            var skippedCount = 0;

            foreach (var asset in assets)
            {
                if (asset.AiAvailable == "notready" && !string.IsNullOrEmpty(asset.URL))
                {
                    try
                    {
                        var jobId = await _chatbotApiService.SubmitDocumentAsync(asset.Id, asset.URL, asset.Filetype ?? "pdf");
                        asset.JobId = jobId;

                        if (jobId.StartsWith("duplicate-accepted-"))
                        {
                            asset.AiAvailable = "ready";
                            _logger.LogInformation("Asset {AssetId} already exists in AI service, marked as ready",
                                asset.Id);
                        }
                        else
                        {
                            asset.AiAvailable = "process";
                            _logger.LogInformation("Submitted asset {AssetId} for processing. Job ID: {JobId}",
                                asset.Id, jobId);
                        }

                        successCount++;
                    }
                    catch (ChatbotApiException ex)
                    {
                        _logger.LogWarning(ex, "Failed to submit asset {AssetId} for processing", asset.Id);
                        failedAssets.Add((asset.Id, ex.Message));
                    }
                }
                else
                {
                    skippedCount++;
                }
            }

            // If all eligible assets failed, throw an error with details
            if (successCount == 0 && failedAssets.Any())
            {
                var errorDetails = string.Join("; ", failedAssets.Select(f => $"Asset {f.AssetId}: {f.Error}"));
                throw new InvalidOperationException($"Failed to submit all assets for processing. Errors: {errorDetails}");
            }

            if (successCount > 0)
            {
                aiAssistant.AIAssistantStatus = "process";
                aiAssistant.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();

                // Log if there were partial failures
                if (failedAssets.Any())
                {
                    _logger.LogWarning("Partial submission failure for AI Assistant material {AIAssistantId}: {SuccessCount} succeeded, {FailedCount} failed",
                        aiAssistantId, successCount, failedAssets.Count);
                }
            }

            return aiAssistant;
        }

        public async Task<AIAssistantMaterial> UpdateStatusFromAssetsAsync(int aiAssistantId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistantId);

            if (aiAssistant == null)
            {
                throw new KeyNotFoundException($"AI Assistant material {aiAssistantId} not found");
            }

            var newStatus = await CalculateAggregateStatusAsync(context, aiAssistant);

            if (aiAssistant.AIAssistantStatus != newStatus)
            {
                aiAssistant.AIAssistantStatus = newStatus;
                aiAssistant.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();

                _logger.LogInformation("Updated AI Assistant material {AIAssistantId} status to {Status}", aiAssistantId, newStatus);
            }

            return aiAssistant;
        }

        public async Task<string> GetAggregateStatusAsync(int aiAssistantId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistantId);

            if (aiAssistant == null)
            {
                return "notready";
            }

            return await CalculateAggregateStatusAsync(context, aiAssistant);
        }

        private async Task<string> CalculateAggregateStatusAsync(XR50TrainingContext context, AIAssistantMaterial aiAssistant)
        {
            var assetIds = aiAssistant.GetAssetIdsList();
            if (!assetIds.Any())
            {
                return "notready";
            }

            var assets = await context.Assets
                .Where(a => assetIds.Contains(a.Id))
                .Select(a => a.AiAvailable)
                .ToListAsync();

            if (!assets.Any())
            {
                return "notready";
            }

            // If any asset is still processing, the whole material is processing
            if (assets.Any(s => s == "process"))
            {
                return "process";
            }

            // If all assets are ready, the material is ready
            if (assets.All(s => s == "ready"))
            {
                return "ready";
            }

            // Otherwise, not ready
            return "notready";
        }

        #endregion

        #region Session Management

        public async Task<AIAssistantSession?> GetActiveSessionAsync(int aiAssistantId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.AIAssistantSessions
                .Where(s => s.AIAssistantMaterialId == aiAssistantId && s.Status == "active")
                .FirstOrDefaultAsync();
        }

        public async Task<AIAssistantSession> CreateSessionAsync(int aiAssistantId, string sessionId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Invalidate any existing sessions
            await InvalidateSessionInternalAsync(context, aiAssistantId);

            // Compute asset hash for change detection
            var assetHash = await ComputeAssetHashAsync(context, aiAssistantId);

            // Create new session
            var newSession = new AIAssistantSession
            {
                AIAssistantMaterialId = aiAssistantId,
                SessionId = sessionId,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                AssetHash = assetHash
            };

            context.AIAssistantSessions.Add(newSession);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created session for AI Assistant material {AIAssistantId}. Session ID: {SessionId}",
                aiAssistantId, sessionId);

            return newSession;
        }

        public async Task InvalidateSessionAsync(int aiAssistantId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            await InvalidateSessionInternalAsync(context, aiAssistantId);
            await context.SaveChangesAsync();
        }

        private async Task InvalidateSessionInternalAsync(XR50TrainingContext context, int aiAssistantId)
        {
            var sessions = await context.AIAssistantSessions
                .Where(s => s.AIAssistantMaterialId == aiAssistantId && s.Status == "active")
                .ToListAsync();

            foreach (var session in sessions)
            {
                session.Status = "invalidated";
                _logger.LogInformation("Invalidated session {SessionId} for AI Assistant material {AIAssistantId}",
                    session.SessionId, aiAssistantId);
            }
        }

        public async Task<bool> IsSessionValidAsync(int aiAssistantId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var session = await context.AIAssistantSessions
                .Where(s => s.AIAssistantMaterialId == aiAssistantId && s.Status == "active")
                .FirstOrDefaultAsync();

            if (session == null)
            {
                return false;
            }

            // Check if asset hash matches (assets haven't changed)
            var currentHash = await ComputeAssetHashAsync(context, aiAssistantId);
            return session.AssetHash == currentHash;
        }

        private async Task<string> ComputeAssetHashAsync(XR50TrainingContext context, int aiAssistantId)
        {
            var aiAssistant = await context.Materials
                .OfType<AIAssistantMaterial>()
                .FirstOrDefaultAsync(a => a.id == aiAssistantId);

            if (aiAssistant == null)
            {
                return string.Empty;
            }

            var assetIds = aiAssistant.GetAssetIdsList();
            if (!assetIds.Any())
            {
                return string.Empty;
            }

            var assets = await context.Assets
                .Where(a => assetIds.Contains(a.Id))
                .OrderBy(a => a.Id)
                .Select(a => a.Filename)
                .ToListAsync();

            var filenames = string.Join(",", assets);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(filenames));
            return Convert.ToBase64String(hash);
        }

        #endregion
    }
}
