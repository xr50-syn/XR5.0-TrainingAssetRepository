using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for INNOV chatbot material operations: multi-asset ingestion into an INNOV pilot,
    /// per-asset status tracking, and conversational chat against the pilot.
    /// </summary>
    public interface IInnovChatbotMaterialService
    {
        // CRUD Operations
        Task<IEnumerable<InnovChatbotMaterial>> GetAllAsync();
        Task<InnovChatbotMaterial?> GetByIdAsync(int id);
        Task<InnovChatbotMaterial> CreateAsync(InnovChatbotMaterial material);
        Task<InnovChatbotMaterial> UpdateAsync(InnovChatbotMaterial material);
        Task<bool> DeleteAsync(int id);

        // Ingestion / asset management
        Task<(InnovChatbotMaterial Material, List<string> Warnings)> CreateWithAssetsAsync(InnovChatbotMaterial material, List<int> assetIds);
        Task<bool> AddAssetAsync(int materialId, int assetId);
        Task<bool> RemoveAssetAsync(int materialId, int assetId);
        Task<List<int>> GetAssetIdsAsync(int materialId);
        Task<IEnumerable<Asset>> GetAssetsAsync(int materialId);

        // Status operations
        Task<InnovChatbotMaterial> SubmitForProcessingAsync(int materialId);
        Task<InnovChatbotMaterial> UpdateStatusFromAssetsAsync(int materialId);
        Task<string> GetAggregateStatusAsync(int materialId);
        Task<List<InnovChatbotMaterialAssetJob>> GetAssetJobsAsync(int materialId);

        // Chat / documents
        Task<InnovChatResponse> ChatAsync(int materialId, string query, string? expertiseLevel = null);
        Task<InnovDocumentUploadResponse> UploadDocumentAsync(int materialId, Stream fileStream, string fileName, string contentType);
        Task ClearHistoryAsync(int materialId);
        Task<bool> IsEndpointAvailableAsync(int materialId);
    }
}
