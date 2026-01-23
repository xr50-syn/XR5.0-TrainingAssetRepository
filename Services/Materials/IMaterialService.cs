using System.Text.Json;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Base interface for material CRUD operations.
    /// Type-specific operations are in separate interfaces (IVideoMaterialService, etc.)
    /// </summary>
    public interface IMaterialServiceBase
    {
        // Generic CRUD Operations
        Task<IEnumerable<Material>> GetAllAsync();
        Task<Material?> GetByIdAsync(int id);
        Task<T?> GetByIdAsync<T>(int id) where T : Material;
        Task<Material> CreateAsync(Material material);
        Task<Material> CreateCompleteAsync(Material material);
        Task<Material> CreateFromJsonAsync(JsonElement materialData);
        Task<Material> UpdateAsync(Material material);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);

        // Type Filtering
        Task<IEnumerable<T>> GetAllOfTypeAsync<T>() where T : Material;
        Task<IEnumerable<Material>> GetByTypeAsync(System.Type materialType);

        // Complete Material Details (polymorphic)
        Task<object?> GetCompleteDetailsAsync(int materialId);
        Task<Material?> GetCompleteAsync(int materialId);

        // Asset Relationships (shared by asset-based material types)
        Task<IEnumerable<Material>> GetByAssetIdAsync(int assetId);
        Task<bool> AssignAssetAsync(int materialId, int assetId);
        Task<bool> RemoveAssetAsync(int materialId);
        Task<int?> GetAssetIdAsync(int materialId);
    }
}
