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
        public int id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; } = "";
        public int? Unique_id { get; set; }
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

    public class SubcomponentMaterialRelationshipDto
    {
        public int RelationshipId { get; set; }
        public int SubcomponentId { get; set; }
        public string SubcomponentType { get; set; } = "";
        public int MaterialId { get; set; }
        public string? MaterialName { get; set; }
        public string? MaterialType { get; set; }
        public string? RelationshipType { get; set; }
        public int? DisplayOrder { get; set; }
    }

    public class AssignMaterialToSubcomponentRequest
    {
        public string? RelationshipType { get; set; }
        public int? DisplayOrder { get; set; }
    }

    public class MaterialSummaryDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; } = "";
        public int? DisplayOrder { get; set; }
        public string? RelationshipType { get; set; }
    }

    /// <summary>
    /// DTO for specifying related materials in subcomponent requests.
    /// Only the id is required - name and description are optional for convenience.
    /// </summary>
    public class RelatedMaterialRequest
    {
        public int id { get; set; }
        public string? name { get; set; }
        public string? description { get; set; }
    }

    /// <summary>
    /// DTO for returning related materials in subcomponent responses.
    /// </summary>
    public class RelatedMaterialResponse
    {
        public int id { get; set; }
        public string? name { get; set; }
        public string? description { get; set; }
        public string? type { get; set; }
    }
}
