using System.ComponentModel.DataAnnotations;

namespace XR50TrainingAssetRepo.Models.DTOs
{
    public class CreateLearningPathWithMaterialsRequest
    {
        [Required]
        [StringLength(255)]
        public string LearningPathName { get; set; } = "";
        
        [StringLength(1000)]
        public string? Description { get; set; }
        
        [Required]
        public List<int> Materials { get; set; } = new();
        
        public List<MaterialAssignmentRequest>? MaterialAssignments { get; set; }
        
        public List<int>? TrainingPrograms { get; set; }
    }

    public class MaterialAssignmentRequest
    {
        public int MaterialId { get; set; }
        public string RelationshipType { get; set; } = "contains";
        public int? DisplayOrder { get; set; }
    }
    
    public class CreateLearningPathWithMaterialsResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int learningPath_id { get; set; }
        public string LearningPathName { get; set; } = "";
        public string? Description { get; set; }
        public string? CreatedAt { get; set; }
        public int MaterialCount { get; set; }
        public int TrainingProgramCount { get; set; }
        public List<AssignedMaterialDetails> AssignedMaterials { get; set; } = new();
        public List<AssignedTrainingProgram> AssignedTrainingPrograms { get; set; } = new();
    }
    
    public class AssignedMaterialDetails
    {
        public int MaterialId { get; set; }
        public string? MaterialName { get; set; }
        public string? MaterialType { get; set; }
        public bool AssignmentSuccessful { get; set; }
        public string? AssignmentNote { get; set; }
        public string RelationshipType { get; set; } = "contains";
        public int? DisplayOrder { get; set; }
        public int? RelationshipId { get; set; }
    }
    
    public class AssignedTrainingProgram
    {
        public int TrainingProgramId { get; set; }
        public string? TrainingProgramName { get; set; }
        public bool AssignmentSuccessful { get; set; }
        public string? AssignmentNote { get; set; }
    }

    public class CompleteLearningPathResponse
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int learningPath_id { get; set; }
        public string LearningPathName { get; set; } = "";
        public string? Description { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }

        public List<MaterialResponse> Materials { get; set; } = new();

        public List<TrainingProgramResponse> TrainingPrograms { get; set; } = new();


        public LearningPathSummary Summary { get; set; } = new();
    }

    public class TrainingProgramResponse
    {
        public int program_id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public DateTime? Created_at { get; set; }
    }

    public class LearningPathSummary
    {
        public int TotalMaterials { get; set; }
        public int TotalTrainingPrograms { get; set; }
        public Dictionary<string, int> MaterialsByType { get; set; } = new();
        public DateTime? LastModified { get; set; }
        public DateTime? Created { get; set; }
    }
}