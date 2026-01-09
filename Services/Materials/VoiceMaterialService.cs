using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Voice material operations with multi-asset support and AI processing.
    /// </summary>
    public class VoiceMaterialService : IVoiceMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly IChatbotApiService _chatbotApiService;
        private readonly ILogger<VoiceMaterialService> _logger;

        public VoiceMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            IChatbotApiService chatbotApiService,
            ILogger<VoiceMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _chatbotApiService = chatbotApiService;
            _logger = logger;
        }

        #region CRUD Operations

        public async Task<IEnumerable<VoiceMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<VoiceMaterial>()
                .ToListAsync();
        }

        public async Task<VoiceMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == id);
        }

        public async Task<VoiceMaterial> CreateAsync(VoiceMaterial voice)
        {
            using var context = _dbContextFactory.CreateDbContext();

            voice.Created_at = DateTime.UtcNow;
            voice.Updated_at = DateTime.UtcNow;
            voice.Type = MaterialType.Voice;

            if (string.IsNullOrEmpty(voice.VoiceStatus))
            {
                voice.VoiceStatus = "notready";
            }

            context.Materials.Add(voice);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created voice material: {Name} with ID: {Id}", voice.Name, voice.id);

            return voice;
        }

        public async Task<VoiceMaterial> UpdateAsync(VoiceMaterial voice)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var existing = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == voice.id);

            if (existing == null)
            {
                throw new KeyNotFoundException($"Voice material {voice.id} not found");
            }

            // Preserve immutable fields
            voice.Created_at = existing.Created_at;
            voice.Unique_id = existing.Unique_id;
            voice.Type = MaterialType.Voice;
            voice.Updated_at = DateTime.UtcNow;

            context.Entry(existing).CurrentValues.SetValues(voice);
            await context.SaveChangesAsync();

            _logger.LogInformation("Updated voice material: {Id} ({Name})", voice.id, voice.Name);

            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == id);

            if (voice == null)
            {
                return false;
            }

            // Clean up material relationships
            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            context.Materials.Remove(voice);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted voice material: {Id}", id);

            return true;
        }

        #endregion

        #region Voice-specific Operations

        public async Task<VoiceMaterial> CreateWithAssetsAsync(VoiceMaterial voice, List<int> assetIds)
        {
            using var context = _dbContextFactory.CreateDbContext();

            voice.Created_at = DateTime.UtcNow;
            voice.Updated_at = DateTime.UtcNow;
            voice.Type = MaterialType.Voice;
            voice.VoiceStatus = "notready";
            voice.SetAssetIdsList(assetIds);

            context.Materials.Add(voice);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created voice material: {Name} with ID: {Id} and {AssetCount} assets",
                voice.Name, voice.id, assetIds.Count);

            return voice;
        }

        public async Task<VoiceMaterial?> GetWithAssetsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == id);

            return voice;
        }

        #endregion

        #region Asset Management

        public async Task<bool> AddAssetAsync(int voiceId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == voiceId);

            if (voice == null)
            {
                return false;
            }

            var assetIds = voice.GetAssetIdsList();
            if (!assetIds.Contains(assetId))
            {
                assetIds.Add(assetId);
                voice.SetAssetIdsList(assetIds);
                voice.Updated_at = DateTime.UtcNow;

                await context.SaveChangesAsync();

                _logger.LogInformation("Added asset {AssetId} to voice material {VoiceId}", assetId, voiceId);
            }

            return true;
        }

        public async Task<bool> RemoveAssetAsync(int voiceId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == voiceId);

            if (voice == null)
            {
                return false;
            }

            var assetIds = voice.GetAssetIdsList();
            if (assetIds.Remove(assetId))
            {
                voice.SetAssetIdsList(assetIds);
                voice.Updated_at = DateTime.UtcNow;

                await context.SaveChangesAsync();

                _logger.LogInformation("Removed asset {AssetId} from voice material {VoiceId}", assetId, voiceId);
            }

            return true;
        }

        public async Task<List<int>> GetAssetIdsAsync(int voiceId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == voiceId);

            return voice?.GetAssetIdsList() ?? new List<int>();
        }

        public async Task<IEnumerable<Asset>> GetAssetsAsync(int voiceId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == voiceId);

            if (voice == null)
            {
                return Enumerable.Empty<Asset>();
            }

            var assetIds = voice.GetAssetIdsList();
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

        public async Task<VoiceMaterial> SubmitForProcessingAsync(int voiceId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == voiceId);

            if (voice == null)
            {
                throw new KeyNotFoundException($"Voice material {voiceId} not found");
            }

            var assetIds = voice.GetAssetIdsList();
            if (!assetIds.Any())
            {
                throw new InvalidOperationException("Voice material has no assets to submit");
            }

            // Get assets and submit each for processing
            var assets = await context.Assets
                .Where(a => assetIds.Contains(a.Id))
                .ToListAsync();

            var hasSubmissions = false;

            foreach (var asset in assets)
            {
                if (asset.AiAvailable == "notready" && !string.IsNullOrEmpty(asset.URL))
                {
                    try
                    {
                        var jobId = await _chatbotApiService.SubmitDocumentAsync(asset.Id, asset.URL, asset.Filetype ?? "pdf");
                        asset.JobId = jobId;
                        asset.AiAvailable = "process";
                        hasSubmissions = true;

                        _logger.LogInformation("Submitted asset {AssetId} for processing. Job ID: {JobId}",
                            asset.Id, jobId);
                    }
                    catch (ChatbotApiException ex)
                    {
                        _logger.LogWarning(ex, "Failed to submit asset {AssetId} for processing", asset.Id);
                    }
                }
            }

            if (hasSubmissions)
            {
                voice.VoiceStatus = "process";
                voice.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }

            return voice;
        }

        public async Task<VoiceMaterial> UpdateStatusFromAssetsAsync(int voiceId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == voiceId);

            if (voice == null)
            {
                throw new KeyNotFoundException($"Voice material {voiceId} not found");
            }

            var newStatus = await CalculateAggregateStatusAsync(context, voice);

            if (voice.VoiceStatus != newStatus)
            {
                voice.VoiceStatus = newStatus;
                voice.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();

                _logger.LogInformation("Updated voice material {VoiceId} status to {Status}", voiceId, newStatus);
            }

            return voice;
        }

        public async Task<string> GetAggregateStatusAsync(int voiceId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var voice = await context.Materials
                .OfType<VoiceMaterial>()
                .FirstOrDefaultAsync(v => v.id == voiceId);

            if (voice == null)
            {
                return "notready";
            }

            return await CalculateAggregateStatusAsync(context, voice);
        }

        private async Task<string> CalculateAggregateStatusAsync(XR50TrainingContext context, VoiceMaterial voice)
        {
            var assetIds = voice.GetAssetIdsList();
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
    }
}
