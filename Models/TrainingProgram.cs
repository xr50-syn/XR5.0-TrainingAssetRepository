using Microsoft.EntityFrameworkCore;
using Mono.TextTemplating;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Permissions;
using System.Text.Json.Serialization;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Models
{

    public class TrainingProgram
    {

        public string? Created_at { get; set; }
        [Key]
        public int id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public string? Objectives { get; set; }
        public string? Requirements { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
         public int? required_upto_level_rank { get; set; }
        [JsonIgnore]
        public virtual ICollection<ProgramMaterial> Materials { get; set; } = new List<ProgramMaterial>();
        [JsonIgnore]
    public virtual ICollection<ProgramLearningPath> LearningPaths { get; set; } = new List<ProgramLearningPath>();
        public TrainingProgram()
        {

        }
        // Junction table models

    }
     public class ProgramMaterial
    {
        public int TrainingProgramId { get; set; }
        public int MaterialId { get; set; }
            
        // Navigation properties
        public virtual TrainingProgram TrainingProgram { get; set; }
        public virtual Material Material { get; set; }
    }

    public class ProgramLearningPath
    {
        public int TrainingProgramId { get; set; }
        public int LearningPathId { get; set; }
            
        // Navigation properties
        public virtual TrainingProgram TrainingProgram { get; set; }
        public virtual LearningPath LearningPath { get; set; }
     }

}
