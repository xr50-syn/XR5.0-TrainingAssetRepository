using System.ComponentModel.DataAnnotations;

namespace XR50TrainingAssetRepo.Models.DTOs
{
     public class CreateTrainingProgramWithMaterialsRequest
    {
        [Required]
        [StringLength(255)]
        public string Name { get; set; } = "";

        [StringLength(1000)]
        public string? Description { get; set; }
        public string? Objectives { get; set; }
        public string? Requirements { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
        [Required]
        public List<int> Materials { get; set; } = new();

        // Optional: Learning path IDs to assign as well
        public List<int>? LearningPaths { get; set; }
    }
    
    public class CreateTrainingProgramWithMaterialsResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int program_id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Objectives { get; set; }
        public string? Requirements { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
        public string? CreatedAt { get; set; }
        public int MaterialCount { get; set; }
        public int LearningPathCount { get; set; }
        public List<AssignedMaterial> AssignedMaterials { get; set; } = new();
        public List<AssignedLearningPath> AssignedLearningPaths { get; set; } = new();
    }
    
    public class AssignedMaterial
    {
        public int MaterialId { get; set; }
        public string? MaterialName { get; set; }
        public string? MaterialType { get; set; }
        public bool AssignmentSuccessful { get; set; }
        public string? AssignmentNote { get; set; }
    }
    
    public class AssignedLearningPath
    {
        public int LearningPathId { get; set; }
        public string? LearningPathName { get; set; }
        public bool AssignmentSuccessful { get; set; }
        public string? AssignmentNote { get; set; }
    }

    public class MaterialInfo
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int Type { get; set; }
}

    public class CompleteTrainingProgramRequest
    {
        public string Name { get; set; } = "";
        [StringLength(1000)]
        public string? Description { get; set; }
        public string? Objectives { get; set; }
        public string? Requirements { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
        // Materials to assign during creation
        public List<int> Materials { get; set; } = new();

        // Learning paths to assign during creation (if needed)
        public List<int> LearningPaths { get; set; } = new();

        // Optional: Materials with full data (for creation + assignment in one go)
       // public List<MaterialCreationRequest>? MaterialsToCreate { get; set; }
    }

    public class MaterialCreationRequest
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string MaterialType { get; set; } = "Default"; // Video, Image, Checklist, etc.
        public int? UniqueId { get; set; }

        // Type-specific properties (only set what's needed based on MaterialType)
        public int? AssetId { get; set; }
        public string? VideoPath { get; set; }
        public int? VideoDuration { get; set; }
        public string? VideoResolution { get; set; }
        public string? ImagePath { get; set; }
        public string? ChatbotConfig { get; set; }
        public string? MessageType { get; set; }
        public string? MessageText { get; set; }

        public List<ChecklistEntryDto>? Entries { get; set; }
        public List<WorkflowStepDto>? Steps { get; set; }
        public List<QuestionnaireEntryDto>? Questions { get; set; }
        public List<VideoTimestampDto>? Timestamps { get; set; }
    }
    public class ChecklistEntryDto
    {
        public string Text { get; set; } = "";
        public string? Description { get; set; }
    }

    public class WorkflowStepDto
    {
        public string Title { get; set; } = "";
        public string? Content { get; set; }
    }

    public class QuestionnaireEntryDto
    {
        public string Question { get; set; } = "";
        public string? QuestionType { get; set; }
        public List<string>? Options { get; set; }
        public string? CorrectAnswer { get; set; }
        public bool Required { get; set; } = true;
    }

    public class VideoTimestampDto
    {
        public string Title { get; set; } = "";
        public string Time { get; set; } = "";
        public string? Description { get; set; }
        public string? AnnotationType { get; set; }
    }
    public class CompleteTrainingProgramResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int program_id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Objectives { get; set; }
        public string? Requirements { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
        public string? Created_at { get; set; }

        // Complete material information
        public List<MaterialResponse> Materials { get; set; } = new();

        // Complete learning path information
        public List<LearningPathResponse> LearningPaths { get; set; } = new();
    }

    public class MaterialResponse
    {
        public int material_id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; } = "";
        public int? UniqueId { get; set; }
        public DateTime? Created_at { get; set; }
        public DateTime? Updated_at { get; set; }

        // Asset information (if applicable)
        public int? AssetId { get; set; }

        // Type-specific properties (populated based on material type)
        public Dictionary<string, object?> TypeSpecificProperties { get; set; } = new();

        // Assignment metadata (if from complex relationships)
        public AssignmentMetadata? Assignment { get; set; }
    }

    public class LearningPathResponse
    {
        public int learningPath_id { get; set; }
        public string LearningPathName { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class AssignmentMetadata
    {
        public string AssignmentType { get; set; } = "Simple"; // Simple or Complex
        public string? RelationshipType { get; set; }
        public int? DisplayOrder { get; set; }
        public int? RelationshipId { get; set; } // For complex relationships
    }

    public class TrainingProgramSummary
    {
        public int TotalMaterials { get; set; }
        public int TotalLearningPaths { get; set; }
        public Dictionary<string, int> MaterialsByType { get; set; } = new();
        public DateTime? LastUpdated { get; set; }
    }
}