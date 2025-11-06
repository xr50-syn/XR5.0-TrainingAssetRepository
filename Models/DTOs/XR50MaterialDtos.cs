using System.ComponentModel.DataAnnotations;

namespace XR50TrainingAssetRepo.Models.DTOs
{
    public class AssetReferenceData
    {
        public string? Filename { get; set; }
        public string? Description { get; set; }
        public string? Filetype { get; set; }
        public string? Src { get; set; }
        public string? URL { get; set; }
    }

    public class CreateMaterialResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int material_id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; } = "";
        public int? UniqueId { get; set; }
        public int? AssetId { get; set; }
        public DateTime? Created_at { get; set; }
    }

    public class CreateAssetResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int Id { get; set; }
        public string? Filename { get; set; }
        public string? Description { get; set; }
        public string? Filetype { get; set; }
        public string? Src { get; set; }
        public string? URL { get; set; }
        public DateTime? Created_at { get; set; }
    }
}
