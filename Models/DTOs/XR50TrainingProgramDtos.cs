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

        // Material assignments with rank configuration (populated by controller when objects are provided)
        public List<ProgramMaterialAssignmentRequest>? MaterialAssignments { get; set; }

        // Optional: Learning path IDs to assign as well
        public List<int>? LearningPaths { get; set; }

        // Optional: Learning path as ordered array of material IDs (using snake_case to match your JSON)
        // DEPRECATED: Use learning_path array of objects instead for inline creation
        //public List<int>? learning_path { get; set; }

        // Optional: Learning paths to create inline with custom names and properties
        public List<LearningPathCreationRequest>? learning_path { get; set; }
    }
    
    public class CreateTrainingProgramWithMaterialsResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int id { get; set; }
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
        public int? Unique_id { get; set; }

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
        public int? id { get; set; }
        public string Text { get; set; } = "";
        public string? Description { get; set; }
        public List<RelatedMaterialRequest>? related { get; set; }
    }

    public class WorkflowStepDto
    {
        public int? id { get; set; }
        public string Title { get; set; } = "";
        public string? Content { get; set; }
        public List<RelatedMaterialRequest>? related { get; set; }
    }

    public class QuestionnaireEntryDto
    {
        public int? id { get; set; }
        public string Question { get; set; } = "";
        public string? QuestionType { get; set; }
        public List<string>? Options { get; set; }
        public string? CorrectAnswer { get; set; }
        public bool Required { get; set; } = true;
        public List<RelatedMaterialRequest>? related { get; set; }
    }

    public class VideoTimestampDto
    {
        public int? id { get; set; }
        public string Title { get; set; } = "";
        public string Time { get; set; } = "";
        public string? Description { get; set; }
        public string? AnnotationType { get; set; }
        public List<RelatedMaterialRequest>? related { get; set; }
    }

    public class ImageAnnotationDto
    {
        public int? id { get; set; }
        public string? ClientId { get; set; }
        public string? Text { get; set; }
        public int? FontSize { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public List<RelatedMaterialRequest>? related { get; set; }
    }

    public class LearningPathCreationRequest
    {
        public string? id { get; set; }  // Material IDs (comma-separated or single) to add to this learning path
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public bool? inherit_from_program { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
    }

    public class CompleteTrainingProgramResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int id { get; set; }
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
        public List<LearningPathResponse> learning_path { get; set; } = new();
    }

    public class MaterialResponse
    {
        public int id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; } = "";
        public int? Unique_id { get; set; }
        public DateTime? Created_at { get; set; }
        public DateTime? Updated_at { get; set; }

        // Asset information (if applicable)
        public int? AssetId { get; set; }

        // Type-specific properties (populated based on material type)
        public Dictionary<string, object?> TypeSpecificProperties { get; set; } = new();

        // Assignment metadata (if from complex relationships)
        public AssignmentMetadata? Assignment { get; set; }

        // Level rank properties for this assignment
        public bool? inherit_from_program { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
    }

    public class LearningPathResponse
    {
        public int id { get; set; }
        public string LearningPathName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool? inherit_from_program { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }

        // Materials in this learning path
        public List<MaterialResponse> Materials { get; set; } = new();
    }

    public class AssignmentMetadata
    {
        public string AssignmentType { get; set; } = "Simple"; // Simple or Complex
        public string? RelationshipType { get; set; }
        public int? DisplayOrder { get; set; }
        public int? RelationshipId { get; set; } // For complex relationships
    }

    /// <summary>
    /// Request DTO for assigning a material to a program with optional rank configuration.
    /// </summary>
    public class ProgramMaterialAssignmentRequest
    {
        public int id { get; set; }
        public bool? inherit_from_program { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
    }

    public class TrainingProgramSummary
    {
        public int TotalMaterials { get; set; }
        public int TotalLearningPaths { get; set; }
        public Dictionary<string, int> MaterialsByType { get; set; } = new();
        public DateTime? LastUpdated { get; set; }
    }

    public class UpdateTrainingProgramRequest
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

        // Material IDs to assign to this program
        public List<int> Materials { get; set; } = new();

        // Material assignments with rank configuration (populated by controller when objects are provided)
        public List<ProgramMaterialAssignmentRequest>? MaterialAssignments { get; set; }

        // Learning path IDs to assign to this program
        public List<int> LearningPaths { get; set; } = new();

        // Optional: Learning paths to create inline with custom names and properties
        public List<LearningPathCreationRequest>? learning_path { get; set; }
    }

    // ====================================
    // SIMPLIFIED DTOs (New Architecture)
    // ====================================
    // These DTOs hide the internal learning path structure and present learning paths
    // as simple ordered lists of materials associated with a training program.
    // The underlying database structure is preserved for future expansion.

    /// <summary>
    /// Simplified learning path response that hides internal IDs and structure.
    /// Learning paths are presented as ordered lists of materials within a training program.
    /// </summary>
    public class SimplifiedLearningPathResponse
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }

        /// <summary>
        /// Ordered list of materials in this learning path.
        /// Materials are returned in DisplayOrder sequence.
        /// </summary>
        public List<OrderedMaterialResponse> Materials { get; set; } = new();
    }

    /// <summary>
    /// Material response with display order information for learning paths.
    /// </summary>
    public class OrderedMaterialResponse
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; } = "";
        public int? Unique_id { get; set; }

        /// <summary>
        /// Position in the learning path sequence (1-based).
        /// </summary>
        public int Order { get; set; }

        public DateTime? Created_at { get; set; }
        public DateTime? Updated_at { get; set; }

        // Asset information (if applicable)
        public int? AssetId { get; set; }

        // Type-specific properties (populated based on material type)
        public Dictionary<string, object?> TypeSpecificProperties { get; set; } = new();

        // Level rank properties for this assignment
        public bool? inherit_from_program { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
    }

    /// <summary>
    /// Simplified complete training program response using the new architecture.
    /// Learning paths are presented as flat ordered lists of materials, with internal structure hidden.
    /// </summary>
    public class SimplifiedCompleteTrainingProgramResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Objectives { get; set; }
        public string? Requirements { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }
        public string? Created_at { get; set; }

        /// <summary>
        /// Materials directly assigned to the training program (not through learning paths).
        /// </summary>
        public List<MaterialResponse> Materials { get; set; } = new();

        /// <summary>
        /// Flat array of materials from all learning paths, preserving order.
        /// Learning path names, descriptions, and IDs are completely hidden.
        /// Named as singular to indicate it's one unified ordered list.
        /// </summary>
        public List<OrderedMaterialResponse> learning_path { get; set; } = new();

        /// <summary>
        /// Summary statistics for the training program.
        /// </summary>
        public SimplifiedTrainingProgramSummary? Summary { get; set; }
    }

    /// <summary>
    /// Summary statistics for simplified training program response.
    /// </summary>
    public class SimplifiedTrainingProgramSummary
    {
        public int TotalDirectMaterials { get; set; }
        public int TotalLearningPaths { get; set; }
        public int TotalLearningPathMaterials { get; set; }
        public Dictionary<string, int> MaterialsByType { get; set; } = new();
        public DateTime? LastUpdated { get; set; }
    }
}