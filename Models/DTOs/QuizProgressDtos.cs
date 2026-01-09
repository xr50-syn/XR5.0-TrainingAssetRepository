namespace XR50TrainingAssetRepo.Models.DTOs
{
    /// <summary>
    /// Per-user quiz progress summary (without detailed answers)
    /// </summary>
    public class UserQuizProgressSummary
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public decimal Score { get; set; }
        public int Progress { get; set; }
        public int MaterialId { get; set; }
        public string? MaterialName { get; set; }
        public int? ProgramId { get; set; }
        public string? ProgramName { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Response for tenant-wide quiz progress
    /// </summary>
    public class TenantQuizProgressResponse
    {
        public string TenantName { get; set; } = "";
        public int TotalUsers { get; set; }
        public int TotalQuizAttempts { get; set; }
        public decimal AverageScore { get; set; }
        public List<UserQuizProgressSummary> UserProgress { get; set; } = new();
    }

    /// <summary>
    /// Response for training program quiz progress
    /// </summary>
    public class TrainingProgramQuizProgressResponse
    {
        public int ProgramId { get; set; }
        public string ProgramName { get; set; } = "";
        public int TotalQuizzes { get; set; }
        public int TotalUsers { get; set; }
        public decimal AverageScore { get; set; }
        public List<UserQuizProgressSummary> UserProgress { get; set; } = new();
    }

    /// <summary>
    /// Response for learning path quiz progress
    /// </summary>
    public class LearningPathQuizProgressResponse
    {
        public int LearningPathId { get; set; }
        public string LearningPathName { get; set; } = "";
        public int TotalQuizzes { get; set; }
        public int TotalUsers { get; set; }
        public decimal AverageScore { get; set; }
        public List<UserQuizProgressSummary> UserProgress { get; set; } = new();
    }

    /// <summary>
    /// Response for single material quiz progress
    /// </summary>
    public class MaterialQuizProgressResponse
    {
        public int MaterialId { get; set; }
        public string MaterialName { get; set; } = "";
        public int TotalAttempts { get; set; }
        public decimal AverageScore { get; set; }
        public List<UserQuizProgressSummary> UserProgress { get; set; } = new();
    }
}
