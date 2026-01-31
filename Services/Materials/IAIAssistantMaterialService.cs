using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for AI Assistant material-specific operations including AI processing and session management.
    /// </summary>
    public interface IAIAssistantMaterialService
    {
        // CRUD Operations
        Task<IEnumerable<AIAssistantMaterial>> GetAllAsync();
        Task<AIAssistantMaterial?> GetByIdAsync(int id);
        Task<AIAssistantMaterial> CreateAsync(AIAssistantMaterial aiAssistant);
        Task<AIAssistantMaterial> UpdateAsync(AIAssistantMaterial aiAssistant);
        Task<bool> DeleteAsync(int id);

        // AI Assistant-specific operations
        Task<AIAssistantMaterial> CreateWithAssetsAsync(AIAssistantMaterial aiAssistant, List<int> assetIds);
        Task<AIAssistantMaterial?> GetWithAssetsAsync(int id);

        // Asset management
        Task<bool> AddAssetAsync(int aiAssistantId, int assetId);
        Task<bool> RemoveAssetAsync(int aiAssistantId, int assetId);
        Task<List<int>> GetAssetIdsAsync(int aiAssistantId);
        Task<IEnumerable<Asset>> GetAssetsAsync(int aiAssistantId);

        // AI Status operations
        Task<AIAssistantMaterial> SubmitForProcessingAsync(int aiAssistantId);
        Task<AIAssistantMaterial> UpdateStatusFromAssetsAsync(int aiAssistantId);
        Task<string> GetAggregateStatusAsync(int aiAssistantId);

        // Session management
        Task<AIAssistantSession?> GetActiveSessionAsync(int aiAssistantId);
        Task<AIAssistantSession> CreateSessionAsync(int aiAssistantId, string sessionId);
        Task InvalidateSessionAsync(int aiAssistantId);
        Task<bool> IsSessionValidAsync(int aiAssistantId);
    }
}
