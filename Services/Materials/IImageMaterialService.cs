using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Image material-specific operations including annotations.
    /// </summary>
    public interface IImageMaterialService
    {
        // Image Material CRUD
        Task<IEnumerable<ImageMaterial>> GetAllAsync();
        Task<ImageMaterial?> GetByIdAsync(int id);
        Task<ImageMaterial?> GetWithAnnotationsAsync(int id);
        Task<ImageMaterial> CreateAsync(ImageMaterial image);
        Task<ImageMaterial> CreateWithAnnotationsAsync(ImageMaterial image, IEnumerable<ImageAnnotation>? annotations = null);
        Task<ImageMaterial> UpdateAsync(ImageMaterial image);
        Task<bool> DeleteAsync(int id);

        // Annotation Operations
        Task<ImageAnnotation> AddAnnotationAsync(int imageId, ImageAnnotation annotation);
        Task<bool> RemoveAnnotationAsync(int imageId, int annotationId);
        Task<IEnumerable<ImageAnnotation>> GetAnnotationsAsync(int imageId);

        // Asset Operations
        Task<bool> AssignAssetAsync(int imageId, int assetId);
        Task<bool> RemoveAssetAsync(int imageId);
        Task<int?> GetAssetIdAsync(int imageId);
    }
}
