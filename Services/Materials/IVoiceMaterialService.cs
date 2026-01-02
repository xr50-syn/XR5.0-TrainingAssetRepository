using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Voice material-specific operations including AI processing.
    /// </summary>
    public interface IVoiceMaterialService
    {
        // CRUD Operations
        Task<IEnumerable<VoiceMaterial>> GetAllAsync();
        Task<VoiceMaterial?> GetByIdAsync(int id);
        Task<VoiceMaterial> CreateAsync(VoiceMaterial voice);
        Task<VoiceMaterial> UpdateAsync(VoiceMaterial voice);
        Task<bool> DeleteAsync(int id);

        // Voice-specific operations
        Task<VoiceMaterial> CreateWithAssetsAsync(VoiceMaterial voice, List<int> assetIds);
        Task<VoiceMaterial?> GetWithAssetsAsync(int id);

        // Asset management
        Task<bool> AddAssetAsync(int voiceId, int assetId);
        Task<bool> RemoveAssetAsync(int voiceId, int assetId);
        Task<List<int>> GetAssetIdsAsync(int voiceId);
        Task<IEnumerable<Asset>> GetAssetsAsync(int voiceId);

        // AI Status operations
        Task<VoiceMaterial> SubmitForProcessingAsync(int voiceId);
        Task<VoiceMaterial> UpdateStatusFromAssetsAsync(int voiceId);
        Task<string> GetAggregateStatusAsync(int voiceId);
    }
}
