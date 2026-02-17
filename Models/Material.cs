// Updated Material.cs with all properties

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace XR50TrainingAssetRepo.Models
{
    public class Material
    {
        public string? Description { get; set; }
        public string? Name { get; set; }
        public DateTime? Created_at { get; set; }
        public DateTime? Updated_at { get; set; }

        [Key]
        public int id { get; set; }
        [Required]
        public Type Type { get; set; }
        public int? Unique_id { get; set; }
        [JsonIgnore]
        public virtual ICollection<ProgramMaterial> ProgramMaterials { get; set; } = new List<ProgramMaterial>();
        [JsonIgnore]
        public virtual ICollection<MaterialRelationship> MaterialRelationships { get; set; } = new List<MaterialRelationship>();
        public Material()
        {

        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Type
    {
        [EnumMember(Value = "image")]
        Image,
        [EnumMember(Value = "video")]
        Video,
        [EnumMember(Value = "pdf")]
        PDF,
        [EnumMember(Value = "unity")]
        Unity,
        [EnumMember(Value = "chatbot")]
        Chatbot,
        [EnumMember(Value = "questionnaire")]
        Questionnaire,
        [EnumMember(Value = "checklist")]
        Checklist,
        [EnumMember(Value = "workflow")]
        Workflow,
        [EnumMember(Value = "mqtt_template")]
        MQTT_Template,
        [EnumMember(Value = "answers")]
        Answers,
        [EnumMember(Value = "quiz")]
        Quiz,
        [EnumMember(Value = "default")]
        Default,
        [EnumMember(Value = "ai_assistant")]
        AIAssistant
    }

    public class ChecklistMaterial : Material
    {

        public List<ChecklistEntry> Entries { get; set; }

        public ChecklistMaterial()
        {
            Entries = new List<ChecklistEntry>();
            Type = Type.Checklist;
        }
    }

    public class ImageMaterial : Material
    {

        public int? AssetId { get; set; }
        public string? ImagePath { get; set; }
        public int? ImageWidth { get; set; }
        public int? ImageHeight { get; set; }
        public string? ImageFormat { get; set; }

        public List<ImageAnnotation> ImageAnnotations { get; set; }

        public ImageMaterial()
        {
            ImageAnnotations = new List<ImageAnnotation>();
            Type = Type.Image;
        }
    }

    public class VideoMaterial : Material
    {

        public int? AssetId { get; set; }
        public string? VideoPath { get; set; }
        public int? VideoDuration { get; set; }
        public string? VideoResolution { get; set; }
        public string? startTime { get; set; }

        [Column(TypeName = "json")]
        public string? Annotations { get; set; }

        public List<VideoTimestamp> Timestamps { get; set; }

        public VideoMaterial()
        {
            Timestamps = new List<VideoTimestamp>();
            Type = Type.Video;
        }
    }

    public class WorkflowMaterial : Material
    {
        public List<WorkflowStep> WorkflowSteps { get; set; }
        
        public WorkflowMaterial()
        {
            WorkflowSteps = new List<WorkflowStep>();
            Type = Type.Workflow;
        }
    }

    public class MQTT_TemplateMaterial : Material
    {
        // MQTT-specific properties stored in Materials table
        public string? message_type { get; set; }
        public string? message_text { get; set; }
        
        public MQTT_TemplateMaterial()
        {
            Type = Type.MQTT_Template;
        }
    }

    public class PDFMaterial : Material
    {

        public int? AssetId { get; set; }
        public string? PdfPath { get; set; }
        public int? PdfPageCount { get; set; }
        public long? PdfFileSize { get; set; }
        
        public PDFMaterial()
        {
            Type = Type.PDF;
        }
    }

    public class UnityMaterial : Material
    {
        public int? AssetId { get; set; }
        public string? UnityVersion { get; set; }
        public string? UnityBuildTarget { get; set; }
        public string? UnitySceneName { get; set; }
        public string? UnityJson { get; set; }

        public UnityMaterial()
        {
            Type = Type.Unity;
        }
    }

    public class ChatbotMaterial : Material
    {

        public string? ChatbotConfig { get; set; }  
        public string? ChatbotModel { get; set; }   
        public string? ChatbotPrompt { get; set; } 
        
        public ChatbotMaterial()
        {
            Type = Type.Chatbot;
        }
    }

    public class QuestionnaireMaterial : Material
    {
        public string? QuestionnaireConfig { get; set; }  
        public string? QuestionnaireType { get; set; }    
        public decimal? PassingScore { get; set; }       
        
        public List<QuestionnaireEntry> QuestionnaireEntries { get; set; }
        
        public QuestionnaireMaterial()
        {
            QuestionnaireEntries = new List<QuestionnaireEntry>();
            Type = Type.Questionnaire;
        }
    }

    public class DefaultMaterial : Material
    {
        // Generic material with asset support
        public int? AssetId { get; set; }

        public DefaultMaterial()
        {
            Type = Type.Default;
        }
    }

    /// <summary>
    /// AI Assistant material for AI-processed document extraction.
    /// Supports multiple assets and tracks processing status via Chatbot API.
    /// Sessions are created on first /ask call and reused for subsequent calls.
    /// </summary>
    public class AIAssistantMaterial : Material
    {
        /// <summary>
        /// Chatbot service job ID for tracking the overall processing job
        /// </summary>
        public string? ServiceJobId { get; set; }

        /// <summary>
        /// Processing status: "ready", "process", "notready"
        /// </summary>
        public string AIAssistantStatus { get; set; } = "notready";

        /// <summary>
        /// JSON array of asset IDs associated with this AI Assistant material.
        /// Example: "[1, 3, 5]"
        /// </summary>
        public string? AIAssistantAssetIds { get; set; }

        /// <summary>
        /// Current active session for this AI Assistant material
        /// </summary>
        [JsonIgnore]
        public AIAssistantSession? CurrentSession { get; set; }

        public AIAssistantMaterial()
        {
            Type = Type.AIAssistant;
        }

        /// <summary>
        /// Helper to get asset IDs as a list
        /// </summary>
        public List<int> GetAssetIdsList()
        {
            if (string.IsNullOrEmpty(AIAssistantAssetIds))
                return new List<int>();

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<int>>(AIAssistantAssetIds) ?? new List<int>();
            }
            catch
            {
                return new List<int>();
            }
        }

        /// <summary>
        /// Helper to set asset IDs from a list
        /// </summary>
        public void SetAssetIdsList(List<int> assetIds)
        {
            AIAssistantAssetIds = System.Text.Json.JsonSerializer.Serialize(assetIds);
        }
    }

    /// <summary>
    /// Stores the Siemens API session for an AIAssistantMaterial.
    /// One active session per material. Session is invalidated when assets change.
    /// </summary>
    public class AIAssistantSession
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Foreign key to the parent AIAssistantMaterial
        /// </summary>
        public int AIAssistantMaterialId { get; set; }

        /// <summary>
        /// The session_id returned by the Siemens API
        /// </summary>
        [Required]
        [StringLength(500)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Status: "active", "invalidated"
        /// </summary>
        [StringLength(20)]
        public string Status { get; set; } = "active";

        /// <summary>
        /// When the session was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Hash of asset filenames to detect changes
        /// </summary>
        [StringLength(64)]
        public string? AssetHash { get; set; }

        /// <summary>
        /// Navigation property back to parent
        /// </summary>
        [JsonIgnore]
        public AIAssistantMaterial? AIAssistantMaterial { get; set; }
    }

    public class QuizMaterial : Material
    {
        public bool EvaluationMode { get; set; } = false;
        public int? MinScore { get; set; }
        public List<QuizQuestion> Questions { get; set; }

        public QuizMaterial()
        {
            Questions = new List<QuizQuestion>();
            Type = Type.Quiz;
        }
    }

    public class MaterialRelationship
    {
        [Key]
        public int Id { get; set; }

        public int MaterialId { get; set; }
        public string RelatedEntityId { get; set; }
        public string RelatedEntityType { get; set; }
        public string? RelationshipType { get; set; }
        public int? DisplayOrder { get; set; }

        // Rank configuration for materials within learning paths
        public bool? inherit_from_program { get; set; }
        public int? min_level_rank { get; set; }
        public int? max_level_rank { get; set; }
        public int? required_upto_level_rank { get; set; }

        public virtual Material Material { get; set; }
    }

    public class SubcomponentMaterialRelationship
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int SubcomponentId { get; set; }

        [Required]
        [StringLength(50)]
        public string SubcomponentType { get; set; } = "";

        [Required]
        public int RelatedMaterialId { get; set; }

        [StringLength(50)]
        public string? RelationshipType { get; set; }

        public int? DisplayOrder { get; set; }

        public virtual Material? RelatedMaterial { get; set; }
    }

    public class QuizQuestion
    {
        [Key]
        public int QuizQuestionId { get; set; }

        public int QuestionNumber { get; set; }

        [Required]
        [StringLength(50)]
        public string QuestionType { get; set; } = "text"; // text, multiple-choice, scale, etc.

        [Required]
        [StringLength(2000)]
        public string Text { get; set; } = "";

        [StringLength(2000)]
        public string? Description { get; set; }

        public decimal? Score { get; set; }

        [StringLength(1000)]
        public string? HelpText { get; set; }

        public bool AllowMultiple { get; set; } = false;

        [StringLength(500)]
        public string? ScaleConfig { get; set; } // JSON for scale configuration

        public int QuizMaterialId { get; set; }

        public List<QuizAnswer> Answers { get; set; }

        public QuizQuestion()
        {
            Answers = new List<QuizAnswer>();
        }
    }

    public class QuizAnswer
    {
        [Key]
        public int QuizAnswerId { get; set; }

        [Required]
        [StringLength(1000)]
        public string Text { get; set; } = "";

        public bool CorrectAnswer { get; set; } = false;

        public int? DisplayOrder { get; set; }

        [StringLength(500)]
        public string? Extra { get; set; }

        public int QuizQuestionId { get; set; }
    }
}