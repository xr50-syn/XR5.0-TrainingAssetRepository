// Updated Material.cs with all properties

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using XR50TrainingAssetRepo.Controllers;
using XR50TrainingAssetRepo.Models;

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
        public int? UniqueId { get; set; }
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
        Image,
        Video,
        PDF,
        Unity,
        Chatbot,
        Questionnaire,
        Checklist,
        Workflow,
        MQTT_Template,
        Answers,
        Quiz,
        Default
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
        
        public ImageMaterial()
        {
            Type = Type.Image;
        }
    }

    public class VideoMaterial : Material
    {

        public int? AssetId { get; set; }
        public string? VideoPath { get; set; }
        public int? VideoDuration { get; set; }  
        public string? VideoResolution { get; set; }  
        

        public List<VideoTimestamp> VideoTimestamps { get; set; }
        
        public VideoMaterial()
        {
            VideoTimestamps = new List<VideoTimestamp>();
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

    public class QuizMaterial : Material
    {
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


        public virtual Material Material { get; set; }
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

        public bool IsCorrect { get; set; } = false;

        public int? DisplayOrder { get; set; }

        public int QuizQuestionId { get; set; }
    }
}