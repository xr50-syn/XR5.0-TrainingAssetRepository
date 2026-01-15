using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XR50TrainingAssetRepo.Models
{
    /// <summary>
    /// Stores detailed quiz answers and evaluation - Source of truth for user responses
    /// </summary>
    public class UserMaterialData
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(255)]
        public string UserId { get; set; } = "";

        public int ProgramId { get; set; } = 0;

        public int? LearningPathId { get; set; }

        [Required]
        public int MaterialId { get; set; }

        /// <summary>
        /// JSON containing processed answers, per-question evaluation, scores, metadata
        /// </summary>
        [Column(TypeName = "json")]
        public string Data { get; set; } = "{}";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User? User { get; set; }
        public virtual TrainingProgram? Program { get; set; }
        public virtual LearningPath? LearningPath { get; set; }
        public virtual Material? Material { get; set; }
    }

    /// <summary>
    /// Stores summarized scores and progress - Hot data for quick queries
    /// </summary>
    public class UserMaterialScore
    {
        [Required]
        [StringLength(255)]
        public string UserId { get; set; } = "";

        public int ProgramId { get; set; } = 0;

        public int? LearningPathId { get; set; }

        [Required]
        public int MaterialId { get; set; }

        public decimal Score { get; set; } = 0;

        /// <summary>
        /// Progress percentage 0-100
        /// </summary>
        public int Progress { get; set; } = 0;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User? User { get; set; }
        public virtual TrainingProgram? Program { get; set; }
        public virtual LearningPath? LearningPath { get; set; }
        public virtual Material? Material { get; set; }
    }
}
