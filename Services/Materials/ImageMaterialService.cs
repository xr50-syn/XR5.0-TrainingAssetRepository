using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Image material-specific operations including annotations.
    /// </summary>
    public class ImageMaterialService : IImageMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<ImageMaterialService> _logger;

        public ImageMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<ImageMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Image Material CRUD

        public async Task<IEnumerable<ImageMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ImageMaterial>()
                .ToListAsync();
        }

        public async Task<ImageMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ImageMaterial>()
                .FirstOrDefaultAsync(i => i.id == id);
        }

        public async Task<ImageMaterial?> GetWithAnnotationsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ImageMaterial>()
                .Include(i => i.ImageAnnotations)
                .FirstOrDefaultAsync(i => i.id == id);
        }

        public async Task<ImageMaterial> CreateAsync(ImageMaterial image)
        {
            using var context = _dbContextFactory.CreateDbContext();

            image.Created_at = DateTime.UtcNow;
            image.Updated_at = DateTime.UtcNow;
            image.Type = MaterialType.Image;

            context.Materials.Add(image);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created image material: {Name} with ID: {Id}", image.Name, image.id);

            return image;
        }

        public async Task<ImageMaterial> CreateWithAnnotationsAsync(ImageMaterial image, IEnumerable<ImageAnnotation>? annotations = null)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                image.Created_at = DateTime.UtcNow;
                image.Updated_at = DateTime.UtcNow;
                image.Type = MaterialType.Image;

                context.Materials.Add(image);
                await context.SaveChangesAsync();

                if (annotations != null && annotations.Any())
                {
                    foreach (var annotation in annotations)
                    {
                        annotation.ImageAnnotationId = 0;
                        annotation.ImageMaterialId = image.id;
                        context.ImageAnnotations.Add(annotation);
                    }
                    await context.SaveChangesAsync();

                    _logger.LogInformation("Added {AnnotationCount} initial annotations to image {ImageId}",
                        annotations.Count(), image.id);
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Created image material: {Name} with ID: {Id}", image.Name, image.id);

                return image;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create image material {Name} - Transaction rolled back", image.Name);
                throw;
            }
        }

        public async Task<ImageMaterial> UpdateAsync(ImageMaterial image)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var existing = await context.Materials
                    .OfType<ImageMaterial>()
                    .FirstOrDefaultAsync(i => i.id == image.id);

                if (existing == null)
                {
                    throw new KeyNotFoundException($"Image material {image.id} not found");
                }

                var createdAt = existing.Created_at;
                var uniqueId = existing.Unique_id;
                var existingAssetId = existing.AssetId;

                // Delete existing annotations
                var existingAnnotations = await context.ImageAnnotations
                    .Where(a => a.ImageMaterialId == image.id)
                    .ToListAsync();
                context.ImageAnnotations.RemoveRange(existingAnnotations);

                context.Materials.Remove(existing);
                await context.SaveChangesAsync();

                image.id = existing.id;
                image.Created_at = createdAt;
                image.Updated_at = DateTime.UtcNow;
                image.Unique_id = uniqueId;
                image.Type = MaterialType.Image;

                if (image.AssetId == null && existingAssetId.HasValue)
                {
                    image.AssetId = existingAssetId;
                }

                context.Materials.Add(image);
                await context.SaveChangesAsync();

                if (image.ImageAnnotations?.Any() == true)
                {
                    foreach (var annotation in image.ImageAnnotations.ToList())
                    {
                        var newAnnotation = new ImageAnnotation
                        {
                            ClientId = annotation.ClientId,
                            Text = annotation.Text,
                            FontSize = annotation.FontSize,
                            X = annotation.X,
                            Y = annotation.Y,
                            ImageMaterialId = image.id
                        };
                        context.ImageAnnotations.Add(newAnnotation);
                    }
                    await context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Updated image material: {Id} ({Name})", image.id, image.Name);

                return await GetWithAnnotationsAsync(image.id) ?? image;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update image material {Id}", image.id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var image = await context.Materials
                .OfType<ImageMaterial>()
                .FirstOrDefaultAsync(i => i.id == id);

            if (image == null)
            {
                return false;
            }

            var annotations = await context.ImageAnnotations
                .Where(a => a.ImageMaterialId == id)
                .ToListAsync();
            context.ImageAnnotations.RemoveRange(annotations);

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            context.Materials.Remove(image);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted image material: {Id} with {AnnotationCount} annotations",
                id, annotations.Count);

            return true;
        }

        #endregion

        #region Annotation Operations

        public async Task<ImageAnnotation> AddAnnotationAsync(int imageId, ImageAnnotation annotation)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var image = await context.Materials
                .OfType<ImageMaterial>()
                .FirstOrDefaultAsync(i => i.id == imageId);

            if (image == null)
            {
                throw new ArgumentException($"Image material with ID {imageId} not found");
            }

            annotation.ImageAnnotationId = 0;
            annotation.ImageMaterialId = imageId;

            context.ImageAnnotations.Add(annotation);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added annotation to image material {ImageId}", imageId);

            return annotation;
        }

        public async Task<bool> RemoveAnnotationAsync(int imageId, int annotationId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var annotation = await context.ImageAnnotations.FindAsync(annotationId);
            if (annotation == null || annotation.ImageMaterialId != imageId)
            {
                return false;
            }

            context.ImageAnnotations.Remove(annotation);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed annotation {AnnotationId} from image material {ImageId}",
                annotationId, imageId);

            return true;
        }

        public async Task<IEnumerable<ImageAnnotation>> GetAnnotationsAsync(int imageId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.ImageAnnotations
                .Where(a => a.ImageMaterialId == imageId)
                .ToListAsync();
        }

        #endregion

        #region Asset Operations

        public async Task<bool> AssignAssetAsync(int imageId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var image = await context.Materials
                .OfType<ImageMaterial>()
                .FirstOrDefaultAsync(i => i.id == imageId);

            if (image == null)
            {
                return false;
            }

            image.AssetId = assetId;
            image.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();

            _logger.LogInformation("Assigned asset {AssetId} to image material {ImageId}", assetId, imageId);

            return true;
        }

        public async Task<bool> RemoveAssetAsync(int imageId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var image = await context.Materials
                .OfType<ImageMaterial>()
                .FirstOrDefaultAsync(i => i.id == imageId);

            if (image == null)
            {
                return false;
            }

            image.AssetId = null;
            image.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();

            _logger.LogInformation("Removed asset from image material {ImageId}", imageId);

            return true;
        }

        public async Task<int?> GetAssetIdAsync(int imageId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var image = await context.Materials
                .OfType<ImageMaterial>()
                .FirstOrDefaultAsync(i => i.id == imageId);

            return image?.AssetId;
        }

        #endregion
    }
}
