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
        public decimal score { get; set; }
        public int progress { get; set; }
        public string? message { get; set; }
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
