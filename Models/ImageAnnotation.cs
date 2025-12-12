using System.ComponentModel.DataAnnotations;

namespace XR50TrainingAssetRepo.Models
{
    public class ImageAnnotation
    {
        [Key]
        public int ImageAnnotationId { get; set; }

        [StringLength(50)]
        public string? ClientId { get; set; }  // Client-generated ID like "of1pw6n"

        [StringLength(2000)]
        public string? Text { get; set; }

        public int? FontSize { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public int ImageMaterialId { get; set; }  // FK to ImageMaterial
    }
}
