using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Video material-specific operations including timestamps.
    /// </summary>
    public class VideoMaterialService : IVideoMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<VideoMaterialService> _logger;

        public VideoMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<VideoMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Video Material CRUD

        public async Task<IEnumerable<VideoMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<VideoMaterial>()
                .ToListAsync();
        }

        public async Task<VideoMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<VideoMaterial>()
                .FirstOrDefaultAsync(v => v.id == id);
        }

        public async Task<VideoMaterial?> GetWithTimestampsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<VideoMaterial>()
                .Include(v => v.Timestamps)
                .FirstOrDefaultAsync(v => v.id == id);
        }

        public async Task<VideoMaterial> CreateAsync(VideoMaterial video)
        {
            using var context = _dbContextFactory.CreateDbContext();

            video.Created_at = DateTime.UtcNow;
            video.Updated_at = DateTime.UtcNow;
            video.Type = MaterialType.Video;

            context.Materials.Add(video);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created video material: {Name} with ID: {Id}", video.Name, video.id);

            return video;
        }

        public async Task<VideoMaterial> CreateWithTimestampsAsync(VideoMaterial video, IEnumerable<VideoTimestamp>? timestamps = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            video.Created_at = DateTime.UtcNow;
            video.Updated_at = DateTime.UtcNow;
            video.Type = MaterialType.Video;

            context.Materials.Add(video);
            await context.SaveChangesAsync();

            if (timestamps != null && timestamps.Any())
            {
                foreach (var timestamp in timestamps)
                {
                    timestamp.id = 0; // Reset ID for new record
                    timestamp.VideoMaterialId = video.id;
                    context.Timestamps.Add(timestamp);
                }
                await context.SaveChangesAsync();

                _logger.LogInformation("Added {TimestampCount} initial timestamps to video {VideoId}",
                    timestamps.Count(), video.id);
            }

            _logger.LogInformation("Created video material: {Name} with ID: {Id}", video.Name, video.id);

            return video;
        }

        public async Task<VideoMaterial> UpdateAsync(VideoMaterial video)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var existing = await context.Materials
                    .OfType<VideoMaterial>()
                    .FirstOrDefaultAsync(v => v.id == video.id);

                if (existing == null)
                {
                    throw new KeyNotFoundException($"Video material {video.id} not found");
                }

                // Preserve original values
                var createdAt = existing.Created_at;
                var uniqueId = existing.Unique_id;
                var existingAssetId = existing.AssetId;

                // Delete existing timestamps
                var existingTimestamps = await context.Timestamps
                    .Where(t => t.VideoMaterialId == video.id)
                    .ToListAsync();
                context.Timestamps.RemoveRange(existingTimestamps);

                // Remove and re-add the material
                context.Materials.Remove(existing);
                await context.SaveChangesAsync();

                video.id = existing.id;
                video.Created_at = createdAt;
                video.Updated_at = DateTime.UtcNow;
                video.Unique_id = uniqueId;
                video.Type = MaterialType.Video;

                // Preserve asset ID if not explicitly set
                if (video.AssetId == null && existingAssetId.HasValue)
                {
                    video.AssetId = existingAssetId;
                }

                context.Materials.Add(video);
                await context.SaveChangesAsync();

                // Re-add timestamps if present
                if (video.Timestamps?.Any() == true)
                {
                    foreach (var timestamp in video.Timestamps.ToList())
                    {
                        var newTimestamp = new VideoTimestamp
                        {
                            Title = timestamp.Title,
                            startTime = timestamp.startTime,
                            endTime = timestamp.endTime,
                            Duration = timestamp.Duration,
                            Description = timestamp.Description,
                            Type = timestamp.Type,
                            VideoMaterialId = video.id
                        };
                        context.Timestamps.Add(newTimestamp);
                    }
                    await context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Updated video material: {Id} ({Name})", video.id, video.Name);

                return video;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update video material {Id}", video.id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var video = await context.Materials
                .OfType<VideoMaterial>()
                .FirstOrDefaultAsync(v => v.id == id);

            if (video == null)
            {
                return false;
            }

            // Delete timestamps
            var timestamps = await context.Timestamps
                .Where(t => t.VideoMaterialId == id)
                .ToListAsync();
            context.Timestamps.RemoveRange(timestamps);

            // Delete material relationships
            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            context.Materials.Remove(video);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted video material: {Id} with {TimestampCount} timestamps and {RelationshipCount} relationships",
                id, timestamps.Count, relationships.Count);

            return true;
        }

        #endregion

        #region Timestamp Operations

        public async Task<VideoTimestamp> AddTimestampAsync(int videoId, VideoTimestamp timestamp)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var video = await context.Materials
                .OfType<VideoMaterial>()
                .FirstOrDefaultAsync(v => v.id == videoId);

            if (video == null)
            {
                throw new ArgumentException($"Video material with ID {videoId} not found");
            }

            timestamp.id = 0;
            timestamp.VideoMaterialId = videoId;

            context.Timestamps.Add(timestamp);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added timestamp '{Title}' to video material {VideoId}",
                timestamp.Title, videoId);

            return timestamp;
        }

        public async Task<bool> RemoveTimestampAsync(int videoId, int timestampId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var timestamp = await context.Timestamps.FindAsync(timestampId);
            if (timestamp == null || timestamp.VideoMaterialId != videoId)
            {
                return false;
            }

            context.Timestamps.Remove(timestamp);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed timestamp {TimestampId} from video material {VideoId}",
                timestampId, videoId);

            return true;
        }

        public async Task<IEnumerable<VideoTimestamp>> GetTimestampsAsync(int videoId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.Timestamps
                .Where(t => t.VideoMaterialId == videoId)
                .OrderBy(t => t.startTime)
                .ToListAsync();
        }

        #endregion

        #region Asset Operations

        public async Task<bool> AssignAssetAsync(int videoId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var video = await context.Materials
                .OfType<VideoMaterial>()
                .FirstOrDefaultAsync(v => v.id == videoId);

            if (video == null)
            {
                return false;
            }

            video.AssetId = assetId;
            video.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();

            _logger.LogInformation("Assigned asset {AssetId} to video material {VideoId}", assetId, videoId);

            return true;
        }

        public async Task<bool> RemoveAssetAsync(int videoId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var video = await context.Materials
                .OfType<VideoMaterial>()
                .FirstOrDefaultAsync(v => v.id == videoId);

            if (video == null)
            {
                return false;
            }

            video.AssetId = null;
            video.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();

            _logger.LogInformation("Removed asset from video material {VideoId}", videoId);

            return true;
        }

        public async Task<int?> GetAssetIdAsync(int videoId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var video = await context.Materials
                .OfType<VideoMaterial>()
                .FirstOrDefaultAsync(v => v.id == videoId);

            return video?.AssetId;
        }

        #endregion
    }
}
