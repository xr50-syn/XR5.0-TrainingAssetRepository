using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Video material-specific operations including timestamps.
    /// </summary>
    public interface IVideoMaterialService
    {
        // Video Material CRUD
        Task<IEnumerable<VideoMaterial>> GetAllAsync();
        Task<VideoMaterial?> GetByIdAsync(int id);
        Task<VideoMaterial?> GetWithTimestampsAsync(int id);
        Task<VideoMaterial> CreateAsync(VideoMaterial video);
        Task<VideoMaterial> CreateWithTimestampsAsync(VideoMaterial video, IEnumerable<VideoTimestamp>? timestamps = null);
        Task<VideoMaterial> UpdateAsync(VideoMaterial video);
        Task<bool> DeleteAsync(int id);

        // Timestamp Operations
        Task<VideoTimestamp> AddTimestampAsync(int videoId, VideoTimestamp timestamp);
        Task<bool> RemoveTimestampAsync(int videoId, int timestampId);
        Task<IEnumerable<VideoTimestamp>> GetTimestampsAsync(int videoId);

        // Asset Operations
        Task<bool> AssignAssetAsync(int videoId, int assetId);
        Task<bool> RemoveAssetAsync(int videoId);
        Task<int?> GetAssetIdAsync(int videoId);
    }
}
