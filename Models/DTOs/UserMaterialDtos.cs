using System.ComponentModel.DataAnnotations;

namespace XR50TrainingAssetRepo.Models.DTOs
{
    #region Submit Request DTOs

    /// <summary>
    /// Request body for POST /api/materials/{material_id}/submit
    /// </summary>
    public class SubmitQuizAnswersRequest
    {
        public int? program_id { get; set; }

        [Required]
        public List<QuestionAnswerRequest> questions { get; set; } = new();
    }

    public class QuestionAnswerRequest
    {
        [Required]
        public int question_id { get; set; }

        [Required]
        public AnswerRequest answer { get; set; } = new();
    }

    public class AnswerRequest
    {
        /// <summary>
        /// For boolean, multiple-choice, single-choice questions
        /// </summary>
        public List<int>? answer_ids { get; set; }

        /// <summary>
        /// For scale questions (numeric value)
        /// </summary>
        public int? value { get; set; }

        /// <summary>
        /// For free-text questions
        /// </summary>
        public string? text { get; set; }
    }

    #endregion

    #region Submit Response DTOs

    public class SubmitQuizAnswersResponse
    {
        public bool success { get; set; }
        public int material_id { get; set; }
        public int? program_id { get; set; }
        public int? learning_path_id { get; set; }
        public decimal score { get; set; }
        public int progress { get; set; }
        public int? learning_path_progress { get; set; }
        public string? message { get; set; }
    }

    #endregion

    #region Mark Complete DTOs

    /// <summary>
    /// Request body for POST /api/materials/{material_id}/complete
    /// </summary>
    public class MarkMaterialCompleteRequest
    {
        [Required]
        public int program_id { get; set; }
    }

    /// <summary>
    /// Response for POST /api/materials/{material_id}/complete
    /// </summary>
    public class MarkMaterialCompleteResponse
    {
        public bool success { get; set; }
        public int material_id { get; set; }
        public int program_id { get; set; }
        public int? learning_path_id { get; set; }
        public int progress { get; set; }
        public int? learning_path_progress { get; set; }
        public string? message { get; set; }
    }

    #endregion

    #region Bulk Complete DTOs

    /// <summary>
    /// Request body for POST /api/programs/{program_id}/submit
    /// </summary>
    public class BulkMaterialCompleteRequest
    {
        [Required]
        public List<int> material_ids { get; set; } = new();
    }

    /// <summary>
    /// Response for POST /api/programs/{program_id}/submit
    /// </summary>
    public class BulkMaterialCompleteResponse
    {
        public bool success { get; set; }
        public int program_id { get; set; }
        public int program_progress { get; set; }
        public int materials_completed { get; set; }
        public List<MaterialCompleteResult> results { get; set; } = new();
        public List<LearningPathProgressSummary> learning_path_summary { get; set; } = new();
    }

    public class MaterialCompleteResult
    {
        public int material_id { get; set; }
        public bool success { get; set; }
        public string? error { get; set; }
        public int? learning_path_id { get; set; }
        public int? learning_path_progress { get; set; }
    }

    public class LearningPathProgressSummary
    {
        public int learning_path_id { get; set; }
        public string? learning_path_name { get; set; }
        public int progress { get; set; }
    }

    #endregion

    #region Program Progress Query DTOs

    /// <summary>
    /// Response for GET /api/program-progress/program/{programId}
    /// Shows progress for all users (admin) or just the requesting user
    /// </summary>
    public class ProgramProgressResponse
    {
        public int program_id { get; set; }
        public string program_name { get; set; } = "";
        public int total_materials { get; set; }
        public int total_users { get; set; }
        public double average_progress { get; set; }
        public List<UserProgramProgressDetail> user_progress { get; set; } = new();
        public List<LearningPathProgressSummary> learning_paths { get; set; } = new();
    }

    public class UserProgramProgressDetail
    {
        public string user_id { get; set; } = "";
        public string user_name { get; set; } = "";
        public int progress { get; set; }
        public int materials_completed { get; set; }
        public int total_materials { get; set; }
        public DateTime? last_activity { get; set; }
        public List<MaterialProgressDetail> materials { get; set; } = new();
    }

    public class MaterialProgressDetail
    {
        public int material_id { get; set; }
        public string material_name { get; set; } = "";
        public string material_type { get; set; } = "";
        public bool completed { get; set; }
        public decimal? score { get; set; }
        public int? learning_path_id { get; set; }
        public DateTime? completed_at { get; set; }
    }

    #endregion

    #region Stored Data DTOs (JSON structure for user_material_data.data)

    public class ProcessedAnswerData
    {
        public int version { get; set; } = 1;
        public DateTime submitted_at { get; set; }
        public List<ProcessedAnswer> answers { get; set; } = new();
        public decimal total_score { get; set; }
    }

    public class ProcessedAnswer
    {
        public int question_id { get; set; }
        public string type { get; set; } = "";
        public List<int>? answer_ids { get; set; }
        public int? value { get; set; }
        public string? text { get; set; }
        public decimal score_awarded { get; set; }
        public bool is_correct { get; set; }
    }

    #endregion

    #region Read Response DTOs

    /// <summary>
    /// Response for GET /api/users/progress
    /// </summary>
    public class UserProgressResponse
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public int progress { get; set; }
        public List<ProgramProgressDto> programs { get; set; } = new();
        public List<StandaloneMaterialProgressDto> standalone_materials { get; set; } = new();
    }

    public class ProgramProgressDto
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public int progress { get; set; }
        public List<MaterialScoreDto> materials { get; set; } = new();
    }

    public class StandaloneMaterialProgressDto
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string type { get; set; } = "";
        public decimal score { get; set; }
    }

    public class MaterialScoreDto
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string type { get; set; } = "";
        public decimal score { get; set; }
        public bool completed { get; set; }
    }

    /// <summary>
    /// Response for GET /api/users/{user_id}/materials/{material_id}
    /// </summary>
    public class UserMaterialDetailResponse
    {
        public string user_id { get; set; } = "";
        public int? program_id { get; set; }
        public int material_id { get; set; }
        public decimal score { get; set; }
        public ProcessedAnswerData? data { get; set; }
    }

    /// <summary>
    /// Response for GET /api/users/{user_id}/programs/{program_id}/materials
    /// </summary>
    public class UserProgramMaterialsResponse
    {
        public string user_id { get; set; } = "";
        public int program_id { get; set; }
        public string program_name { get; set; } = "";
        public int progress { get; set; }
        public List<UserMaterialDetailResponse> materials { get; set; } = new();
    }

    #endregion
}
