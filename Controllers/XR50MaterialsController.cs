using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Materials;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Controllers
{
     public class FileUploadFormDataWithMaterial
        {
            public string materialData { get; set; }
            public string? assetData { get; set; }
            public IFormFile? File { get; set; }
        }

    /// <summary>
    /// Form data model for creating materials with optional file upload.
    /// Used by the unified POST /materials endpoint.
    /// </summary>
    public class MaterialCreateFormData
    {
        /// <summary>
        /// JSON string containing the material data (name, description, type, etc.)
        /// </summary>
        public string material { get; set; } = string.Empty;

        /// <summary>
        /// Optional JSON string containing asset metadata
        /// </summary>
        public string? assetData { get; set; }

        /// <summary>
        /// Optional file to upload as an asset
        /// </summary>
        public IFormFile? file { get; set; }
    }
    [Route("api/{tenantName}/[controller]")]
    [ApiController]
    public class materialsController : ControllerBase
    {
        private readonly IMaterialService _materialService;
        private readonly IAssetService _assetService;
        private readonly ILearningPathService _learningPathService;
        private readonly IVoiceMaterialService _voiceMaterialService;
        private readonly IUserMaterialService _userMaterialService;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<materialsController> _logger;

        public materialsController(
            IMaterialService materialService,
            IAssetService assetService,
            ILearningPathService learningPathService,
            IVoiceMaterialService voiceMaterialService,
            IUserMaterialService userMaterialService,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<materialsController> logger)
        {
            _materialService = materialService;
            _assetService = assetService;
            _learningPathService = learningPathService;
            _voiceMaterialService = voiceMaterialService;
            _userMaterialService = userMaterialService;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        // GET: api/{tenantName}/materials
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Material>>> GetMaterials(string tenantName)
        {
            _logger.LogInformation("Getting materials for tenant: {TenantName}", tenantName);

            var materials = await _materialService.GetAllMaterialsAsync();

            _logger.LogInformation("Found {MaterialCount} materials for tenant: {TenantName}",
                materials.Count(), tenantName);

            return Ok(materials);
        }

        // GET: api/{tenantName}/materials/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Material>> GetMaterial(string tenantName, int id)
        {
            _logger.LogInformation("Getting material {Id} for tenant: {TenantName}", id, tenantName);

            var material = await _materialService.GetMaterialAsync(id);

            if (material == null)
            {
                _logger.LogWarning("Material {Id} not found in tenant: {TenantName}", id, tenantName);
                return NotFound();
            }

            return material;
        }

        /// <summary>
        /// Submit quiz answers for evaluation
        /// POST /api/{tenantName}/materials/{materialId}/submit
        /// Requires authentication - user ID is extracted from JWT token claims
        /// In development mode with AllowAnonymousInDevelopment=true, uses DevelopmentUserId fallback
        /// </summary>
        [HttpPost("{materialId}/submit")]
        [Authorize(Policy = "RequireAuthenticatedUser")]
        public async Task<ActionResult<SubmitQuizAnswersResponse>> SubmitAnswers(
            string tenantName,
            int materialId,
            [FromBody] SubmitQuizAnswersRequest request)
        {
            try
            {
                // Log all claims for debugging
                _logger.LogInformation("Token claims: {Claims}",
                    string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));

                // Extract user ID from JWT token claims
                // For Keycloak: prefer "preferred_username" over "sub" (which is a UUID)
                // Fallback order: preferred_username -> name -> email -> sub (UUID)
                var userId = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst(ClaimTypes.Name)?.Value
                    ?? User.FindFirst("name")?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value
                    ?? User.FindFirst("email")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

                // Development fallback: allow configured test user when AllowAnonymousInDevelopment is true
                if (string.IsNullOrEmpty(userId) && _environment.IsDevelopment())
                {
                    var allowAnonymous = _configuration.GetValue<bool>("IAM:AllowAnonymousInDevelopment", false);
                    if (allowAnonymous)
                    {
                        userId = _configuration.GetValue<string>("IAM:DevelopmentUserId") ?? "dev-test-user";
                        _logger.LogWarning("Using development fallback user ID: {UserId}", userId);
                    }
                }

                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("No user identifier found in token claims. Available claims: {Claims}",
                        string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));
                    return Unauthorized(new { error = "User identifier not found in token" });
                }

                _logger.LogInformation(
                    "User {UserId} submitting answers for material {MaterialId} in tenant {TenantName}",
                    userId, materialId, tenantName);

                var result = await _userMaterialService.SubmitAnswersAsync(
                    userId, materialId, request);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error submitting answers");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting answers for material {MaterialId}", materialId);
                return StatusCode(500, new { error = "Failed to submit answers", details = ex.Message });
            }
        }

/// Get complete material details with all type-specific properties and child entities
/// This replaces the need to call different endpoints for different material types

[HttpGet("{id}/detail")]
public async Task<ActionResult<object>> GetCompleteMaterialDetails(string tenantName, int id)
{
    try
    {
        _logger.LogInformation("Getting complete details for material: {MaterialId} in tenant: {TenantName}", id, tenantName);

        // First get the basic material to determine type
        var baseMaterial = await _materialService.GetMaterialAsync(id);
        if (baseMaterial == null)
        {
            _logger.LogWarning("Material not found: {MaterialId}", id);
            return NotFound(new { Error = $"Material with ID {id} not found" });
        }

        // Use the existing service methods that work with Include() patterns
        object materialDetails = baseMaterial.Type switch
        {
            MaterialType.Workflow => await GetWorkflowDetails(id),
            MaterialType.Video => await GetVideoDetails(id),
            MaterialType.Checklist => await GetChecklistDetails(id),
            MaterialType.Questionnaire => await GetQuestionnaireDetails(id),
            MaterialType.Quiz => await GetQuizDetails(id),
            MaterialType.Image => await GetImageDetails(id),
            MaterialType.PDF => await GetPDFDetails(id),
            MaterialType.Unity => await GetUnityDetails(id),
            MaterialType.Chatbot => await GetChatbotDetails(id),
            MaterialType.MQTT_Template => await GetMQTTTemplateDetails(id),
            MaterialType.Voice => await GetVoiceDetails(id),
            _ => await GetBasicMaterialDetails(id)
        };

        if (materialDetails == null)
        {
            return NotFound(new { Error = $"Material with ID {id} not found" });
        }

        _logger.LogInformation("Retrieved complete details for material: {MaterialId} (Type: {MaterialType})", 
            id, baseMaterial.Type);
        
        return Ok(materialDetails);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting complete material details: {MaterialId}", id);
        return StatusCode(500, new { Error = "Failed to retrieve material details", Details = ex.Message });
    }
}

private async Task<object?> GetWorkflowDetails(int materialId)
{
    var workflow = await _materialService.GetWorkflowMaterialWithStepsAsync(materialId);
    if (workflow == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    // Build steps with their related materials
    var stepsWithRelated = new List<object>();
    if (workflow.WorkflowSteps != null)
    {
        foreach (var ws in workflow.WorkflowSteps)
        {
            var stepRelated = await GetSubcomponentRelatedMaterialsAsync(ws.Id, "WorkflowStep");
            stepsWithRelated.Add(new
            {
                id = ws.Id.ToString(),
                title = ws.Title,
                content = ws.Content,
                related = stepRelated
            });
        }
    }

    return new
    {
        id = workflow.id.ToString(),
        name = workflow.Name,
        description = workflow.Description,
        type = GetLowercaseType(workflow.Type),
        unique_id = workflow.Unique_id,
        created_at = workflow.Created_at,
        updated_at = workflow.Updated_at,
        config = new
        {
            steps = stepsWithRelated
        },
        related = related
    };
}

private async Task<object?> GetVideoDetails(int materialId)
{
    var video = await _materialService.GetVideoMaterialWithTimestampsAsync(materialId);
    if (video == null) return null;

    // Fetch full asset if AssetId exists
    object? asset = null;
    if (video.AssetId.HasValue)
    {
        var assetEntity = await _assetService.GetAssetAsync(video.AssetId.Value);
        if (assetEntity != null)
        {
            asset = new
            {
                id = assetEntity.Id,
                filename = assetEntity.Filename,
                description = assetEntity.Description,
                filetype = assetEntity.Filetype,
                src = assetEntity.Src,
                url = assetEntity.URL
            };
        }
    }

    var related = await GetRelatedMaterialsAsync(materialId);

    // Build timestamps with their related materials
    var timestampsWithRelated = new List<object>();
    if (video.Timestamps != null)
    {
        foreach (var vt in video.Timestamps)
        {
            var timestampRelated = await GetSubcomponentRelatedMaterialsAsync(vt.id, "VideoTimestamp");
            timestampsWithRelated.Add(new
            {
                id = vt.id.ToString(),
                title = vt.Title,
                startTime = vt.startTime,
                endTime = vt.endTime,
                duration = vt.Duration,
                description = vt.Description,
                type = vt.Type,
                related = timestampRelated
            });
        }
    }

    return new
    {
        id = video.id.ToString(),
        name = video.Name,
        description = video.Description,
        type = GetLowercaseType(video.Type),
        unique_id = video.Unique_id,
        created_at = video.Created_at,
        updated_at = video.Updated_at,
        asset = asset,
        videoPath = video.VideoPath,
        videoDuration = video.VideoDuration,
        videoResolution = video.VideoResolution,
        startTime = video.startTime,
        annotations = video.Annotations,
        config = new
        {
            timestamps = timestampsWithRelated
        },
        related = related
    };
}

private async Task<object?> GetChecklistDetails(int materialId)
{
    var checklist = await _materialService.GetChecklistMaterialWithEntriesAsync(materialId);
    if (checklist == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    // Build entries with their related materials
    var entriesWithRelated = new List<object>();
    if (checklist.Entries != null)
    {
        foreach (var ce in checklist.Entries)
        {
            var entryRelated = await GetSubcomponentRelatedMaterialsAsync(ce.ChecklistEntryId, "ChecklistEntry");
            entriesWithRelated.Add(new
            {
                id = ce.ChecklistEntryId.ToString(),
                text = ce.Text,
                description = ce.Description,
                related = entryRelated
            });
        }
    }

    return new
    {
        id = checklist.id.ToString(),
        name = checklist.Name,
        description = checklist.Description,
        type = GetLowercaseType(checklist.Type),
        unique_id = checklist.Unique_id,
        created_at = checklist.Created_at,
        updated_at = checklist.Updated_at,
        config = new
        {
            entries = entriesWithRelated
        },
        related = related
    };
}

private async Task<object?> GetQuestionnaireDetails(int materialId)
{
    var questionnaire = await _materialService.GetQuestionnaireMaterialWithEntriesAsync(materialId);
    if (questionnaire == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    // Build entries with their related materials
    var entriesWithRelated = new List<object>();
    if (questionnaire.QuestionnaireEntries != null)
    {
        foreach (var qe in questionnaire.QuestionnaireEntries)
        {
            var entryRelated = await GetSubcomponentRelatedMaterialsAsync(qe.QuestionnaireEntryId, "QuestionnaireEntry");
            entriesWithRelated.Add(new
            {
                id = qe.QuestionnaireEntryId.ToString(),
                text = qe.Text,
                description = qe.Description,
                related = entryRelated
            });
        }
    }

    return new
    {
        id = questionnaire.id.ToString(),
        name = questionnaire.Name,
        description = questionnaire.Description,
        type = GetLowercaseType(questionnaire.Type),
        unique_id = questionnaire.Unique_id,
        created_at = questionnaire.Created_at,
        updated_at = questionnaire.Updated_at,
        questionnaireType = questionnaire.QuestionnaireType,
        passingScore = questionnaire.PassingScore,
        questionnaireConfig = questionnaire.QuestionnaireConfig,
        config = new
        {
            entries = entriesWithRelated
        },
        related = related
    };
}

private async Task<object?> GetQuizDetails(int materialId)
{
    var quiz = await _materialService.GetQuizMaterialWithQuestionsAsync(materialId);
    if (quiz == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    // Build questions with their related materials
    var questionsWithRelated = new List<object>();
    if (quiz.Questions != null)
    {
        foreach (var q in quiz.Questions)
        {
            var questionRelated = await GetSubcomponentRelatedMaterialsAsync(q.QuizQuestionId, "QuizQuestion");

            // Build answers with their related materials
            var answersWithRelated = new List<object>();
            if (q.Answers != null)
            {
                foreach (var a in q.Answers)
                {
                    var answerRelated = await GetSubcomponentRelatedMaterialsAsync(a.QuizAnswerId, "QuizAnswer");
                    answersWithRelated.Add(new
                    {
                        id = a.QuizAnswerId,
                        text = a.Text,
                        correctAnswer = a.CorrectAnswer,
                        displayOrder = a.DisplayOrder,
                        extra = a.Extra,
                        related = answerRelated
                    });
                }
            }

            questionsWithRelated.Add(new
            {
                Id = q.QuizQuestionId,
                QuestionNumber = q.QuestionNumber,
                QuestionType = q.QuestionType,
                Text = q.Text,
                Description = q.Description,
                Score = q.Score,
                HelpText = q.HelpText,
                AllowMultiple = q.AllowMultiple,
                ScaleConfig = q.ScaleConfig,
                Answers = answersWithRelated,
                related = questionRelated
            });
        }
    }

    return new
    {
        id = quiz.id.ToString(),
        Name = quiz.Name,
        Description = quiz.Description,
        Type = GetLowercaseType(quiz.Type),
        Unique_id = quiz.Unique_id,
        Created_at = quiz.Created_at,
        Updated_at = quiz.Updated_at,
        Config = new
        {
            Questions = questionsWithRelated
        },
        Related = related
    };
}

private async Task<object?> GetImageDetails(int materialId)
{
    var image = await _materialService.GetImageMaterialAsync(materialId);
    if (image == null) return null;

    // Fetch full asset if AssetId exists
    object? asset = null;
    if (image.AssetId.HasValue)
    {
        var assetEntity = await _assetService.GetAssetAsync(image.AssetId.Value);
        if (assetEntity != null)
        {
            asset = new
            {
                Id = assetEntity.Id,
                Filename = assetEntity.Filename,
                Description = assetEntity.Description,
                Filetype = assetEntity.Filetype,
                Src = assetEntity.Src,
                URL = assetEntity.URL
            };
        }
    }

    // Get related materials
    var related = await GetRelatedMaterialsAsync(materialId);

    // Get related materials for each annotation
    var annotationsWithRelated = new List<object>();
    if (image.ImageAnnotations != null)
    {
        foreach (var annotation in image.ImageAnnotations)
        {
            var annotationRelated = await GetAnnotationRelatedMaterialsAsync(annotation.ImageAnnotationId);
            annotationsWithRelated.Add(new
            {
                id = annotation.ImageAnnotationId,
                clientId = annotation.ClientId,
                text = annotation.Text,
                fontsize = annotation.FontSize,
                x = annotation.X,
                y = annotation.Y,
                related = annotationRelated
            });
        }
    }

    return new
    {
        id = image.id.ToString(),
        Name = image.Name,
        Description = image.Description,
        Type = GetLowercaseType(image.Type),
        Unique_id = image.Unique_id,
        Created_at = image.Created_at,
        Updated_at = image.Updated_at,
        Asset = asset,
        ImagePath = image.ImagePath,
        ImageWidth = image.ImageWidth,
        ImageHeight = image.ImageHeight,
        ImageFormat = image.ImageFormat,
        Config = new
        {
            Annotations = annotationsWithRelated
        },
        Related = related
    };
}

private async Task<object?> GetPDFDetails(int materialId)
{
    var pdf = await _materialService.GetPDFMaterialAsync(materialId);
    if (pdf == null) return null;

    // Fetch full asset if AssetId exists
    object? asset = null;
    if (pdf.AssetId.HasValue)
    {
        var assetEntity = await _assetService.GetAssetAsync(pdf.AssetId.Value);
        if (assetEntity != null)
        {
            asset = new
            {
                Id = assetEntity.Id,
                Filename = assetEntity.Filename,
                Description = assetEntity.Description,
                Filetype = assetEntity.Filetype,
                Src = assetEntity.Src,
                URL = assetEntity.URL
            };
        }
    }

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = pdf.id.ToString(),
        Name = pdf.Name,
        Description = pdf.Description,
        Type = GetLowercaseType(pdf.Type),
        Unique_id = pdf.Unique_id,
        Created_at = pdf.Created_at,
        Updated_at = pdf.Updated_at,
        Asset = asset,
        PdfPath = pdf.PdfPath,
        PdfPageCount = pdf.PdfPageCount,
        PdfFileSize = pdf.PdfFileSize,
        Related = related
    };
}

private async Task<object?> GetUnityDetails(int materialId)
{
    var unity = await _materialService.GetUnityMaterialAsync(materialId);
    if (unity == null) return null;

    // Fetch full asset if AssetId exists
    object? asset = null;
    if (unity.AssetId.HasValue)
    {
        var assetEntity = await _assetService.GetAssetAsync(unity.AssetId.Value);
        if (assetEntity != null)
        {
            asset = new
            {
                Id = assetEntity.Id,
                Filename = assetEntity.Filename,
                Description = assetEntity.Description,
                Filetype = assetEntity.Filetype,
                Src = assetEntity.Src,
                URL = assetEntity.URL
            };
        }
    }

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = unity.id.ToString(),
        Name = unity.Name,
        Description = unity.Description,
        Type = GetLowercaseType(unity.Type),
        Unique_id = unity.Unique_id,
        Created_at = unity.Created_at,
        Updated_at = unity.Updated_at,
        Asset = asset,
        UnityVersion = unity.UnityVersion,
        UnityBuildTarget = unity.UnityBuildTarget,
        UnitySceneName = unity.UnitySceneName,
        UnityJson = unity.UnityJson,
        Related = related
    };
}

private async Task<object?> GetChatbotDetails(int materialId)
{
    var chatbot = await _materialService.GetChatbotMaterialAsync(materialId);
    if (chatbot == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = chatbot.id.ToString(),
        Name = chatbot.Name,
        Description = chatbot.Description,
        Type = GetLowercaseType(chatbot.Type),
        Unique_id = chatbot.Unique_id,
        Created_at = chatbot.Created_at,
        Updated_at = chatbot.Updated_at,
        ChatbotConfig = chatbot.ChatbotConfig,
        ChatbotModel = chatbot.ChatbotModel,
        ChatbotPrompt = chatbot.ChatbotPrompt,
        Related = related
    };
}

private async Task<object?> GetMQTTTemplateDetails(int materialId)
{
    var mqtt = await _materialService.GetMQTTTemplateMaterialAsync(materialId);
    if (mqtt == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = mqtt.id.ToString(),
        Name = mqtt.Name,
        Description = mqtt.Description,
        Type = GetLowercaseType(mqtt.Type),
        Unique_id = mqtt.Unique_id,
        Created_at = mqtt.Created_at,
        Updated_at = mqtt.Updated_at,
        MessageType = mqtt.message_type,
        MessageText = mqtt.message_text,
        Related = related
    };
}

private async Task<object?> GetVoiceDetails(int materialId)
{
    var voice = await _voiceMaterialService.GetByIdAsync(materialId);
    if (voice == null) return null;

    // Get all assets for this voice material
    var assetIds = voice.GetAssetIdsList();
    var assets = new List<object>();

    foreach (var assetId in assetIds)
    {
        var assetEntity = await _assetService.GetAssetAsync(assetId);
        if (assetEntity != null)
        {
            assets.Add(new
            {
                Id = assetEntity.Id,
                Filename = assetEntity.Filename,
                Description = assetEntity.Description,
                Filetype = assetEntity.Filetype,
                Src = assetEntity.Src,
                URL = assetEntity.URL,
                AiAvailable = assetEntity.AiAvailable,
                JobId = assetEntity.JobId
            });
        }
    }

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = voice.id.ToString(),
        Name = voice.Name,
        Description = voice.Description,
        Type = GetLowercaseType(voice.Type),
        Unique_id = voice.Unique_id,
        Created_at = voice.Created_at,
        Updated_at = voice.Updated_at,
        ServiceJobId = voice.ServiceJobId,
        VoiceStatus = voice.VoiceStatus,
        Assets = assets,
        Related = related
    };
}

private async Task<object?> GetBasicMaterialDetails(int materialId)
{
    var material = await _materialService.GetMaterialAsync(materialId);
    if (material == null) return null;

    // Check if it's a DefaultMaterial with an asset
    object? asset = null;
    if (material is DefaultMaterial defaultMaterial && defaultMaterial.AssetId.HasValue)
    {
        var assetEntity = await _assetService.GetAssetAsync(defaultMaterial.AssetId.Value);
        if (assetEntity != null)
        {
            asset = new
            {
                Id = assetEntity.Id,
                Filename = assetEntity.Filename,
                Description = assetEntity.Description,
                Filetype = assetEntity.Filetype,
                Src = assetEntity.Src,
                URL = assetEntity.URL
            };
        }
    }

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = material.id.ToString(),
        Name = material.Name,
        Description = material.Description,
        Type = GetLowercaseType(material.Type),
        Unique_id = material.Unique_id,
        Created_at = material.Created_at,
        Updated_at = material.Updated_at,
        Asset = asset,
        Related = related
    };
}
        [HttpGet("{id}/typed")]
        public async Task<ActionResult<Material>> GetCompleteMaterial(int id)
        {
            try
            {
                _logger.LogInformation("Getting complete typed material: {MaterialId}", id);

                var material = await _materialService.GetCompleteMaterialAsync(id);

                if (material == null)
                {
                    _logger.LogWarning("Material not found: {MaterialId}", id);
                    return NotFound(new { Error = $"Material with ID {id} not found" });
                }

                _logger.LogInformation("Retrieved complete typed material: {MaterialId} (Type: {MaterialType})",
                    id, material.Type);
                return Ok(material);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete typed material: {MaterialId}", id);
                return StatusCode(500, new { Error = "Failed to retrieve material", Details = ex.Message });
            }
        }

       
        /// Get materials by type with complete details (bulk operation)
        
        [HttpGet("type/{materialType}/complete")]
        public async Task<ActionResult<object[]>> GetCompleteMaterialsByType(string materialType)
        {
            try
            {
                _logger.LogInformation("Getting complete materials by type: {MaterialType}", materialType);

                // Parse the material type - use MaterialType alias for your enum
                if (!Enum.TryParse<MaterialType>(materialType, true, out var type))
                {
                    return BadRequest(new { Error = $"Invalid material type: {materialType}" });
                }

                // Use the enum overload of GetMaterialsByTypeAsync
                var materials = await _materialService.GetMaterialsByTypeAsync(GetSystemTypeFromMaterialType(type));

                // Get complete details for each
                var completeMaterials = new List<object>();
                foreach (var material in materials)
                {
                    var completeDetails = await _materialService.GetCompleteMaterialDetailsAsync(material.id);
                    if (completeDetails != null)
                    {
                        completeMaterials.Add(completeDetails);
                    }
                }

                _logger.LogInformation("Retrieved {Count} complete materials of type: {MaterialType}",
                    completeMaterials.Count, materialType);

                return Ok(completeMaterials.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete materials by type: {MaterialType}", materialType);
                return StatusCode(500, new { Error = "Failed to retrieve materials", Details = ex.Message });
            }
        }

        [HttpGet("{id}/summary")]
        public async Task<ActionResult<object>> GetMaterialSummary(int id)
        {
            try
            {
                _logger.LogInformation("Getting material summary: {MaterialId}", id);
                
                // Use the service instead of direct DbContext access
                var material = await _materialService.GetMaterialAsync(id);
                if (material == null)
                {
                    return NotFound(new { Error = $"Material with ID {id} not found" });
                }

                var summary = new
                {
                    id = material.id,
                    Name = material.Name,
                    Description = material.Description,
                    Type = GetLowercaseType(material.Type),
                    Created_at = material.Created_at,
                    Updated_at = material.Updated_at,
                    // Note: Child entity counts would need separate service calls
                    // Or move this logic to the MaterialService
                };

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting material summary: {MaterialId}", id);
                return StatusCode(500, new { Error = "Failed to retrieve material summary", Details = ex.Message });
            }
        }
        // DEPRECATED: Use POST /materials instead
        // This endpoint has been replaced by the unified POST /materials endpoint which supports the same functionality

        [Obsolete("This endpoint is deprecated. Use POST /api/{tenantName}/materials instead.")]
        [HttpPost("advanced")]
        public async Task<ActionResult<CreateMaterialResponse>> PostMaterialAdvanced(string tenantName)
        {
            try
            {
                JsonElement materialData;
                var contentType = Request.ContentType?.ToLower() ?? "";

                _logger.LogInformation("Received material creation request with Content-Type: {ContentType}", contentType);

                // Handle different content types
                if (contentType.Contains("application/json"))
                {
                    // JSON request
                    using var reader = new StreamReader(Request.Body);
                    var body = await reader.ReadToEndAsync();
                    materialData = JsonSerializer.Deserialize<JsonElement>(body);
                    _logger.LogInformation("Parsed JSON request body");
                }
                else if (contentType.Contains("multipart/form-data") || contentType.Contains("application/x-www-form-urlencoded"))
                {
                    // Form data request - convert to JSON
                    var formDict = new Dictionary<string, object>();

                    foreach (var key in Request.Form.Keys)
                    {
                        var value = Request.Form[key].ToString();

                        // Try to parse numeric values
                        if (int.TryParse(value, out int intValue))
                        {
                            formDict[key] = intValue;
                        }
                        else if (bool.TryParse(value, out bool boolValue))
                        {
                            formDict[key] = boolValue;
                        }
                        else
                        {
                            formDict[key] = value;
                        }
                    }

                    // Convert to JSON
                    var jsonString = JsonSerializer.Serialize(formDict);
                    materialData = JsonSerializer.Deserialize<JsonElement>(jsonString);
                    _logger.LogInformation("Converted form data to JSON: {Json}", jsonString);
                }
                else
                {
                    _logger.LogWarning("Unsupported Content-Type: {ContentType}", contentType);
                    return StatusCode(415, $"Unsupported Media Type. Please use 'application/json', 'multipart/form-data', or 'application/x-www-form-urlencoded'. Received: {contentType}");
                }

                // Parse the incoming data to determine material type
                var material = ParseMaterialFromJson(materialData);

                if (material == null)
                {
                    return BadRequest("Invalid material data or unsupported material type");
                }

                _logger.LogInformation("Creating material {Name} (Type: {Type}) for tenant: {TenantName}",
                    material.Name, material.GetType().Name, tenantName);

                var createdMaterial = await _materialService.CreateMaterialAsync(material);

                _logger.LogInformation("Created material {Name} with ID {Id} for tenant: {TenantName}",
                    createdMaterial.Name, createdMaterial.id, tenantName);

                // Process related materials for subcomponents (annotations, timestamps, steps, entries, etc.)
                await ProcessSubcomponentRelatedMaterialsForUpdateAsync(createdMaterial, materialData);

                // Process related materials for the parent material if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, materialData);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = $"Material '{createdMaterial.Name}' created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_id = createdMaterial.Unique_id,
                    AssetId = createdMaterial switch
                    {
                        VideoMaterial v => v.AssetId,
                        ImageMaterial i => i.AssetId,
                        PDFMaterial p => p.AssetId,
                        UnityMaterial u => u.AssetId,
                        DefaultMaterial d => d.AssetId,
                        _ => null
                    },
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating material for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error creating material: {ex.Message}");
            }
        }
        // DEPRECATED: Use POST /materials instead
        // This endpoint has been replaced by the unified POST /materials endpoint which supports both material-only and material-with-asset scenarios

        [Obsolete("This endpoint is deprecated. Use POST /api/{tenantName}/materials instead, which supports optional file uploads via multipart/form-data.")]
        [HttpPost("detail-with-asset")]
        public async Task<ActionResult<CreateMaterialResponse>> PostMaterialDetailedWithAsset(
            string tenantName, [FromForm] FileUploadFormDataWithMaterial materialaAssetData)  // Optional file upload
        {
            try
            {
                // Parse the JSON material data
                JsonElement jsonMaterialData;
                try
                {
                    jsonMaterialData = JsonSerializer.Deserialize<JsonElement>(materialaAssetData.materialData);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON in materialData parameter");
                    return BadRequest("Invalid JSON format in materialData");
                }

                // Parse asset data if provided
                JsonElement? jsonAssetData = null;
                if (!string.IsNullOrEmpty(materialaAssetData.assetData))
                {
                    try
                    {
                        jsonAssetData = JsonSerializer.Deserialize<JsonElement>(materialaAssetData.assetData);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Invalid JSON in assetData parameter");
                        return BadRequest("Invalid JSON format in assetData");
                    }
                }

                // Parse the incoming JSON to determine material type
                var materialType = GetMaterialTypeFromJson(jsonMaterialData);
                
                _logger.LogInformation("Creating detailed material with asset support of type: {MaterialType} for tenant: {TenantName}", 
                    materialType, tenantName);

                // Check if we should create an asset
                bool shouldCreateAsset = ShouldCreateAsset(jsonMaterialData, materialType, materialaAssetData.File, jsonAssetData);
                
                if (shouldCreateAsset)
                {
                    _logger.LogInformation("Asset creation detected (file: {HasFile}, assetData: {HasAssetData})", 
                        materialaAssetData.File != null, jsonAssetData.HasValue);
                    return await CreateMaterialWithAsset(tenantName, jsonMaterialData, materialType, materialaAssetData.File, jsonAssetData);
                }

                // Fall back to creating material without asset (same as existing endpoint)
                return materialType.ToLower() switch
                {
                    "workflow" => await CreateWorkflowFromJson(tenantName, jsonMaterialData),
                    "video" => await CreateVideoFromJson(tenantName, jsonMaterialData),
                    "checklist" => await CreateChecklistFromJson(tenantName, jsonMaterialData),
                    "questionnaire" => await CreateQuestionnaireFromJson(tenantName, jsonMaterialData),
                    "quiz" => await CreateQuizFromJson(tenantName, jsonMaterialData),
                    _ => await CreateBasicMaterialFromJson(tenantName, jsonMaterialData)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating detailed material with asset for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error creating material: {ex.Message}");
            }
        }

        // NEW: Determine if we should create an asset
        private bool ShouldCreateAsset(JsonElement materialData, string materialType, IFormFile? assetFile, JsonElement? assetData)
        {
            // Only create assets for material types that support them
            var assetSupportingTypes = new[] { "video", "image", "pdf", "unitydemo", "default" };
            
            if (!assetSupportingTypes.Contains(materialType.ToLower()))
            {
                return false;
            }

            // Create asset if we have:
            // 1. A file upload, OR
            // 2. Explicit asset data, OR  
            // 3. Legacy asset reference in material data
            return assetFile != null || 
                assetData.HasValue || 
                CheckForAssetReference(materialData, materialType);
        }

        // NEW: Check if the material request includes asset reference data
        private bool CheckForAssetReference(JsonElement materialData, string materialType)
        {
            // Check for asset reference properties in the JSON
            return TryGetPropertyCaseInsensitive(materialData, "createAssetReference", out var _) ||
                (TryGetPropertyCaseInsensitive(materialData, "assetReference", out var assetElement) && 
                    assetElement.ValueKind == JsonValueKind.Object);
        }

        // NEW: Create material with associated asset (file upload or reference)
        private async Task<ActionResult<CreateMaterialResponse>> CreateMaterialWithAsset(
            string tenantName,
            JsonElement materialData,
            string materialType,
            IFormFile? assetFile,
            JsonElement? assetData)
        {
            try
            {
                _logger.LogInformation(" Creating material with asset for type: {MaterialType}", materialType);

                // Create the asset first
                Asset createdAsset;
                try
                {
                    if (assetFile != null)
                    {
                        // File upload scenario - use assetData for metadata if provided
                        createdAsset = await CreateAssetFromFile(tenantName, assetFile, assetData);
                        _logger.LogInformation("Created asset from file upload {AssetId} ({Filename})", 
                            createdAsset.Id, createdAsset.Filename);
                    }
                    else if (assetData.HasValue)
                    {
                        // Asset reference scenario using dedicated assetData
                        var assetRefData = ExtractAssetReferenceFromAssetData(assetData.Value);
                        if (assetRefData == null)
                        {
                            return BadRequest("Asset data is invalid or missing required fields");
                        }
                        
                        createdAsset = await _assetService.CreateAssetReference(tenantName, assetRefData);
                        _logger.LogInformation("Created asset reference from assetData {AssetId} ({Filename})", 
                            createdAsset.Id, createdAsset.Filename);
                    }
                    else
                    {
                        // Fallback to legacy material data extraction
                        var assetRefData = ExtractAssetReferenceData(materialData);
                        if (assetRefData == null)
                        {
                            return BadRequest("Asset reference data is invalid or missing required fields");
                        }
                        
                        createdAsset = await _assetService.CreateAssetReference(tenantName, assetRefData);
                        _logger.LogInformation("Created asset reference from materialData {AssetId} ({Filename})", 
                            createdAsset.Id, createdAsset.Filename);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create asset during material creation");
                    return StatusCode(500, $"Failed to create asset: {ex.Message}");
                }

                // Create the material with the asset ID
                Material material;
                try
                {
                    material = CreateMaterialWithAssetId(materialData, materialType, createdAsset.Id);
                    var createdMaterial = await _materialService.CreateMaterialAsyncComplete(material);

                    _logger.LogInformation("Created material {MaterialId} ({Name}) with asset {AssetId}",
                        createdMaterial.id, createdMaterial.Name, createdAsset.Id);

                    // Process related materials for subcomponents (timestamps, steps, entries, etc.)
                    await ProcessSubcomponentRelatedMaterialsForUpdateAsync(createdMaterial, materialData);

                    // Process related materials for the parent material if provided
                    await ProcessRelatedMaterialsAsync(createdMaterial.id, materialData);

                    var response = new CreateMaterialResponse
                    {
                        Status = "success",
                        Message = "Material with asset created successfully",
                        id = createdMaterial.id,
                        Name = createdMaterial.Name,
                        Description = createdMaterial.Description,
                        Type = GetLowercaseType(createdMaterial.Type),
                        Unique_id = createdMaterial.Unique_id,
                        AssetId = createdAsset.Id,
                        Created_at = createdMaterial.Created_at
                    };

                    return CreatedAtAction(nameof(GetMaterial),
                        new { tenantName, id = createdMaterial.id },
                        response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create material, will attempt to clean up asset {AssetId}", createdAsset.Id);
                    
                    // Attempt cleanup of created asset
                    try
                    {
                        await _assetService.DeleteAssetAsync(tenantName, createdAsset.Id);
                        _logger.LogInformation("Cleaned up asset {AssetId} after material creation failure", createdAsset.Id);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to clean up asset {AssetId} after material creation failure", createdAsset.Id);
                    }
                    
                    return StatusCode(500, $"Failed to create material: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error in CreateMaterialWithAsset");
                return StatusCode(500, $"Error creating material with asset: {ex.Message}");
            }
        }

        // NEW: Create asset from uploaded file with metadata from assetData
        private async Task<Asset> CreateAssetFromFile(string tenantName, IFormFile assetFile, JsonElement? assetData)
        {
            // Extract asset metadata from assetData JSON (preferred) or fallback defaults
            string? description = null;
            string? customFilename = null;
            string? filetype = null;
            
            if (assetData.HasValue)
            {
                if (TryGetPropertyCaseInsensitive(assetData.Value, "description", out var descProp))
                    description = descProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(assetData.Value, "filename", out var filenameProp))
                    customFilename = filenameProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(assetData.Value, "filetype", out var filetypeProp))
                    filetype = filetypeProp.GetString();
            }

            // Use custom filename or generate one
            var filename = customFilename ?? assetFile.FileName ?? Guid.NewGuid().ToString();
            
            // Create asset using the existing asset service
            return await _assetService.UploadAssetAsync(assetFile, tenantName, filename, description);
        }

        // NEW: Extract asset reference data from dedicated assetData JSON
        private AssetReferenceData? ExtractAssetReferenceFromAssetData(JsonElement assetData)
        {
            var assetRefData = new AssetReferenceData();
            
            if (TryGetPropertyCaseInsensitive(assetData, "filename", out var filenameProp))
                assetRefData.Filename = filenameProp.GetString();
            
            if (TryGetPropertyCaseInsensitive(assetData, "description", out var descProp))
                assetRefData.Description = descProp.GetString();
            
            if (TryGetPropertyCaseInsensitive(assetData, "filetype", out var typeProp))
                assetRefData.Filetype = typeProp.GetString();
            
            if (TryGetPropertyCaseInsensitive(assetData, "src", out var srcProp))
                assetRefData.Src = srcProp.GetString();
            
            if (TryGetPropertyCaseInsensitive(assetData, "url", out var urlProp))
                assetRefData.URL = urlProp.GetString();

            // Validate required fields - need either filename or src/url
            if (string.IsNullOrEmpty(assetRefData.Filename) && 
                string.IsNullOrEmpty(assetRefData.Src) && 
                string.IsNullOrEmpty(assetRefData.URL))
            {
                _logger.LogWarning("AssetData missing required filename, src, or url");
                return null;
            }

            return assetRefData;
        }

        // NEW: Extract asset reference data from the material JSON
        private AssetReferenceData? ExtractAssetReferenceData(JsonElement materialData)
        {
            AssetReferenceData? assetRefData = null;

            // Check for inline asset reference object
            if (TryGetPropertyCaseInsensitive(materialData, "assetReference", out var assetElement) && 
                assetElement.ValueKind == JsonValueKind.Object)
            {
                assetRefData = new AssetReferenceData();
                
                if (TryGetPropertyCaseInsensitive(assetElement, "filename", out var filenameProp))
                    assetRefData.Filename = filenameProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(assetElement, "description", out var descProp))
                    assetRefData.Description = descProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(assetElement, "filetype", out var typeProp))
                    assetRefData.Filetype = typeProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(assetElement, "src", out var srcProp))
                    assetRefData.Src = srcProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(assetElement, "url", out var urlProp))
                    assetRefData.URL = urlProp.GetString();
            }
            
            // Check for direct asset reference properties at material level
            else if (TryGetPropertyCaseInsensitive(materialData, "createAssetReference", out var createAssetProp) && 
                    createAssetProp.GetBoolean())
            {
                assetRefData = new AssetReferenceData();
                
                if (TryGetPropertyCaseInsensitive(materialData, "assetFilename", out var filenameProp))
                    assetRefData.Filename = filenameProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(materialData, "assetDescription", out var descProp))
                    assetRefData.Description = descProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(materialData, "assetFiletype", out var typeProp))
                    assetRefData.Filetype = typeProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(materialData, "assetSrc", out var srcProp))
                    assetRefData.Src = srcProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(materialData, "assetUrl", out var urlProp))
                    assetRefData.URL = urlProp.GetString();
            }

            // Validate required fields - need either filename or src/url
            if (assetRefData != null && 
                string.IsNullOrEmpty(assetRefData.Filename) && 
                string.IsNullOrEmpty(assetRefData.Src) && 
                string.IsNullOrEmpty(assetRefData.URL))
            {
                _logger.LogWarning("Asset reference data missing required filename, src, or url");
                return null;
            }

            return assetRefData;
        }

        // NEW: Create asset reference record (no actual file upload)
        
        // NEW: Create material instance with asset ID set
        private Material CreateMaterialWithAssetId(JsonElement materialData, string materialType, int assetId)
        {
            // Parse the basic material first
            var material = ParseMaterialFromJson(materialData);
            if (material == null)
            {
                throw new ArgumentException("Failed to parse material from JSON");
            }

            // Set the asset ID for asset-supporting materials
            switch (material)
            {
                case VideoMaterial video:
                    video.AssetId = assetId;
                    break;
                case ImageMaterial image:
                    image.AssetId = assetId;
                    break;
                case PDFMaterial pdf:
                    pdf.AssetId = assetId;
                    break;
                case UnityMaterial unity:
                    unity.AssetId = assetId;
                    break;
                case DefaultMaterial defaultMat:
                    defaultMat.AssetId = assetId;
                    break;
                default:
                    _logger.LogWarning("Material type {MaterialType} does not support assets, ignoring asset assignment", 
                        material.GetType().Name);
                    break;
            }

            return material;
        }

        // Helper to get AssetId from a material (returns null if material type doesn't support assets)
        private int? GetAssetIdFromMaterial(Material material)
        {
            return material switch
            {
                VideoMaterial video => video.AssetId,
                ImageMaterial image => image.AssetId,
                PDFMaterial pdf => pdf.AssetId,
                UnityMaterial unity => unity.AssetId,
                DefaultMaterial defaultMat => defaultMat.AssetId,
                _ => null
            };
        }

        // Helper to set AssetId on a material
        private void SetAssetIdOnMaterial(Material material, int assetId)
        {
            switch (material)
            {
                case VideoMaterial video:
                    video.AssetId = assetId;
                    break;
                case ImageMaterial image:
                    image.AssetId = assetId;
                    break;
                case PDFMaterial pdf:
                    pdf.AssetId = assetId;
                    break;
                case UnityMaterial unity:
                    unity.AssetId = assetId;
                    break;
                case DefaultMaterial defaultMat:
                    defaultMat.AssetId = assetId;
                    break;
                default:
                    _logger.LogWarning("Material type {MaterialType} does not support assets, ignoring asset assignment",
                        material.GetType().Name);
                    break;
            }
        }

        // NEW: Helper to extract file type from filename or URL
        private string GetFiletypeFromFilename(string? filenameOrUrl)
        {
            if (string.IsNullOrEmpty(filenameOrUrl))
                return "unknown";
            
            // Extract filename from URL if needed
            var filename = filenameOrUrl;
            if (Uri.TryCreate(filenameOrUrl, UriKind.Absolute, out var uri))
            {
                filename = Path.GetFileName(uri.LocalPath);
            }
            
            var extension = Path.GetExtension(filename)?.ToLowerInvariant();
            return extension?.TrimStart('.') ?? "unknown";
        }
        /// <summary>
        /// Create a new material with optional file upload.
        /// </summary>
        /// <remarks>
        /// The 'material' field should contain a JSON string with the material data:
        /// ```json
        /// {
        ///   "name": "My Material",
        ///   "description": "Description here",
        ///   "type": "Chatbot",
        ///   "chatbotConfig": "https://api.example.com",
        ///   "chatbotModel": "default",
        ///   "chatbotPrompt": "You are a helpful assistant."
        /// }
        /// ```
        /// </remarks>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="formData">Form data containing material JSON, optional asset data, and optional file</param>
        /// <returns>The created material</returns>
        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<CreateMaterialResponse>> PostMaterialDetailed(
            string tenantName,
            [FromForm] MaterialCreateFormData formData)
        {
            try
            {
                JsonElement materialData;
                IFormFile? file = formData.file;
                JsonElement? assetData = null;

                _logger.LogInformation("Received material creation request via form-data");

                // Parse the material JSON
                if (string.IsNullOrEmpty(formData.material))
                {
                    return BadRequest("material is required");
                }

                try
                {
                    materialData = JsonSerializer.Deserialize<JsonElement>(formData.material);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON in material parameter");
                    return BadRequest("Invalid JSON format in material");
                }

                // Extract optional assetData
                if (!string.IsNullOrEmpty(formData.assetData))
                {
                    try
                    {
                        assetData = JsonSerializer.Deserialize<JsonElement>(formData.assetData);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Invalid JSON in assetData parameter");
                        return BadRequest("Invalid JSON format in assetData");
                    }
                }

                _logger.LogInformation("Parsed form-data request (file: {HasFile})", file != null);

                // Parse the incoming JSON to determine material type
                var materialType = GetMaterialTypeFromJson(materialData);

                _logger.LogInformation("Creating material of type: {MaterialType} for tenant: {TenantName}",
                    materialType, tenantName);

                // Check if we should create an asset (file provided + material type supports assets)
                bool shouldCreateAsset = ShouldCreateAsset(materialData, materialType, file, assetData);

                if (shouldCreateAsset)
                {
                    _logger.LogInformation("Asset creation detected (file: {HasFile}, assetData: {HasAssetData})",
                        file != null, assetData.HasValue);
                    return await CreateMaterialWithAsset(tenantName, materialData, materialType, file, assetData);
                }

                // Material-only creation (no asset)
                return materialType.ToLower() switch
                {
                    "workflow" => await CreateWorkflowFromJson(tenantName, materialData),
                    "video" => await CreateVideoFromJson(tenantName, materialData),
                    "checklist" => await CreateChecklistFromJson(tenantName, materialData),
                    "questionnaire" => await CreateQuestionnaireFromJson(tenantName, materialData),
                    "quiz" => await CreateQuizFromJson(tenantName, materialData),
                    "voice" => await CreateVoiceFromJson(tenantName, materialData),
                    _ => await CreateBasicMaterialFromJson(tenantName, materialData)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating material for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error creating material: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a new material using JSON body (no file upload).
        /// </summary>
        /// <remarks>
        /// Use this endpoint for creating materials without file uploads.
        /// For materials that require file uploads, use the multipart/form-data endpoint instead.
        ///
        /// Example request body for a Chatbot material:
        /// ```json
        /// {
        ///   "name": "My Chatbot",
        ///   "description": "A helpful assistant",
        ///   "type": "Chatbot",
        ///   "chatbotConfig": "https://api.example.com",
        ///   "chatbotModel": "default",
        ///   "chatbotPrompt": "You are a helpful assistant."
        /// }
        /// ```
        /// </remarks>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="materialData">The material data as JSON</param>
        /// <returns>The created material</returns>
        [HttpPost("json")]
        [Consumes("application/json")]
        public async Task<ActionResult<CreateMaterialResponse>> PostMaterialJson(
            string tenantName,
            [FromBody] JsonElement materialData)
        {
            try
            {
                _logger.LogInformation("Received material creation request via JSON body");

                // Parse the incoming JSON to determine material type
                var materialType = GetMaterialTypeFromJson(materialData);

                _logger.LogInformation("Creating material of type: {MaterialType} for tenant: {TenantName}",
                    materialType, tenantName);

                // Material-only creation (no asset)
                return materialType.ToLower() switch
                {
                    "workflow" => await CreateWorkflowFromJson(tenantName, materialData),
                    "video" => await CreateVideoFromJson(tenantName, materialData),
                    "checklist" => await CreateChecklistFromJson(tenantName, materialData),
                    "questionnaire" => await CreateQuestionnaireFromJson(tenantName, materialData),
                    "quiz" => await CreateQuizFromJson(tenantName, materialData),
                    "voice" => await CreateVoiceFromJson(tenantName, materialData),
                    _ => await CreateBasicMaterialFromJson(tenantName, materialData)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating material for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error creating material: {ex.Message}");
            }
        }

        private string GetMaterialTypeFromJson(JsonElement jsonElement)
        {
            // Try different variations of type property names
            if (TryGetPropertyCaseInsensitive(jsonElement, "discriminator", out var discProp))
            {
                var discriminator = discProp.GetString();
                return discriminator?.Replace("Material", "").ToLower() ?? "default";
            }

            if (TryGetPropertyCaseInsensitive(jsonElement, "type", out var typeProp))
            {
                return typeProp.GetString()?.ToLower() ?? "default";
            }

            return "default";
        }

        // Helper method to convert Type enum to lowercase string (matching EnumMember values)
        private string GetLowercaseType(XR50TrainingAssetRepo.Models.Type type)
        {
            return type switch
            {
                XR50TrainingAssetRepo.Models.Type.Image => "image",
                XR50TrainingAssetRepo.Models.Type.Video => "video",
                XR50TrainingAssetRepo.Models.Type.PDF => "pdf",
                XR50TrainingAssetRepo.Models.Type.Unity => "unity",
                XR50TrainingAssetRepo.Models.Type.Chatbot => "chatbot",
                XR50TrainingAssetRepo.Models.Type.Questionnaire => "questionnaire",
                XR50TrainingAssetRepo.Models.Type.Checklist => "checklist",
                XR50TrainingAssetRepo.Models.Type.Workflow => "workflow",
                XR50TrainingAssetRepo.Models.Type.MQTT_Template => "mqtt_template",
                XR50TrainingAssetRepo.Models.Type.Answers => "answers",
                XR50TrainingAssetRepo.Models.Type.Quiz => "quiz",
                XR50TrainingAssetRepo.Models.Type.Default => "default",
                _ => "default"
            };
        }

        private async Task<ActionResult<CreateMaterialResponse>> CreateWorkflowFromJson(string tenantName, JsonElement jsonElement)
        {
            try
            {
                _logger.LogInformation(" Creating workflow material from JSON");

                // Parse the workflow material properties
                var workflow = new WorkflowMaterial();

                if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
                    workflow.Name = nameProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
                    workflow.Description = descProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "unique_id", out var uniqueIdProp) && uniqueIdProp.ValueKind == JsonValueKind.Number)
                    workflow.Unique_id = uniqueIdProp.GetInt32();

                // Parse the steps - try to get from "config" object first, then direct "steps" array
                var steps = new List<WorkflowStep>();
                var stepRelatedMaterials = new Dictionary<int, List<int>>();

                JsonElement stepsElement = default;
                bool hasSteps = false;

                if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configElement))
                {
                    if (TryGetPropertyCaseInsensitive(configElement, "steps", out var configStepsElement))
                    {
                        stepsElement = configStepsElement;
                        hasSteps = true;
                        _logger.LogInformation("Found steps in config object");
                    }
                }

                if (!hasSteps && TryGetPropertyCaseInsensitive(jsonElement, "steps", out var directStepsElement))
                {
                    stepsElement = directStepsElement;
                    hasSteps = true;
                    _logger.LogInformation("Found steps directly");
                }

                if (hasSteps && stepsElement.ValueKind == JsonValueKind.Array)
                {
                    int stepIndex = 0;
                    foreach (var stepElement in stepsElement.EnumerateArray())
                    {
                        var step = new WorkflowStep();

                        if (TryGetPropertyCaseInsensitive(stepElement, "title", out var titleProp))
                            step.Title = titleProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(stepElement, "content", out var contentProp))
                            step.Content = contentProp.GetString();

                        steps.Add(step);

                        // Parse related materials for this step
                        var relatedIds = ParseRelatedMaterialIds(stepElement);
                        if (relatedIds.Any())
                        {
                            stepRelatedMaterials[stepIndex] = relatedIds;
                        }

                        stepIndex++;
                    }
                }

                _logger.LogInformation("Parsed workflow: {Name} with {StepCount} steps", workflow.Name, steps.Count);

                // Use the service method directly instead of the controller method
                var createdMaterial = await _materialService.CreateWorkflowWithStepsAsync(workflow, steps);

                _logger.LogInformation("Created workflow material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

                // Process related materials for steps
                await ProcessSubcomponentRelatedMaterialsAsync(
                    "WorkflowStep",
                    steps,
                    stepRelatedMaterials,
                    s => s.Id);

                // Process related materials if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = "Workflow material created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_id = createdMaterial.Unique_id,
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating workflow from JSON");
                throw;
            }
        }

        private async Task<ActionResult<CreateMaterialResponse>> CreateVideoFromJson(string tenantName, JsonElement jsonElement)
        {
            try
            {
                _logger.LogInformation("🎥 Creating video material from JSON");
                
                // Parse the video material properties
                var video = new VideoMaterial();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
                    video.Name = nameProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
                    video.Description = descProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "unique_id", out var uniqueIdProp) && uniqueIdProp.ValueKind == JsonValueKind.Number)
                    video.Unique_id = uniqueIdProp.GetInt32();

                if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var assetIdProp))
                    video.AssetId = assetIdProp.GetInt32();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "videoPath", out var pathProp))
                    video.VideoPath = pathProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "videoDuration", out var durationProp))
                    video.VideoDuration = durationProp.GetInt32();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "videoResolution", out var resolutionProp))
                    video.VideoResolution = resolutionProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "startTime", out var startTimeProp))
                    video.startTime = startTimeProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "annotations", out var annotationsProp))
                    video.Annotations = annotationsProp.GetRawText();

                // Parse the timestamps - check both root level and inside config object
                var timestamps = new List<VideoTimestamp>();
                var timestampRelatedMaterials = new Dictionary<int, List<int>>();
                JsonElement? timestampsElement = null;

                // First try root level
                if (TryGetPropertyCaseInsensitive(jsonElement, "timestamps", out var rootTimestamps) &&
                    rootTimestamps.ValueKind == JsonValueKind.Array)
                {
                    timestampsElement = rootTimestamps;
                }
                // Then try inside config object
                else if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configElement) &&
                         TryGetPropertyCaseInsensitive(configElement, "timestamps", out var configTimestamps) &&
                         configTimestamps.ValueKind == JsonValueKind.Array)
                {
                    timestampsElement = configTimestamps;
                }

                if (timestampsElement.HasValue)
                {
                    int timestampIndex = 0;
                    foreach (var timestampElement in timestampsElement.Value.EnumerateArray())
                    {
                        var timestamp = new VideoTimestamp();

                        if (TryGetPropertyCaseInsensitive(timestampElement, "title", out var titleProp))
                            timestamp.Title = titleProp.GetString() ?? "";

                        int? startTimeInt = null;
                        if (TryGetPropertyCaseInsensitive(timestampElement, "startTime", out var startTimePropTs))
                        {
                            if (startTimePropTs.ValueKind == JsonValueKind.Number)
                            {
                                startTimeInt = startTimePropTs.GetInt32();
                                timestamp.startTime = startTimeInt.ToString()!;
                            }
                            else
                            {
                                timestamp.startTime = startTimePropTs.GetString() ?? "";
                                int.TryParse(timestamp.startTime, out var parsed);
                                startTimeInt = parsed;
                            }
                        }

                        // Parse duration
                        int? tsDuration = null;
                        if (TryGetPropertyCaseInsensitive(timestampElement, "duration", out var tsDurationProp))
                        {
                            if (tsDurationProp.ValueKind == JsonValueKind.Number)
                                tsDuration = tsDurationProp.GetInt32();
                            else if (int.TryParse(tsDurationProp.GetString(), out var parsedDuration))
                                tsDuration = parsedDuration;
                            timestamp.Duration = tsDuration;
                        }

                        // Parse endTime, or calculate from startTime + duration
                        if (TryGetPropertyCaseInsensitive(timestampElement, "endTime", out var endTimeProp))
                        {
                            timestamp.endTime = endTimeProp.ValueKind == JsonValueKind.Number
                                ? endTimeProp.GetInt32().ToString()
                                : endTimeProp.GetString() ?? "";
                        }
                        else if (startTimeInt.HasValue && tsDuration.HasValue)
                        {
                            // Calculate endTime from startTime + duration
                            timestamp.endTime = (startTimeInt.Value + tsDuration.Value).ToString();
                        }

                        if (TryGetPropertyCaseInsensitive(timestampElement, "description", out var descriptionProp))
                            timestamp.Description = descriptionProp.GetString();

                        if (TryGetPropertyCaseInsensitive(timestampElement, "type", out var typeProp))
                            timestamp.Type = typeProp.GetString();

                        timestamps.Add(timestamp);

                        // Parse related materials for this timestamp
                        var relatedIds = ParseRelatedMaterialIds(timestampElement);
                        _logger.LogInformation("Timestamp {Index} '{Title}': found {Count} related material IDs: [{Ids}]",
                            timestampIndex, timestamp.Title, relatedIds.Count, string.Join(", ", relatedIds));
                        if (relatedIds.Any())
                        {
                            timestampRelatedMaterials[timestampIndex] = relatedIds;
                        }

                        timestampIndex++;
                    }
                }

                _logger.LogInformation("🎬 Parsed video: {Name} with {TimestampCount} timestamps, {RelatedCount} timestamps have related materials",
                    video.Name, timestamps.Count, timestampRelatedMaterials.Count);

                // Use the service method directly instead of the controller method
                var createdMaterial = await _materialService.CreateVideoWithTimestampsAsync(video, timestamps);

                _logger.LogInformation("Created video material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

                // Log timestamp IDs after creation
                foreach (var ts in timestamps)
                {
                    _logger.LogInformation("Timestamp '{Title}' has id: {Id}", ts.Title, ts.id);
                }

                // Process related materials for timestamps
                _logger.LogInformation("Processing related materials for {Count} timestamps", timestampRelatedMaterials.Count);
                await ProcessSubcomponentRelatedMaterialsAsync(
                    "VideoTimestamp",
                    timestamps,
                    timestampRelatedMaterials,
                    t => t.id);

                // Process related materials if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = "Video material created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_id = createdMaterial.Unique_id,
                    AssetId = video.AssetId,
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating video from JSON");
                throw;
            }
        }

        private async Task<ActionResult<CreateMaterialResponse>> CreateChecklistFromJson(string tenantName, JsonElement jsonElement)
        {
            try
            {
                _logger.LogInformation("Creating checklist material from JSON");
                
                // Parse the checklist material properties
                var checklist = new ChecklistMaterial();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
                    checklist.Name = nameProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
                    checklist.Description = descProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "unique_id", out var uniqueIdProp) && uniqueIdProp.ValueKind == JsonValueKind.Number)
                    checklist.Unique_id = uniqueIdProp.GetInt32();

                // Parse the entries - try to get from "config" object first, then direct "entries" array
                var entries = new List<ChecklistEntry>();
                var entryRelatedMaterials = new Dictionary<int, List<int>>();

                JsonElement entriesElement = default;
                bool hasEntries = false;

                if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configElement))
                {
                    if (TryGetPropertyCaseInsensitive(configElement, "entries", out var configEntriesElement))
                    {
                        entriesElement = configEntriesElement;
                        hasEntries = true;
                        _logger.LogInformation("Found entries in config object");
                    }
                }

                if (!hasEntries && TryGetPropertyCaseInsensitive(jsonElement, "entries", out var directEntriesElement))
                {
                    entriesElement = directEntriesElement;
                    hasEntries = true;
                    _logger.LogInformation("Found entries directly");
                }

                if (hasEntries && entriesElement.ValueKind == JsonValueKind.Array)
                {
                    int entryIndex = 0;
                    foreach (var entryElement in entriesElement.EnumerateArray())
                    {
                        var entry = new ChecklistEntry();

                        if (TryGetPropertyCaseInsensitive(entryElement, "text", out var textProp))
                            entry.Text = textProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(entryElement, "description", out var descriptionProp))
                            entry.Description = descriptionProp.GetString();

                        entries.Add(entry);

                        // Parse related materials for this entry
                        var relatedIds = ParseRelatedMaterialIds(entryElement);
                        _logger.LogInformation("Entry {Index} '{Text}': found {Count} related material IDs: [{Ids}]",
                            entryIndex, entry.Text, relatedIds.Count, string.Join(", ", relatedIds));
                        if (relatedIds.Any())
                        {
                            entryRelatedMaterials[entryIndex] = relatedIds;
                        }

                        entryIndex++;
                    }
                }

                _logger.LogInformation("Parsed checklist: {Name} with {EntryCount} entries, {RelatedCount} entries have related materials",
                    checklist.Name, entries.Count, entryRelatedMaterials.Count);

                // Use the service method directly instead of the controller method
                var createdMaterial = await _materialService.CreateChecklistWithEntriesAsync(checklist, entries);

                _logger.LogInformation("Created checklist material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

                // Log entry IDs after creation
                foreach (var entry in entries)
                {
                    _logger.LogInformation("Entry '{Text}' has ChecklistEntryId: {Id}", entry.Text, entry.ChecklistEntryId);
                }

                // Process related materials for entries
                _logger.LogInformation("Processing related materials for {Count} entries", entryRelatedMaterials.Count);
                await ProcessSubcomponentRelatedMaterialsAsync(
                    "ChecklistEntry",
                    entries,
                    entryRelatedMaterials,
                    e => e.ChecklistEntryId);

                // Process related materials if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = "Checklist material created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_id = createdMaterial.Unique_id,
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating checklist from JSON");
                throw;
            }
        }

        private async Task<ActionResult<CreateMaterialResponse>> CreateQuestionnaireFromJson(string tenantName, JsonElement jsonElement)
        {
            try
            {
                _logger.LogInformation("❓ Creating questionnaire material from JSON");
                
                // Parse the questionnaire material properties
                var questionnaire = new QuestionnaireMaterial();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
                    questionnaire.Name = nameProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
                    questionnaire.Description = descProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "unique_id", out var uniqueIdProp) && uniqueIdProp.ValueKind == JsonValueKind.Number)
                    questionnaire.Unique_id = uniqueIdProp.GetInt32();

                if (TryGetPropertyCaseInsensitive(jsonElement, "questionnaireType", out var typeProp))
                    questionnaire.QuestionnaireType = typeProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "passingScore", out var scoreProp))
                    questionnaire.PassingScore = scoreProp.GetDecimal();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "questionnaireConfig", out var configProp))
                    questionnaire.QuestionnaireConfig = configProp.GetString();

                // Parse the entries - try to get from "config" object first, then direct "entries" array
                var entries = new List<QuestionnaireEntry>();
                var entryRelatedMaterials = new Dictionary<int, List<int>>();

                JsonElement entriesElement = default;
                bool hasEntries = false;

                if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configElement))
                {
                    if (TryGetPropertyCaseInsensitive(configElement, "entries", out var configEntriesElement))
                    {
                        entriesElement = configEntriesElement;
                        hasEntries = true;
                        _logger.LogInformation("Found entries in config object");
                    }
                }

                if (!hasEntries && TryGetPropertyCaseInsensitive(jsonElement, "entries", out var directEntriesElement))
                {
                    entriesElement = directEntriesElement;
                    hasEntries = true;
                    _logger.LogInformation("Found entries directly");
                }

                if (hasEntries && entriesElement.ValueKind == JsonValueKind.Array)
                {
                    int entryIndex = 0;
                    foreach (var entryElement in entriesElement.EnumerateArray())
                    {
                        var entry = new QuestionnaireEntry();

                        if (TryGetPropertyCaseInsensitive(entryElement, "text", out var textProp))
                            entry.Text = textProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(entryElement, "description", out var descriptionProp))
                            entry.Description = descriptionProp.GetString();

                        entries.Add(entry);

                        // Parse related materials for this entry
                        var relatedIds = ParseRelatedMaterialIds(entryElement);
                        if (relatedIds.Any())
                        {
                            entryRelatedMaterials[entryIndex] = relatedIds;
                        }

                        entryIndex++;
                    }
                }

                _logger.LogInformation("Parsed questionnaire: {Name} with {EntryCount} entries", questionnaire.Name, entries.Count);

                // For questionnaires, we can use the existing service method directly
                var createdMaterial = await _materialService.CreateQuestionnaireMaterialWithEntriesAsync(questionnaire, entries);

                _logger.LogInformation("Created questionnaire material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

                // Process related materials for entries
                await ProcessSubcomponentRelatedMaterialsAsync(
                    "QuestionnaireEntry",
                    entries,
                    entryRelatedMaterials,
                    e => e.QuestionnaireEntryId);

                // Process related materials if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = "Questionnaire material created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_id = createdMaterial.Unique_id,
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating questionnaire from JSON");
                throw;
            }
        }

        private async Task<ActionResult<CreateMaterialResponse>> CreateQuizFromJson(string tenantName, JsonElement jsonElement)
        {
            try
            {
                _logger.LogInformation("Creating quiz material from JSON");

                // Parse the quiz material properties
                var quiz = new QuizMaterial();

                if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
                    quiz.Name = nameProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
                    quiz.Description = descProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "unique_id", out var uniqueIdProp) && uniqueIdProp.ValueKind == JsonValueKind.Number)
                    quiz.Unique_id = uniqueIdProp.GetInt32();

                // Parse the questions - try to get from "config" object first, then direct "questions" array
                var questions = new List<QuizQuestion>();
                var questionRelatedMaterials = new Dictionary<int, List<int>>();
                // Track answer related materials: key is (questionIndex, answerIndex), value is list of material IDs
                var answerRelatedMaterials = new Dictionary<(int questionIndex, int answerIndex), List<int>>();

                JsonElement questionsElement = default;
                bool hasQuestions = false;

                if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configElement))
                {
                    if (TryGetPropertyCaseInsensitive(configElement, "questions", out var configQuestionsElement))
                    {
                        questionsElement = configQuestionsElement;
                        hasQuestions = true;
                        _logger.LogInformation("Found questions in config object");
                    }
                }

                if (!hasQuestions && TryGetPropertyCaseInsensitive(jsonElement, "questions", out var directQuestionsElement))
                {
                    questionsElement = directQuestionsElement;
                    hasQuestions = true;
                    _logger.LogInformation("Found questions directly");
                }

                if (hasQuestions && questionsElement.ValueKind == JsonValueKind.Array)
                {
                    _logger.LogInformation("Questions array has {Count} elements", questionsElement.GetArrayLength());

                    int questionIndex = 0;
                    foreach (var questionElement in questionsElement.EnumerateArray())
                    {
                        var question = new QuizQuestion();

                        if (TryGetPropertyCaseInsensitive(questionElement, "id", out var idProp))
                            question.QuestionNumber = idProp.GetInt32();

                        // Accept both "type" and "questionType" for flexibility
                        if (TryGetPropertyCaseInsensitive(questionElement, "questionType", out var questionTypeProp))
                            question.QuestionType = NormalizeQuestionType(questionTypeProp.GetString());
                        else if (TryGetPropertyCaseInsensitive(questionElement, "type", out var typeProp))
                            question.QuestionType = NormalizeQuestionType(typeProp.GetString());

                        if (TryGetPropertyCaseInsensitive(questionElement, "text", out var textProp))
                            question.Text = textProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(questionElement, "description", out var descriptionProp))
                            question.Description = descriptionProp.GetString();

                        if (TryGetPropertyCaseInsensitive(questionElement, "score", out var scoreProp))
                        {
                            if (scoreProp.ValueKind == JsonValueKind.String)
                            {
                                if (decimal.TryParse(scoreProp.GetString(), out var scoreValue))
                                    question.Score = scoreValue;
                            }
                            else if (scoreProp.ValueKind == JsonValueKind.Number)
                            {
                                question.Score = scoreProp.GetDecimal();
                            }
                        }

                        if (TryGetPropertyCaseInsensitive(questionElement, "helpText", out var helpProp))
                            question.HelpText = helpProp.GetString();

                        if (TryGetPropertyCaseInsensitive(questionElement, "allowMultiple", out var multiProp))
                            question.AllowMultiple = multiProp.GetBoolean();

                        if (TryGetPropertyCaseInsensitive(questionElement, "scaleConfig", out var scaleProp))
                            question.ScaleConfig = scaleProp.GetString();

                        // Handle answers
                        if (TryGetPropertyCaseInsensitive(questionElement, "answers", out var answersElement))
                        {
                            if (answersElement.ValueKind == JsonValueKind.Array)
                            {
                                var answers = new List<QuizAnswer>();
                                int answerIndex = 0;
                                foreach (var answerElement in answersElement.EnumerateArray())
                                {
                                    var answer = new QuizAnswer();

                                    if (TryGetPropertyCaseInsensitive(answerElement, "text", out var ansTextProp))
                                        answer.Text = ansTextProp.GetString() ?? "";

                                    // Support both "correctAnswer" and legacy "isCorrect" property names
                                    if (TryGetPropertyCaseInsensitive(answerElement, "correctAnswer", out var correctProp) ||
                                        TryGetPropertyCaseInsensitive(answerElement, "isCorrect", out correctProp))
                                    {
                                        if (correctProp.ValueKind == JsonValueKind.True)
                                            answer.CorrectAnswer = true;
                                        else if (correctProp.ValueKind == JsonValueKind.String)
                                            answer.CorrectAnswer = bool.TryParse(correctProp.GetString(), out var b) && b;
                                        else if (correctProp.ValueKind == JsonValueKind.False)
                                            answer.CorrectAnswer = false;
                                    }

                                    if (TryGetPropertyCaseInsensitive(answerElement, "displayOrder", out var orderProp))
                                        answer.DisplayOrder = orderProp.GetInt32();

                                    if (TryGetPropertyCaseInsensitive(answerElement, "extra", out var extraProp))
                                        answer.Extra = extraProp.GetString();

                                    answers.Add(answer);

                                    // Parse related materials for this answer
                                    var answerRelatedIds = ParseRelatedMaterialIds(answerElement);
                                    if (answerRelatedIds.Any())
                                    {
                                        answerRelatedMaterials[(questionIndex, answerIndex)] = answerRelatedIds;
                                    }

                                    answerIndex++;
                                }
                                question.Answers = answers;
                                _logger.LogInformation("Added {Count} answers to question", answers.Count);
                            }
                        }

                        questions.Add(question);

                        // Parse related materials for this question
                        var relatedIds = ParseRelatedMaterialIds(questionElement);
                        if (relatedIds.Any())
                        {
                            questionRelatedMaterials[questionIndex] = relatedIds;
                        }

                        questionIndex++;
                    }
                }

                _logger.LogInformation("Parsed quiz: {Name} with {QuestionCount} questions", quiz.Name, questions.Count);

                // Validate each question based on its type
                foreach (var question in questions)
                {
                    var (isValid, error) = ValidateQuestionByType(question);
                    if (!isValid)
                    {
                        _logger.LogWarning("Question validation failed: {Error}", error);
                        return BadRequest(error);
                    }
                }

                // Use the service method to create quiz with questions
                var createdMaterial = await _materialService.CreateQuizWithQuestionsAsync(quiz, questions);

                _logger.LogInformation("Created quiz material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

                // Process related materials for questions
                await ProcessSubcomponentRelatedMaterialsAsync(
                    "QuizQuestion",
                    questions,
                    questionRelatedMaterials,
                    q => q.QuizQuestionId);

                // Process related materials for answers
                await ProcessQuizAnswerRelatedMaterialsAsync(questions, answerRelatedMaterials);

                // Process related materials if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = "Quiz material created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_id = createdMaterial.Unique_id,
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating quiz from JSON");
                throw;
            }
        }

        private async Task<ActionResult<CreateMaterialResponse>> CreateVoiceFromJson(string tenantName, JsonElement jsonElement)
        {
            try
            {
                _logger.LogInformation("Creating voice material from JSON");

                var voice = new VoiceMaterial();

                if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
                    voice.Name = nameProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
                    voice.Description = descProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "unique_id", out var uniqueIdProp) && uniqueIdProp.ValueKind == JsonValueKind.Number)
                    voice.Unique_id = uniqueIdProp.GetInt32();

                // Parse asset IDs from JSON
                var assetIds = new List<int>();

                if (TryGetPropertyCaseInsensitive(jsonElement, "assetIds", out var assetIdsProp))
                {
                    if (assetIdsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var assetIdElement in assetIdsProp.EnumerateArray())
                        {
                            if (assetIdElement.ValueKind == JsonValueKind.Number)
                            {
                                assetIds.Add(assetIdElement.GetInt32());
                            }
                        }
                    }
                }

                // Alternative property names
                if (!assetIds.Any() && TryGetPropertyCaseInsensitive(jsonElement, "asset_ids", out var altAssetIdsProp))
                {
                    if (altAssetIdsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var assetIdElement in altAssetIdsProp.EnumerateArray())
                        {
                            if (assetIdElement.ValueKind == JsonValueKind.Number)
                            {
                                assetIds.Add(assetIdElement.GetInt32());
                            }
                        }
                    }
                }

                // Create the voice material
                VoiceMaterial createdMaterial;
                if (assetIds.Any())
                {
                    createdMaterial = await _voiceMaterialService.CreateWithAssetsAsync(voice, assetIds);
                }
                else
                {
                    createdMaterial = await _voiceMaterialService.CreateAsync(voice);
                }

                _logger.LogInformation("Created voice material {Name} with ID {Id} and {AssetCount} assets",
                    createdMaterial.Name, createdMaterial.id, assetIds.Count);

                // Process related materials if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = "Voice material created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_id = createdMaterial.Unique_id,
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating voice material from JSON");
                throw;
            }
        }

        private async Task<ActionResult<CreateMaterialResponse>> CreateBasicMaterialFromJson(string tenantName, JsonElement jsonElement)
        {
            try
            {
                _logger.LogInformation("📄 Creating basic material from JSON");
                
                // Parse the basic material using the existing logic
                var material = ParseMaterialFromJson(jsonElement);
                
                if (material == null)
                {
                    return BadRequest("Invalid material data or unsupported material type");
                }
                
                // Use the complete creation method to handle subcomponents (annotations, timestamps, etc.)
                var createdMaterial = await _materialService.CreateMaterialAsyncComplete(material);

                _logger.LogInformation("Created material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

                // Process related materials for subcomponents (annotations, timestamps, steps, entries, etc.)
                await ProcessSubcomponentRelatedMaterialsForUpdateAsync(createdMaterial, jsonElement);

                // Process related materials for the parent material if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = "Material created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_id = createdMaterial.Unique_id,
                    AssetId = (createdMaterial as DefaultMaterial)?.AssetId ??
                              (createdMaterial as VideoMaterial)?.AssetId ??
                              (createdMaterial as ImageMaterial)?.AssetId ??
                              (createdMaterial as PDFMaterial)?.AssetId ??
                              (createdMaterial as UnityMaterial)?.AssetId,
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating basic material from JSON");
                throw;
            }
        }
        private Material? ParseMaterialFromJson(JsonElement jsonElement)
        {
            _logger.LogInformation("Parsing material JSON: {Json}", jsonElement.ToString());

            // Get the discriminator/type from the JSON (case-insensitive)
            string? discriminator = null;
            string? typeValue = null;

            // Try different variations of property names
            if (TryGetPropertyCaseInsensitive(jsonElement, "discriminator", out var discProp))
            {
                discriminator = discProp.GetString();
                _logger.LogInformation("Found discriminator: {Discriminator}", discriminator);
            }
            else if (TryGetPropertyCaseInsensitive(jsonElement, "type", out var typeProp))
            {
                // Handle both string and numeric type values
                if (typeProp.ValueKind == JsonValueKind.String)
                {
                    typeValue = typeProp.GetString();
                    _logger.LogInformation("Found type (string): {Type}", typeValue);
                }
                else if (typeProp.ValueKind == JsonValueKind.Number)
                {
                    // Convert numeric type to string equivalent
                    var numericType = typeProp.GetInt32();
                    typeValue = numericType switch
                    {
                        0 => "image",
                        1 => "video",
                        2 => "pdf",
                        3 => "unitydemo",
                        4 => "chatbot",
                        5 => "questionnaire",
                        6 => "checklist",
                        7 => "workflow",
                        8 => "mqtt_template",
                        9 => "answers",
                        10 => "quiz",
                        11 => "default",
                        _ => "default"
                    };
                    _logger.LogInformation("Found type (numeric {NumericType}), converted to: {Type}", numericType, typeValue);
                }
            }
            else if (TryGetPropertyCaseInsensitive(jsonElement, "materialType", out var matTypeProp))
            {
                // Some requests use "materialType" instead of "type"
                if (matTypeProp.ValueKind == JsonValueKind.String)
                {
                    typeValue = matTypeProp.GetString();
                    _logger.LogInformation("Found materialType (string): {Type}", typeValue);
                }
                else if (matTypeProp.ValueKind == JsonValueKind.Number)
                {
                    var numericType = matTypeProp.GetInt32();
                    typeValue = numericType switch
                    {
                        0 => "image",
                        1 => "video",
                        2 => "pdf",
                        3 => "unitydemo",
                        4 => "chatbot",
                        5 => "questionnaire",
                        6 => "checklist",
                        7 => "workflow",
                        8 => "mqtt_template",
                        9 => "answers",
                        10 => "quiz",
                        11 => "default",
                        _ => "default"
                    };
                    _logger.LogInformation("Found materialType (numeric {NumericType}), converted to: {Type}", numericType, typeValue);
                }
            }

            // Create the appropriate material type
            Material material = (discriminator?.ToLower(), typeValue?.ToLower()) switch
            {
                ("videomaterial", _) or (_, "video") => new VideoMaterial(),
                ("imagematerial", _) or (_, "image") => new ImageMaterial(),
                ("checklistmaterial", _) or (_, "checklist") => new ChecklistMaterial(),
                ("workflowmaterial", _) or (_, "workflow") => new WorkflowMaterial(),
                ("pdfmaterial", _) or (_, "pdf") => new PDFMaterial(),
                ("unitydemo", _) or (_, "unitydemo") or ("unitymaterial", _) or (_, "unity") => new UnityMaterial(),
                ("chatbotmaterial", _) or (_, "chatbot") => new ChatbotMaterial(),
                ("questionnairematerial", _) or (_, "questionnaire") => new QuestionnaireMaterial(),
                ("quizmaterial", _) or (_, "quiz") => new QuizMaterial(),
                ("mqtt_templatematerial", _) or (_, "mqtt_template") => new MQTT_TemplateMaterial(),
                ("defaultmaterial", _) or (_, "default") => new DefaultMaterial(),
                _ => new DefaultMaterial() // Default fallback
            };

            _logger.LogInformation("Created material type: {MaterialType}", material.GetType().Name);

            // Populate common properties (case-insensitive)
            if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
            {
                material.Name = nameProp.GetString();
                _logger.LogInformation("Set material name: {Name}", material.Name);
            }
            else
            {
                _logger.LogWarning("No 'name' property found in JSON");
            }

            if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
            {
                material.Description = descProp.GetString();
                _logger.LogInformation("Set material description: {Description}", material.Description);
            }

            if (TryGetPropertyCaseInsensitive(jsonElement, "unique_id", out var uniqueIdProp) && uniqueIdProp.ValueKind == JsonValueKind.Number)
            {
                material.Unique_id = uniqueIdProp.GetInt32();
                _logger.LogInformation("Set material unique_id: {UniqueId}", material.Unique_id);
            }

            // Populate type-specific properties
            PopulateTypeSpecificProperties(material, jsonElement);

            return material;
        }

        /// <summary>
        /// Parses material from JSON for update operations, using existing material's type as fallback
        /// </summary>
        private Material? ParseMaterialFromJsonForUpdate(JsonElement jsonElement, Material existingMaterial)
        {
            // Check if JSON specifies a type
            bool hasTypeInJson = TryGetPropertyCaseInsensitive(jsonElement, "discriminator", out _) ||
                                 TryGetPropertyCaseInsensitive(jsonElement, "type", out _) ||
                                 TryGetPropertyCaseInsensitive(jsonElement, "materialType", out _);

            if (hasTypeInJson)
            {
                // Type is specified in JSON, use normal parsing
                return ParseMaterialFromJson(jsonElement);
            }

            // No type in JSON - create material instance matching existing type
            _logger.LogInformation("No type in update JSON, using existing material type: {Type}", existingMaterial.GetType().Name);

            Material material = existingMaterial switch
            {
                VideoMaterial => new VideoMaterial(),
                ImageMaterial => new ImageMaterial(),
                ChecklistMaterial => new ChecklistMaterial(),
                WorkflowMaterial => new WorkflowMaterial(),
                PDFMaterial => new PDFMaterial(),
                UnityMaterial => new UnityMaterial(),
                ChatbotMaterial => new ChatbotMaterial(),
                QuestionnaireMaterial => new QuestionnaireMaterial(),
                QuizMaterial => new QuizMaterial(),
                MQTT_TemplateMaterial => new MQTT_TemplateMaterial(),
                DefaultMaterial => new DefaultMaterial(),
                _ => new DefaultMaterial()
            };

            // Populate common properties
            if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
                material.Name = nameProp.GetString();

            if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
                material.Description = descProp.GetString();

            if (TryGetPropertyCaseInsensitive(jsonElement, "unique_id", out var uniqueIdProp) && uniqueIdProp.ValueKind == JsonValueKind.Number)
                material.Unique_id = uniqueIdProp.GetInt32();

            // Populate type-specific properties
            PopulateTypeSpecificProperties(material, jsonElement);

            return material;
        }

        // Helper method for case-insensitive property lookup
        private bool TryGetPropertyCaseInsensitive(JsonElement jsonElement, string propertyName, out JsonElement property)
        {
            // Try exact match first
            if (jsonElement.TryGetProperty(propertyName, out property))
                return true;

            // Try capitalized version
            var capitalizedName = char.ToUpper(propertyName[0]) + propertyName.Substring(1);
            if (jsonElement.TryGetProperty(capitalizedName, out property))
                return true;

            // Try lowercase version
            var lowerName = propertyName.ToLower();
            if (jsonElement.TryGetProperty(lowerName, out property))
                return true;

            // Try to find by iterating through all properties (last resort)
            foreach (var prop in jsonElement.EnumerateObject())
            {
                if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = prop.Value;
                    return true;
                }
            }

            property = default;
            return false;
        }

        // Known question types with special validation rules
        private static readonly string[] KnownQuestionTypes = { "text", "boolean", "choice", "checkboxes", "scale" };

        /// <summary>
        /// Normalizes question type from various display names to internal type names.
        /// Supports both internal names and human-readable display names from clients.
        /// </summary>
        private static string NormalizeQuestionType(string? type)
        {
            if (string.IsNullOrEmpty(type))
                return "text";

            return type.ToLowerInvariant().Trim() switch
            {
                // Internal names (already normalized)
                "text" => "text",
                "boolean" => "boolean",
                "choice" => "choice",
                "checkboxes" => "checkboxes",
                "scale" => "scale",

                // Human-readable display names from clients
                "true or false" => "boolean",
                "true/false" => "boolean",
                "yes or no" => "boolean",
                "yes/no" => "boolean",

                "multiple choice" => "choice",
                "multiplechoice" => "choice",
                "single choice" => "choice",
                "radio" => "choice",

                "selection checkboxes" => "checkboxes",
                "checkbox" => "checkboxes",
                "multi select" => "checkboxes",
                "multiselect" => "checkboxes",
                "multiple select" => "checkboxes",

                "likert" => "scale",
                "rating" => "scale",

                "open" => "text",
                "free text" => "text",
                "freetext" => "text",
                "open ended" => "text",

                // Unknown types - return as-is (lenient)
                _ => type.ToLowerInvariant().Trim()
            };
        }

        /// <summary>
        /// Converts internal question type to display name for frontend.
        /// </summary>
        private static string DenormalizeQuestionType(string? type)
        {
            if (string.IsNullOrEmpty(type))
                return "Open";

            return type.ToLowerInvariant().Trim() switch
            {
                "text" => "Open",
                "boolean" => "True or False",
                "choice" => "Multiple choice",
                "checkboxes" => "Selection checkboxes",
                "scale" => "Scale",
                // Unknown types - return as-is with first letter capitalized
                _ => char.ToUpper(type[0]) + type.Substring(1).ToLower()
            };
        }

        /// <summary>
        /// Validates a quiz question based on its type.
        /// Returns (isValid, errorMessage) tuple.
        /// Lenient validation: unknown types are allowed, only known types have specific rules.
        /// </summary>
        private (bool isValid, string? error) ValidateQuestionByType(QuizQuestion question)
        {
            var type = NormalizeQuestionType(question.QuestionType);

            switch (type)
            {
                case "boolean":
                    if (question.Answers?.Count != 2)
                        return (false, $"Boolean question '{question.Text}' must have exactly 2 answers");
                    break;

                case "scale":
                    if (string.IsNullOrEmpty(question.ScaleConfig))
                        return (false, $"Scale question '{question.Text}' requires scaleConfig");
                    break;

                case "choice":
                    if (question.Answers == null || question.Answers.Count < 2)
                        return (false, $"Choice question '{question.Text}' requires at least 2 answers");
                    break;

                case "checkboxes":
                    if (question.Answers == null || question.Answers.Count < 2)
                        return (false, $"Checkboxes question '{question.Text}' requires at least 2 answers");
                    break;

                // text and unknown types have no specific validation
            }

            return (true, null);
        }

        private void PopulateTypeSpecificProperties(Material material, JsonElement jsonElement)
        {
            _logger.LogInformation(" Populating type-specific properties for {MaterialType}", material.GetType().Name);

            switch (material)
            {
                case WorkflowMaterial workflow:
                    _logger.LogInformation("Processing workflow material...");

                    // Handle workflow steps - check "config" object first, then direct "steps" array
                    JsonElement stepsElement = default;
                    bool hasSteps = false;

                    if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var workflowConfigElement))
                    {
                        if (TryGetPropertyCaseInsensitive(workflowConfigElement, "steps", out stepsElement))
                        {
                            hasSteps = true;
                            _logger.LogInformation("Found steps in config object");
                        }
                    }

                    if (!hasSteps && TryGetPropertyCaseInsensitive(jsonElement, "steps", out stepsElement))
                    {
                        hasSteps = true;
                        _logger.LogInformation("Found steps directly");
                    }

                    if (hasSteps && stepsElement.ValueKind == JsonValueKind.Array)
                    {
                        var steps = new List<WorkflowStep>();
                        _logger.LogInformation("Steps is an array with {Count} elements", stepsElement.GetArrayLength());

                        foreach (var stepElement in stepsElement.EnumerateArray())
                        {
                            var step = new WorkflowStep();

                            if (TryGetPropertyCaseInsensitive(stepElement, "title", out var titleProp))
                            {
                                step.Title = titleProp.GetString() ?? "";
                                _logger.LogInformation("Step title: {Title}", step.Title);
                            }

                            if (TryGetPropertyCaseInsensitive(stepElement, "content", out var contentProp))
                            {
                                step.Content = contentProp.GetString();
                                _logger.LogInformation("Step content: {Content}", step.Content);
                            }

                            steps.Add(step);
                        }

                        workflow.WorkflowSteps = steps;
                        _logger.LogInformation("Added {Count} workflow steps", steps.Count);
                    }
                    else if (!hasSteps)
                    {
                        _logger.LogWarning("No 'steps' property found in workflow JSON");
                    }
                    break;

                case ChecklistMaterial checklist:
                    // Handle checklist entries - check "config" object first, then direct "entries" array
                    JsonElement entriesElement = default;
                    bool hasEntries = false;

                    if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var checklistConfigElement))
                    {
                        if (TryGetPropertyCaseInsensitive(checklistConfigElement, "entries", out entriesElement))
                        {
                            hasEntries = true;
                            _logger.LogInformation("Found entries in config object");
                        }
                    }

                    if (!hasEntries && TryGetPropertyCaseInsensitive(jsonElement, "entries", out entriesElement))
                    {
                        hasEntries = true;
                        _logger.LogInformation("Found entries directly");
                    }

                    if (hasEntries && entriesElement.ValueKind == JsonValueKind.Array)
                    {
                        var entries = new List<ChecklistEntry>();
                        foreach (var entryElement in entriesElement.EnumerateArray())
                        {
                            var entry = new ChecklistEntry();

                            if (TryGetPropertyCaseInsensitive(entryElement, "text", out var textProp))
                                entry.Text = textProp.GetString() ?? "";

                            if (TryGetPropertyCaseInsensitive(entryElement, "description", out var descProp))
                                entry.Description = descProp.GetString();

                            entries.Add(entry);
                        }
                        checklist.Entries = entries;
                        _logger.LogInformation("Added {Count} checklist entries", entries.Count);
                    }
                    break;

                case QuizMaterial quiz:
                    _logger.LogInformation("Processing quiz material...");

                    // Try to get questions from "config" object first, then try direct "questions" array
                    JsonElement questionsElement;
                    bool hasQuestions = false;

                    if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configElement))
                    {
                        if (TryGetPropertyCaseInsensitive(configElement, "questions", out questionsElement))
                        {
                            hasQuestions = true;
                            _logger.LogInformation("Found questions in config object");
                        }
                    }
                    else if (TryGetPropertyCaseInsensitive(jsonElement, "questions", out questionsElement))
                    {
                        hasQuestions = true;
                        _logger.LogInformation("Found questions directly");
                    }

                    if (hasQuestions && questionsElement.ValueKind == JsonValueKind.Array)
                    {
                        var questions = new List<QuizQuestion>();
                        _logger.LogInformation("Questions array has {Count} elements", questionsElement.GetArrayLength());

                        foreach (var questionElement in questionsElement.EnumerateArray())
                        {
                            var question = new QuizQuestion();

                            if (TryGetPropertyCaseInsensitive(questionElement, "id", out var idProp))
                            {
                                if (idProp.ValueKind == JsonValueKind.String && int.TryParse(idProp.GetString(), out var idValue))
                                    question.QuestionNumber = idValue;
                                else if (idProp.ValueKind == JsonValueKind.Number)
                                    question.QuestionNumber = idProp.GetInt32();
                            }

                            // Accept both "type" and "questionType" for flexibility
                            if (TryGetPropertyCaseInsensitive(questionElement, "questionType", out var questionTypeProp))
                                question.QuestionType = NormalizeQuestionType(questionTypeProp.GetString());
                            else if (TryGetPropertyCaseInsensitive(questionElement, "type", out var typeProp))
                                question.QuestionType = NormalizeQuestionType(typeProp.GetString());

                            if (TryGetPropertyCaseInsensitive(questionElement, "text", out var textProp))
                                question.Text = textProp.GetString() ?? "";

                            if (TryGetPropertyCaseInsensitive(questionElement, "description", out var descProp))
                                question.Description = descProp.GetString();

                            if (TryGetPropertyCaseInsensitive(questionElement, "score", out var scoreProp))
                            {
                                if (scoreProp.ValueKind == JsonValueKind.String)
                                {
                                    if (decimal.TryParse(scoreProp.GetString(), out var scoreValue))
                                        question.Score = scoreValue;
                                }
                                else if (scoreProp.ValueKind == JsonValueKind.Number)
                                {
                                    question.Score = scoreProp.GetDecimal();
                                }
                            }

                            if (TryGetPropertyCaseInsensitive(questionElement, "helpText", out var helpProp))
                                question.HelpText = helpProp.GetString();

                            if (TryGetPropertyCaseInsensitive(questionElement, "allowMultiple", out var multiProp))
                            {
                                if (multiProp.ValueKind == JsonValueKind.String && bool.TryParse(multiProp.GetString(), out var boolValue))
                                    question.AllowMultiple = boolValue;
                                else if (multiProp.ValueKind == JsonValueKind.True || multiProp.ValueKind == JsonValueKind.False)
                                    question.AllowMultiple = multiProp.GetBoolean();
                            }

                            if (TryGetPropertyCaseInsensitive(questionElement, "scaleConfig", out var scaleProp))
                                question.ScaleConfig = scaleProp.GetString();

                            // Handle answers
                            if (TryGetPropertyCaseInsensitive(questionElement, "answers", out var answersElement))
                            {
                                if (answersElement.ValueKind == JsonValueKind.Array)
                                {
                                    var answers = new List<QuizAnswer>();
                                    foreach (var answerElement in answersElement.EnumerateArray())
                                    {
                                        var answer = new QuizAnswer();

                                        if (TryGetPropertyCaseInsensitive(answerElement, "text", out var ansTextProp))
                                            answer.Text = ansTextProp.GetString() ?? "";

                                        // Support both "correctAnswer" and legacy "isCorrect" property names
                                        if (TryGetPropertyCaseInsensitive(answerElement, "correctAnswer", out var correctProp) ||
                                            TryGetPropertyCaseInsensitive(answerElement, "isCorrect", out correctProp))
                                        {
                                            if (correctProp.ValueKind == JsonValueKind.String && bool.TryParse(correctProp.GetString(), out var correctValue))
                                                answer.CorrectAnswer = correctValue;
                                            else if (correctProp.ValueKind == JsonValueKind.True || correctProp.ValueKind == JsonValueKind.False)
                                                answer.CorrectAnswer = correctProp.GetBoolean();
                                        }

                                        if (TryGetPropertyCaseInsensitive(answerElement, "displayOrder", out var orderProp))
                                        {
                                            if (orderProp.ValueKind == JsonValueKind.String && int.TryParse(orderProp.GetString(), out var orderValue))
                                                answer.DisplayOrder = orderValue;
                                            else if (orderProp.ValueKind == JsonValueKind.Number)
                                                answer.DisplayOrder = orderProp.GetInt32();
                                        }

                                        if (TryGetPropertyCaseInsensitive(answerElement, "extra", out var extraProp))
                                            answer.Extra = extraProp.GetString();

                                        answers.Add(answer);
                                    }
                                    question.Answers = answers;
                                    _logger.LogInformation("Added {Count} answers to question", answers.Count);
                                }
                            }

                            questions.Add(question);
                        }
                        quiz.Questions = questions;
                        _logger.LogInformation("Added {Count} questions to quiz", questions.Count);
                    }
                    break;

                case VideoMaterial video:
                    // Handle video timestamps and properties - check both root level and inside config object
                    JsonElement? tsElement = null;
                    if (TryGetPropertyCaseInsensitive(jsonElement, "timestamps", out var rootTs) &&
                        rootTs.ValueKind == JsonValueKind.Array)
                    {
                        tsElement = rootTs;
                    }
                    else if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configEl) &&
                             TryGetPropertyCaseInsensitive(configEl, "timestamps", out var configTs) &&
                             configTs.ValueKind == JsonValueKind.Array)
                    {
                        tsElement = configTs;
                    }

                    if (tsElement.HasValue)
                    {
                        var timestamps = new List<VideoTimestamp>();
                        foreach (var timestampElement in tsElement.Value.EnumerateArray())
                        {
                            var timestamp = new VideoTimestamp();

                            if (TryGetPropertyCaseInsensitive(timestampElement, "title", out var titleProp))
                                timestamp.Title = titleProp.GetString() ?? "";

                            int? startTimeInt = null;
                            if (TryGetPropertyCaseInsensitive(timestampElement, "startTime", out var timeProp))
                            {
                                if (timeProp.ValueKind == JsonValueKind.Number)
                                {
                                    startTimeInt = timeProp.GetInt32();
                                    timestamp.startTime = startTimeInt.ToString()!;
                                }
                                else
                                {
                                    timestamp.startTime = timeProp.GetString() ?? "";
                                    int.TryParse(timestamp.startTime, out var parsed);
                                    startTimeInt = parsed;
                                }
                            }

                            // Parse duration
                            int? tsDuration = null;
                            if (TryGetPropertyCaseInsensitive(timestampElement, "duration", out var tsDurationProp))
                            {
                                if (tsDurationProp.ValueKind == JsonValueKind.Number)
                                    tsDuration = tsDurationProp.GetInt32();
                                else if (int.TryParse(tsDurationProp.GetString(), out var parsedDuration))
                                    tsDuration = parsedDuration;
                                timestamp.Duration = tsDuration;
                            }

                            // Parse endTime, or calculate from startTime + duration
                            if (TryGetPropertyCaseInsensitive(timestampElement, "endTime", out var endTimeProp))
                            {
                                timestamp.endTime = endTimeProp.ValueKind == JsonValueKind.Number
                                    ? endTimeProp.GetInt32().ToString()
                                    : endTimeProp.GetString() ?? "";
                            }
                            else if (startTimeInt.HasValue && tsDuration.HasValue)
                            {
                                // Calculate endTime from startTime + duration
                                timestamp.endTime = (startTimeInt.Value + tsDuration.Value).ToString();
                            }

                            if (TryGetPropertyCaseInsensitive(timestampElement, "description", out var descProp))
                                timestamp.Description = descProp.GetString();

                            if (TryGetPropertyCaseInsensitive(timestampElement, "type", out var typeProp))
                                timestamp.Type = typeProp.GetString();

                            timestamps.Add(timestamp);
                        }
                        video.Timestamps = timestamps;
                        _logger.LogInformation("Added {Count} video timestamps", timestamps.Count);
                    }
                    
                    // Video-specific properties
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var videoAssetId) && videoAssetId.ValueKind == JsonValueKind.Number)
                        video.AssetId = videoAssetId.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "videoPath", out var videoPath) && videoPath.ValueKind == JsonValueKind.String)
                        video.VideoPath = videoPath.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "videoDuration", out var duration) && duration.ValueKind == JsonValueKind.Number)
                        video.VideoDuration = duration.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "videoResolution", out var resolution) && resolution.ValueKind == JsonValueKind.String)
                        video.VideoResolution = resolution.GetString();
                    break;

                case MQTT_TemplateMaterial mqtt:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "message_type", out var msgType) && msgType.ValueKind == JsonValueKind.String)
                        mqtt.message_type = msgType.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "message_text", out var msgText) && msgText.ValueKind == JsonValueKind.String)
                        mqtt.message_text = msgText.GetString();
                    break;

                case UnityMaterial unity:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var unityAssetId) && unityAssetId.ValueKind == JsonValueKind.Number)
                        unity.AssetId = unityAssetId.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "unityVersion", out var version) && version.ValueKind == JsonValueKind.String)
                        unity.UnityVersion = version.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "unityBuildTarget", out var buildTarget) && buildTarget.ValueKind == JsonValueKind.String)
                        unity.UnityBuildTarget = buildTarget.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "unitySceneName", out var sceneName) && sceneName.ValueKind == JsonValueKind.String)
                        unity.UnitySceneName = sceneName.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "unityJson", out var unityJson))
                        unity.UnityJson = unityJson.ValueKind == JsonValueKind.String ? unityJson.GetString() : unityJson.GetRawText();
                    break;

                case DefaultMaterial defaultMat:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var defaultAssetId) && defaultAssetId.ValueKind == JsonValueKind.Number)
                        defaultMat.AssetId = defaultAssetId.GetInt32();
                    break;

                case ImageMaterial image:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var imageAssetId) && imageAssetId.ValueKind == JsonValueKind.Number)
                        image.AssetId = imageAssetId.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "imagePath", out var imagePath) && imagePath.ValueKind == JsonValueKind.String)
                        image.ImagePath = imagePath.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "imageWidth", out var width) && width.ValueKind == JsonValueKind.Number)
                        image.ImageWidth = width.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "imageHeight", out var height) && height.ValueKind == JsonValueKind.Number)
                        image.ImageHeight = height.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "imageFormat", out var format) && format.ValueKind == JsonValueKind.String)
                        image.ImageFormat = format.GetString();
                    // Parse annotations array
                    if (TryGetPropertyCaseInsensitive(jsonElement, "annotations", out var annotationsElement) &&
                        annotationsElement.ValueKind == JsonValueKind.Array)
                    {
                        var imageAnnotations = new List<ImageAnnotation>();
                        foreach (var annotationElement in annotationsElement.EnumerateArray())
                        {
                            var annotation = new ImageAnnotation();

                            // Preserve ImageAnnotationId for updates
                            if (TryGetPropertyCaseInsensitive(annotationElement, "imageAnnotationId", out var annotationIdProp))
                            {
                                if (annotationIdProp.ValueKind == JsonValueKind.Number)
                                    annotation.ImageAnnotationId = annotationIdProp.GetInt32();
                                else if (annotationIdProp.ValueKind == JsonValueKind.String && int.TryParse(annotationIdProp.GetString(), out var annotationId))
                                    annotation.ImageAnnotationId = annotationId;
                            }

                            // Client ID (string like "of1pw6n")
                            if (TryGetPropertyCaseInsensitive(annotationElement, "id", out var clientIdProp) ||
                                TryGetPropertyCaseInsensitive(annotationElement, "clientId", out clientIdProp))
                                annotation.ClientId = clientIdProp.GetString();

                            if (TryGetPropertyCaseInsensitive(annotationElement, "text", out var textProp))
                                annotation.Text = textProp.GetString();

                            if (TryGetPropertyCaseInsensitive(annotationElement, "fontsize", out var fontsizeProp) ||
                                TryGetPropertyCaseInsensitive(annotationElement, "fontSize", out fontsizeProp))
                            {
                                if (fontsizeProp.ValueKind == JsonValueKind.Number)
                                    annotation.FontSize = fontsizeProp.GetInt32();
                            }

                            if (TryGetPropertyCaseInsensitive(annotationElement, "x", out var xProp))
                            {
                                if (xProp.ValueKind == JsonValueKind.Number)
                                    annotation.X = xProp.GetDouble();
                            }

                            if (TryGetPropertyCaseInsensitive(annotationElement, "y", out var yProp))
                            {
                                if (yProp.ValueKind == JsonValueKind.Number)
                                    annotation.Y = yProp.GetDouble();
                            }

                            imageAnnotations.Add(annotation);
                        }
                        image.ImageAnnotations = imageAnnotations;
                        _logger.LogInformation("Parsed {Count} image annotations", imageAnnotations.Count);
                    }
                    break;

                case PDFMaterial pdf:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var pdfAssetId))
                        pdf.AssetId = pdfAssetId.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "pdfPath", out var pdfPath))
                        pdf.PdfPath = pdfPath.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "pdfPageCount", out var pageCount))
                        pdf.PdfPageCount = pageCount.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "pdfFileSize", out var fileSize))
                        pdf.PdfFileSize = fileSize.GetInt64();
                    break;

                case ChatbotMaterial chatbot:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "chatbotConfig", out var config))
                        chatbot.ChatbotConfig = config.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "chatbotModel", out var model))
                        chatbot.ChatbotModel = model.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "chatbotPrompt", out var prompt))
                        chatbot.ChatbotPrompt = prompt.GetString();
                    break;

                case QuestionnaireMaterial questionnaire:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "questionnaireConfig", out var qConfig))
                        questionnaire.QuestionnaireConfig = qConfig.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "questionnaireType", out var qType))
                        questionnaire.QuestionnaireType = qType.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "passingScore", out var score))
                        questionnaire.PassingScore = score.GetDecimal();

                    // Handle questionnaire entries - check "config" object first, then direct "entries" array
                    JsonElement qEntriesElement = default;
                    bool hasQEntries = false;

                    if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var qConfigElement))
                    {
                        if (TryGetPropertyCaseInsensitive(qConfigElement, "entries", out qEntriesElement))
                        {
                            hasQEntries = true;
                            _logger.LogInformation("Found questionnaire entries in config object");
                        }
                    }

                    if (!hasQEntries && TryGetPropertyCaseInsensitive(jsonElement, "entries", out qEntriesElement))
                    {
                        hasQEntries = true;
                        _logger.LogInformation("Found questionnaire entries directly");
                    }

                    if (hasQEntries && qEntriesElement.ValueKind == JsonValueKind.Array)
                    {
                        var qEntries = new List<QuestionnaireEntry>();
                        foreach (var entryElement in qEntriesElement.EnumerateArray())
                        {
                            var qEntry = new QuestionnaireEntry();

                            if (TryGetPropertyCaseInsensitive(entryElement, "text", out var qTextProp))
                                qEntry.Text = qTextProp.GetString() ?? "";

                            if (TryGetPropertyCaseInsensitive(entryElement, "description", out var qDescProp))
                                qEntry.Description = qDescProp.GetString();

                            qEntries.Add(qEntry);
                        }
                        questionnaire.QuestionnaireEntries = qEntries;
                        _logger.LogInformation("Added {Count} questionnaire entries", qEntries.Count);
                    }
                    break;
            }
        }
        
        // PUT: api/{tenantName}/materials/5
        // JSON-based PUT endpoint that accepts the same format as GET responses
        // Supports updating material properties and managing relationships via 'related' array
        [HttpPut("{id}")]
        // PUT: api/{tenantName}/materials/{id} - Update material
        // Accepts both JSON (application/json) and multipart/form-data for updates with optional file uploads
        // Form-data parameters: material (JSON string, required), file (binary, optional), assetData (JSON string, optional)
        public async Task<IActionResult> PutMaterial(string tenantName, string id)
        {
            try
            {
                // Parse the ID from string (supports future UUID migration)
                if (!int.TryParse(id, out int materialId))
                {
                    return BadRequest($"Invalid material ID format: {id}");
                }

                JsonElement jsonElement;
                IFormFile? file = null;
                JsonElement? assetData = null;

                var contentType = Request.ContentType?.ToLower() ?? "";

                _logger.LogInformation("Received material update request with Content-Type: {ContentType}", contentType);

                // Handle different content types
                if (contentType.Contains("multipart/form-data"))
                {
                    // Form data request with optional file upload
                    var form = await Request.ReadFormAsync();

                    if (!form.ContainsKey("material"))
                    {
                        return BadRequest("material is required in form-data requests");
                    }

                    var materialDataString = form["material"].ToString();
                    try
                    {
                        jsonElement = JsonSerializer.Deserialize<JsonElement>(materialDataString);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Invalid JSON in material parameter");
                        return BadRequest("Invalid JSON format in material");
                    }

                    // Extract optional file
                    file = form.Files.GetFile("file");

                    // Extract optional assetData
                    if (form.ContainsKey("assetData") && !string.IsNullOrEmpty(form["assetData"]))
                    {
                        try
                        {
                            assetData = JsonSerializer.Deserialize<JsonElement>(form["assetData"]);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "Invalid JSON in assetData parameter");
                            return BadRequest("Invalid JSON format in assetData");
                        }
                    }

                    _logger.LogInformation("Parsed form-data update request (file: {HasFile})", file != null);
                }
                else if (contentType.Contains("application/json"))
                {
                    // JSON request
                    using var reader = new StreamReader(Request.Body);
                    var body = await reader.ReadToEndAsync();
                    var parsedBody = JsonSerializer.Deserialize<JsonElement>(body);

                    // Check if body is wrapped in {"material": {...}, "file": "..."}
                    if (TryGetPropertyCaseInsensitive(parsedBody, "material", out var materialProp) &&
                        materialProp.ValueKind == JsonValueKind.Object)
                    {
                        // Unwrap the material object
                        jsonElement = materialProp;
                        _logger.LogInformation("Parsed JSON update request with material wrapper");

                        // Check for file URL reference (not a real file upload, but a URL)
                        if (TryGetPropertyCaseInsensitive(parsedBody, "file", out var fileProp) &&
                            fileProp.ValueKind == JsonValueKind.String)
                        {
                            var fileUrl = fileProp.GetString();
                            _logger.LogInformation("Found file URL in wrapped JSON: {FileUrl}", fileUrl);
                            // Note: URL-based file references are handled via assetData, not file upload
                        }
                    }
                    else
                    {
                        // Direct material object (no wrapper)
                        jsonElement = parsedBody;
                        _logger.LogInformation("Parsed JSON update request body (direct)");
                    }
                }
                else
                {
                    _logger.LogWarning("Unsupported Content-Type for PUT: {ContentType}", contentType);
                    return StatusCode(415, $"Unsupported Media Type. Please use 'application/json' or 'multipart/form-data'. Received: {contentType}");
                }

                // Verify ID in body matches route parameter
                if (TryGetPropertyCaseInsensitive(jsonElement, "id", out var idProp))
                {
                    string bodyId = idProp.ValueKind == JsonValueKind.String
                        ? idProp.GetString() ?? ""
                        : idProp.GetInt32().ToString();

                    if (bodyId != id)
                    {
                        return BadRequest($"ID mismatch: route={id}, body={bodyId}");
                    }
                }

                _logger.LogInformation("Updating material {Id} for tenant: {TenantName}", materialId, tenantName);

                // Check if material exists
                var existingMaterial = await _materialService.GetMaterialAsync(materialId);
                if (existingMaterial == null)
                {
                    return NotFound($"Material with ID {materialId} not found");
                }

                // Parse the material from JSON, using existing material's type as fallback
                var material = ParseMaterialFromJsonForUpdate(jsonElement, existingMaterial);
                if (material == null)
                {
                    return BadRequest("Invalid material data or unsupported material type");
                }

                // Ensure the ID is set correctly
                material.id = materialId;

                // Handle file upload if provided
                if (file != null)
                {
                    _logger.LogInformation("Processing file upload for material update: {FileName}", file.FileName);

                    // Check if material already has an asset
                    int? existingAssetId = GetAssetIdFromMaterial(existingMaterial);

                    if (existingAssetId.HasValue)
                    {
                        // Delete existing asset and create new one (replace)
                        _logger.LogInformation("Replacing existing asset {AssetId} with new file", existingAssetId.Value);
                        try
                        {
                            await _assetService.DeleteAssetAsync(tenantName, existingAssetId.Value);
                            _logger.LogInformation("Deleted old asset {AssetId}", existingAssetId.Value);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete old asset {AssetId}, continuing with new asset creation", existingAssetId.Value);
                        }
                    }

                    // Create new asset
                    _logger.LogInformation("Creating new asset for material {MaterialId}", materialId);
                    var createdAsset = await CreateAssetFromFile(tenantName, file, assetData);
                    _logger.LogInformation("Created new asset {AssetId} for material {MaterialId}", createdAsset.Id, materialId);

                    // Set the AssetId on the material
                    SetAssetIdOnMaterial(material, createdAsset.Id);
                }

                // Update the material and get the updated instance with populated child IDs
                var updatedMaterial = await _materialService.UpdateMaterialAsync(material);
                _logger.LogInformation("Updated material {Id} for tenant: {TenantName}", materialId, tenantName);

                // Process related materials for subcomponents based on material type
                // Use updatedMaterial which has the correct database-generated IDs for entries/steps/etc.
                await ProcessSubcomponentRelatedMaterialsForUpdateAsync(updatedMaterial, jsonElement);

                // Handle 'related' array if provided
                if (TryGetPropertyCaseInsensitive(jsonElement, "related", out var relatedElement) &&
                    relatedElement.ValueKind == JsonValueKind.Array)
                {
                    _logger.LogInformation("Processing relationship updates for material {Id}", materialId);

                    // Get current relationships
                    var currentChildren = await _materialService.GetChildMaterialsAsync(materialId, includeOrder: true);
                    var currentChildIds = currentChildren.Select(m => m.id).ToHashSet();

                    // Parse desired relationships from JSON
                    var desiredChildIds = new HashSet<int>();
                    foreach (var relatedItem in relatedElement.EnumerateArray())
                    {
                        if (TryGetPropertyCaseInsensitive(relatedItem, "id", out var childIdProp))
                        {
                            int childId;
                            if (childIdProp.ValueKind == JsonValueKind.String)
                            {
                                if (int.TryParse(childIdProp.GetString(), out childId))
                                {
                                    desiredChildIds.Add(childId);
                                }
                            }
                            else if (childIdProp.ValueKind == JsonValueKind.Number)
                            {
                                desiredChildIds.Add(childIdProp.GetInt32());
                            }
                        }
                    }

                    // Remove relationships that are no longer present
                    foreach (var childId in currentChildIds)
                    {
                        if (!desiredChildIds.Contains(childId))
                        {
                            try
                            {
                                await _materialService.RemoveMaterialFromMaterialAsync(materialId, childId);
                                _logger.LogInformation("Removed relationship: material {ParentId} -> {ChildId}",
                                    materialId, childId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to remove relationship: material {ParentId} -> {ChildId}",
                                    materialId, childId);
                            }
                        }
                    }

                    // Add new relationships
                    int displayOrder = 1;
                    foreach (var childId in desiredChildIds)
                    {
                        if (!currentChildIds.Contains(childId))
                        {
                            try
                            {
                                await _materialService.AssignMaterialToMaterialAsync(
                                    materialId, childId, "contains", displayOrder);
                                _logger.LogInformation("Added relationship: material {ParentId} -> {ChildId}",
                                    materialId, childId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to add relationship: material {ParentId} -> {ChildId}",
                                    materialId, childId);
                            }
                        }
                        displayOrder++;
                    }
                }

                return Ok(new { status = "success", message = $"Material '{updatedMaterial.Name}' with ID {materialId} updated successfully" });
            }
            catch (KeyNotFoundException)
            {
                return NotFound($"Material with ID {id} not found");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid material type for update");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating material {Id} for tenant: {TenantName}", id, tenantName);
                return StatusCode(500, $"Error updating material: {ex.Message}");
            }
        }

        // DELETE: api/{tenantName}/materials/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaterial(string tenantName, int id)
        {
            _logger.LogInformation("Deleting material {Id} for tenant: {TenantName}", id, tenantName);

            var deleted = await _materialService.DeleteMaterialAsync(id);

            if (!deleted)
            {
                return NotFound(new { Status = "error", Message = $"Material with ID {id} not found" });
            }

            _logger.LogInformation("Deleted material {Id} for tenant: {TenantName}", id, tenantName);

            return Ok(new { Status = "success", Message = $"Material with ID {id} deleted successfully" });
        }

        // GET: api/{tenantName}/materials/videos
        [HttpGet("videos")]
        public async Task<ActionResult<IEnumerable<VideoMaterial>>> GetVideoMaterials(string tenantName)
        {
            _logger.LogInformation("🎥 Getting video materials for tenant: {TenantName}", tenantName);

            var videos = await _materialService.GetAllVideoMaterialsAsync();

            _logger.LogInformation("Found {Count} video materials for tenant: {TenantName}",
                videos.Count(), tenantName);

            return Ok(videos);
        }

        // GET: api/{tenantName}/materials/checklists
        [HttpGet("checklists")]
        public async Task<ActionResult<IEnumerable<ChecklistMaterial>>> GetChecklistMaterials(string tenantName)
        {
            _logger.LogInformation("Getting checklist materials for tenant: {TenantName}", tenantName);

            var checklists = await _materialService.GetAllChecklistMaterialsAsync();

            _logger.LogInformation("Found {Count} checklist materials for tenant: {TenantName}",
                checklists.Count(), tenantName);

            return Ok(checklists);
        }

        // GET: api/{tenantName}/materials/workflows
        [HttpGet("workflows")]
        public async Task<ActionResult<IEnumerable<WorkflowMaterial>>> GetWorkflowMaterials(string tenantName)
        {
            _logger.LogInformation("Getting workflow materials for tenant: {TenantName}", tenantName);

            var workflows = await _materialService.GetAllWorkflowMaterialsAsync();

            _logger.LogInformation("Found {Count} workflow materials for tenant: {TenantName}",
                workflows.Count(), tenantName);

            return Ok(workflows);
        }

        // GET: api/{tenantName}/materials/images
        [HttpGet("images")]
        public async Task<ActionResult<IEnumerable<ImageMaterial>>> GetImageMaterials(string tenantName)
        {
            _logger.LogInformation("Getting image materials for tenant: {TenantName}", tenantName);

            var images = await _materialService.GetAllImageMaterialsAsync();

            _logger.LogInformation("Found {Count} image materials for tenant: {TenantName}",
                images.Count(), tenantName);

            return Ok(images);
        }

        // GET: api/{tenantName}/materials/pdfs
        [HttpGet("pdfs")]
        public async Task<ActionResult<IEnumerable<PDFMaterial>>> GetPDFMaterials(string tenantName)
        {
            _logger.LogInformation("📄 Getting PDF materials for tenant: {TenantName}", tenantName);

            var pdfs = await _materialService.GetAllPDFMaterialsAsync();

            _logger.LogInformation("Found {Count} PDF materials for tenant: {TenantName}",
                pdfs.Count(), tenantName);

            return Ok(pdfs);
        }

        // GET: api/{tenantName}/materials/chatbots
        [HttpGet("chatbots")]
        public async Task<ActionResult<IEnumerable<ChatbotMaterial>>> GetChatbotMaterials(string tenantName)
        {
            _logger.LogInformation("🤖 Getting chatbot materials for tenant: {TenantName}", tenantName);

            var chatbots = await _materialService.GetAllChatbotMaterialsAsync();

            _logger.LogInformation("Found {Count} chatbot materials for tenant: {TenantName}",
                chatbots.Count(), tenantName);

            return Ok(chatbots);
        }

        // GET: api/{tenantName}/materials/questionnaires
        [HttpGet("questionnaires")]
        public async Task<ActionResult<IEnumerable<QuestionnaireMaterial>>> GetQuestionnaireMaterials(string tenantName)
        {
            _logger.LogInformation("❓ Getting questionnaire materials for tenant: {TenantName}", tenantName);

            var questionnaires = await _materialService.GetAllQuestionnaireMaterialsAsync();

            _logger.LogInformation("Found {Count} questionnaire materials for tenant: {TenantName}",
                questionnaires.Count(), tenantName);

            return Ok(questionnaires);
        }

        // GET: api/{tenantName}/materials/mqtt-templates
        [HttpGet("mqtt-templates")]
        public async Task<ActionResult<IEnumerable<MQTT_TemplateMaterial>>> GetMQTTTemplateMaterials(string tenantName)
        {
            _logger.LogInformation("📡 Getting MQTT template materials for tenant: {TenantName}", tenantName);

            var templates = await _materialService.GetAllMQTTTemplateMaterialsAsync();

            _logger.LogInformation("Found {Count} MQTT template materials for tenant: {TenantName}",
                templates.Count(), tenantName);

            return Ok(templates);
        }

        // GET: api/{tenantName}/materials/unity-demos
        [HttpGet("unity-demos")]
        public async Task<ActionResult<IEnumerable<UnityMaterial>>> GetUnityMaterials(string tenantName)
        {
            _logger.LogInformation("🎮 Getting Unity demo materials for tenant: {TenantName}", tenantName);

            var unitys = await _materialService.GetAllUnityMaterialsAsync();

            _logger.LogInformation("Found {Count} Unity demo materials for tenant: {TenantName}",
                unitys.Count(), tenantName);

            return Ok(unitys);
        }

        // POST: api/{tenantName}/materials/workflow-complete
        [HttpPost("workflow-complete")]
        public async Task<ActionResult<WorkflowMaterial>> CreateCompleteWorkflow(
            string tenantName,
            [FromBody] CompleteWorkflowRequest request)
        {
            _logger.LogInformation("Creating complete workflow {Name} with {StepCount} steps for tenant: {TenantName}",
                request.Workflow.Name, request.Steps?.Count ?? 0, tenantName);

            try
            {
                var createdMaterial = await _materialService.CreateWorkflowWithStepsAsync(
                    request.Workflow,
                    request.Steps);

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    createdMaterial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating complete workflow for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error creating workflow: {ex.Message}");
            }
        }

        // POST: api/{tenantName}/materials/video-complete
        [HttpPost("video-complete")]
        public async Task<ActionResult<VideoMaterial>> CreateCompleteVideo(
            string tenantName,
            [FromBody] CompleteVideoRequest request)
        {
            _logger.LogInformation("🎥 Creating complete video {Name} with {TimestampCount} timestamps for tenant: {TenantName}",
                request.Video.Name, request.Timestamps?.Count ?? 0, tenantName);

            try
            {
                var createdMaterial = await _materialService.CreateVideoWithTimestampsAsync(
                    request.Video,
                    request.Timestamps);

                // Process related materials for timestamps if provided
                if (request.TimestampRelatedMaterials != null && request.TimestampRelatedMaterials.Any() && request.Timestamps != null)
                {
                    _logger.LogInformation("Processing related materials for {Count} timestamps", request.TimestampRelatedMaterials.Count);
                    await ProcessSubcomponentRelatedMaterialsAsync(
                        "VideoTimestamp",
                        request.Timestamps,
                        request.TimestampRelatedMaterials,
                        t => t.id);
                }

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    createdMaterial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating complete video for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error creating video: {ex.Message}");
            }
        }

        // POST: api/{tenantName}/materials/checklist-complete
        [HttpPost("checklist-complete")]
        public async Task<ActionResult<ChecklistMaterial>> CreateCompleteChecklist(
            string tenantName,
            [FromBody] CompleteChecklistRequest request)
        {
            _logger.LogInformation("Creating complete checklist {Name} with {EntryCount} entries for tenant: {TenantName}",
                request.Checklist.Name, request.Entries?.Count ?? 0, tenantName);

            try
            {
                var createdMaterial = await _materialService.CreateChecklistWithEntriesAsync(
                    request.Checklist,
                    request.Entries);

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    createdMaterial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating complete checklist for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error creating checklist: {ex.Message}");
            }
        }

        // GET: api/{tenantName}/materials/videos/5/with-timestamps
        [HttpGet("videos/{id}/with-timestamps")]
        public async Task<ActionResult<VideoMaterial>> GetVideoWithTimestamps(string tenantName, int id)
        {
            _logger.LogInformation("🎥 Getting video material {Id} with timestamps for tenant: {TenantName}",
                id, tenantName);

            var video = await _materialService.GetVideoMaterialWithTimestampsAsync(id);

            if (video == null)
            {
                _logger.LogWarning("Video material {Id} not found in tenant: {TenantName}", id, tenantName);
                return NotFound();
            }

            _logger.LogInformation("Retrieved video material {Id} with {Count} timestamps for tenant: {TenantName}",
                id, video.Timestamps?.Count() ?? 0, tenantName);

            return Ok(video);
        }

        // POST: api/{tenantName}/materials/videos/5/timestamps
        [HttpPost("videos/{videoId}/timestamps")]
        public async Task<ActionResult<VideoMaterial>> AddTimestampToVideo(string tenantName, int videoId, VideoTimestamp timestamp)
        {
            _logger.LogInformation("Adding timestamp '{Title}' to video {VideoId} for tenant: {TenantName}",
                timestamp.Title, videoId, tenantName);

            try
            {
                var video = await _materialService.AddTimestampToVideoAsync(videoId, timestamp);

                _logger.LogInformation("Added timestamp to video {VideoId} for tenant: {TenantName}",
                    videoId, tenantName);

                return Ok(video);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("{Message} for tenant: {TenantName}", ex.Message, tenantName);
                return NotFound(ex.Message);
            }
        }
        // DELETE: api/{tenantName}/materials/videos/5/timestamps/3
        [HttpDelete("videos/{videoId}/timestamps/{timestampId}")]
        public async Task<IActionResult> RemoveTimestampFromVideo(string tenantName, int videoId, int timestampId)
        {
            _logger.LogInformation("Removing timestamp {TimestampId} from video {VideoId} for tenant: {TenantName}",
                timestampId, videoId, tenantName);

            var removed = await _materialService.RemoveTimestampFromVideoAsync(videoId, timestampId);

            if (!removed)
            {
                return NotFound("Timestamp not found");
            }

            _logger.LogInformation("Removed timestamp {TimestampId} from video {VideoId} for tenant: {TenantName}",
                timestampId, videoId, tenantName);

            return NoContent();
        }


        // GET: api/{tenantName}/materials/checklists/5/with-entries
        [HttpGet("checklists/{id}/with-entries")]
        public async Task<ActionResult<ChecklistMaterial>> GetChecklistWithEntries(string tenantName, int id)
        {
            _logger.LogInformation("Getting checklist material {Id} with entries for tenant: {TenantName}",
                id, tenantName);

            var checklist = await _materialService.GetChecklistMaterialWithEntriesAsync(id);

            if (checklist == null)
            {
                _logger.LogWarning("Checklist material {Id} not found in tenant: {TenantName}", id, tenantName);
                return NotFound();
            }

            _logger.LogInformation("Retrieved checklist material {Id} with {Count} entries for tenant: {TenantName}",
                id, checklist.Entries?.Count() ?? 0, tenantName);

            return Ok(checklist);
        }

        // POST: api/{tenantName}/materials/checklists/5/entries
        [HttpPost("checklists/{checklistId}/entries")]
        public async Task<ActionResult<ChecklistMaterial>> AddEntryToChecklist(string tenantName, int checklistId, ChecklistEntry entry)
        {
            _logger.LogInformation("Adding entry '{Text}' to checklist {ChecklistId} for tenant: {TenantName}",
                entry.Text, checklistId, tenantName);

            try
            {
                var checklist = await _materialService.AddEntryToChecklistAsync(checklistId, entry);

                _logger.LogInformation("Added entry to checklist {ChecklistId} for tenant: {TenantName}",
                    checklistId, tenantName);

                return Ok(checklist);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("{Message} for tenant: {TenantName}", ex.Message, tenantName);
                return NotFound(ex.Message);
            }
        }

        // DELETE: api/{tenantName}/materials/checklists/5/entries/3
        [HttpDelete("checklists/{checklistId}/entries/{entryId}")]
        public async Task<IActionResult> RemoveEntryFromChecklist(string tenantName, int checklistId, int entryId)
        {
            _logger.LogInformation("Removing entry {EntryId} from checklist {ChecklistId} for tenant: {TenantName}",
                entryId, checklistId, tenantName);

            var removed = await _materialService.RemoveEntryFromChecklistAsync(checklistId, entryId);

            if (!removed)
            {
                return NotFound("Entry not found");
            }

            _logger.LogInformation("Removed entry {EntryId} from checklist {ChecklistId} for tenant: {TenantName}",
                entryId, checklistId, tenantName);

            return NoContent();
        }


        // GET: api/{tenantName}/materials/workflows/5/with-steps
        [HttpGet("workflows/{id}/with-steps")]
        public async Task<ActionResult<WorkflowMaterial>> GetWorkflowWithSteps(string tenantName, int id)
        {
            _logger.LogInformation("Getting workflow material {Id} with steps for tenant: {TenantName}",
                id, tenantName);

            var workflow = await _materialService.GetWorkflowMaterialWithStepsAsync(id);

            if (workflow == null)
            {
                _logger.LogWarning("Workflow material {Id} not found in tenant: {TenantName}", id, tenantName);
                return NotFound();
            }

            _logger.LogInformation("Retrieved workflow material {Id} with {Count} steps for tenant: {TenantName}",
                id, workflow.WorkflowSteps?.Count() ?? 0, tenantName);

            return Ok(workflow);
        }

        // POST: api/{tenantName}/materials/workflows/5/steps
        [HttpPost("workflows/{workflowId}/steps")]
        public async Task<ActionResult<WorkflowMaterial>> AddStepToWorkflow(string tenantName, int workflowId, WorkflowStep step)
        {
            _logger.LogInformation("➕ Adding step '{Title}' to workflow {WorkflowId} for tenant: {TenantName}",
                step.Title, workflowId, tenantName);

            try
            {
                var workflow = await _materialService.AddStepToWorkflowAsync(workflowId, step);

                _logger.LogInformation("Added step to workflow {WorkflowId} for tenant: {TenantName}",
                    workflowId, tenantName);

                return Ok(workflow);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("{Message} for tenant: {TenantName}", ex.Message, tenantName);
                return NotFound(ex.Message);
            }
        }

        // DELETE: api/{tenantName}/materials/workflows/5/steps/3
        [HttpDelete("workflows/{workflowId}/steps/{stepId}")]
        public async Task<IActionResult> RemoveStepFromWorkflow(string tenantName, int workflowId, int stepId)
        {
            _logger.LogInformation("Removing step {StepId} from workflow {WorkflowId} for tenant: {TenantName}",
                stepId, workflowId, tenantName);

            var removed = await _materialService.RemoveStepFromWorkflowAsync(workflowId, stepId);

            if (!removed)
            {
                return NotFound("Step not found");
            }

            _logger.LogInformation("Removed step {StepId} from workflow {WorkflowId} for tenant: {TenantName}",
                stepId, workflowId, tenantName);

            return NoContent();
        }

        // GET: api/{tenantName}/materials/by-asset/asset123
        [HttpGet("by-asset/{assetId}")]
        public async Task<ActionResult<IEnumerable<Material>>> GetMaterialsByAsset(string tenantName, int assetId)
        {
            _logger.LogInformation("Getting materials for asset {AssetId} in tenant: {TenantName}",
                assetId, tenantName);

            var materials = await _materialService.GetMaterialsByAssetIdAsync(assetId);

            _logger.LogInformation("Found {Count} materials for asset {AssetId} in tenant: {TenantName}",
                materials.Count(), assetId, tenantName);

            return Ok(materials);
        }

        // GET: api/{tenantName}/materials/5/asset
        [HttpGet("{materialId}/asset")]
        public async Task<ActionResult<object>> GetMaterialAsset(string tenantName, int materialId)
        {
            _logger.LogInformation("Getting asset for material {MaterialId} in tenant: {TenantName}",
                materialId, tenantName);

            var assetId = await _materialService.GetMaterialAssetIdAsync(materialId);

            if (assetId == null)
            {
                return Ok(new { AssetId = (string?)null, Message = "Material does not support assets or has no asset assigned" });
            }

            _logger.LogInformation("Material {MaterialId} has asset {AssetId} in tenant: {TenantName}",
                materialId, assetId, tenantName);

            return Ok(new { AssetId = assetId });
        }

        // POST: api/{tenantName}/materials/5/assign-asset/asset123
        [HttpPost("{materialId}/assign-asset/{assetId}")]
        public async Task<IActionResult> AssignAssetToMaterial(string tenantName, int materialId, int assetId)
        {
            _logger.LogInformation("Assigning asset {AssetId} to material {MaterialId} for tenant: {TenantName}",
                assetId, materialId, tenantName);

            var success = await _materialService.AssignAssetToMaterialAsync(materialId, assetId);

            if (!success)
            {
                return BadRequest("Material not found or material type does not support assets");
            }

            _logger.LogInformation("Assigned asset {AssetId} to material {MaterialId} for tenant: {TenantName}",
                assetId, materialId, tenantName);

            return Ok(new { Message = "Asset successfully assigned to material" });
        }

        // DELETE: api/{tenantName}/materials/5/remove-asset
        [HttpDelete("{materialId}/remove-asset")]
        public async Task<IActionResult> RemoveAssetFromMaterial(string tenantName, int materialId)
        {
            _logger.LogInformation("Removing asset from material {MaterialId} for tenant: {TenantName}",
                materialId, tenantName);

            var success = await _materialService.RemoveAssetFromMaterialAsync(materialId);

            if (!success)
            {
                return BadRequest("Material not found or material type does not support assets");
            }

            _logger.LogInformation("Removed asset from material {MaterialId} for tenant: {TenantName}",
                materialId, tenantName);

            return Ok(new { Message = "Asset successfully removed from material" });
        }

        // GET: api/{tenantName}/materials/5/relationships
        [HttpGet("{materialId}/relationships")]
        public async Task<ActionResult<IEnumerable<MaterialRelationship>>> GetMaterialRelationships(string tenantName, int materialId)
        {
            _logger.LogInformation("Getting relationships for material {MaterialId} in tenant: {TenantName}",
                materialId, tenantName);

            var relationships = await _materialService.GetMaterialRelationshipsAsync(materialId);

            _logger.LogInformation("Found {Count} relationships for material {MaterialId} in tenant: {TenantName}",
                relationships.Count(), materialId, tenantName);

            return Ok(relationships);
        }

        // POST: api/{tenantName}/materials/5/assign-learningpath/3
        [HttpPost("{materialId}/assign-learningpath/{learningPathId}")]
        public async Task<ActionResult<object>> AssignMaterialToLearningPath(
            string tenantName, int materialId, int learningPathId, [FromQuery] string relationshipType = "contains", [FromQuery] int? displayOrder = null)
        {
            _logger.LogInformation("Assigning material {MaterialId} to learning path {LearningPathId} for tenant: {TenantName}",
                materialId, learningPathId, tenantName);

            var relationshipId = await _materialService.AssignMaterialToLearningPathAsync(materialId, learningPathId, relationshipType, displayOrder);

            _logger.LogInformation("Assigned material {MaterialId} to learning path {LearningPathId} (Relationship: {RelationshipId}) for tenant: {TenantName}",
                materialId, learningPathId, relationshipId, tenantName);

            return Ok(new
            {
                Message = "Material successfully assigned to learning path",
                RelationshipId = relationshipId,
                RelationshipType = relationshipType,
                DisplayOrder = displayOrder
            });
        }

        // DELETE: api/{tenantName}/materials/5/remove-learningpath/3
        [HttpDelete("{materialId}/remove-learningpath/{learningPathId}")]
        public async Task<IActionResult> RemoveMaterialFromLearningPath(string tenantName, int materialId, int learningPathId)
        {
            _logger.LogInformation("Removing material {MaterialId} from learning path {LearningPathId} for tenant: {TenantName}",
                materialId, learningPathId, tenantName);

            var success = await _materialService.RemoveMaterialFromLearningPathAsync(materialId, learningPathId);

            if (!success)
            {
                return NotFound("Relationship not found");
            }

            _logger.LogInformation("Removed material {MaterialId} from learning path {LearningPathId} for tenant: {TenantName}",
                materialId, learningPathId, tenantName);

            return Ok(new { Message = "Material successfully removed from learning path" });
        }

        // POST: api/{tenantName}/materials/5/assign-material/8
        [HttpPost("{parentMaterialId}/assign-material/{childMaterialId}")]
        public async Task<ActionResult<object>> AssignMaterialToMaterial(
            string tenantName, int parentMaterialId, int childMaterialId,
            [FromQuery] string relationshipType = "contains",
            [FromQuery] int? displayOrder = null)
        {
            _logger.LogInformation("Assigning material {ChildId} to material {ParentId} for tenant: {TenantName}",
                childMaterialId, parentMaterialId, tenantName);

            try
            {
                var relationshipId = await _materialService.AssignMaterialToMaterialAsync(
                    parentMaterialId, childMaterialId, relationshipType, displayOrder);

                _logger.LogInformation("Assigned material {ChildId} to material {ParentId} (Relationship: {RelationshipId}) for tenant: {TenantName}",
                    childMaterialId, parentMaterialId, relationshipId, tenantName);

                return Ok(new
                {
                    Message = "Material successfully assigned to parent material",
                    RelationshipId = relationshipId,
                    RelationshipType = relationshipType,
                    DisplayOrder = displayOrder
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Failed to assign material {ChildId} to material {ParentId}",
                    childMaterialId, parentMaterialId);
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation assigning material {ChildId} to material {ParentId}",
                    childMaterialId, parentMaterialId);
                return BadRequest(ex.Message);
            }
        }

        // DELETE: api/{tenantName}/materials/5/remove-material/8
        [HttpDelete("{parentMaterialId}/remove-material/{childMaterialId}")]
        public async Task<IActionResult> RemoveMaterialFromMaterial(
            string tenantName, int parentMaterialId, int childMaterialId)
        {
            _logger.LogInformation("Removing material {ChildId} from material {ParentId} for tenant: {TenantName}",
                childMaterialId, parentMaterialId, tenantName);

            var success = await _materialService.RemoveMaterialFromMaterialAsync(parentMaterialId, childMaterialId);

            if (!success)
            {
                return NotFound("Relationship not found");
            }

            _logger.LogInformation("Removed material {ChildId} from material {ParentId} for tenant: {TenantName}",
                childMaterialId, parentMaterialId, tenantName);

            return Ok(new { Message = "Material successfully removed from parent material" });
        }

        // GET: api/{tenantName}/materials/5/children
        [HttpGet("{materialId}/children")]
        public async Task<ActionResult<IEnumerable<object>>> GetChildMaterials(
            string tenantName, int materialId,
            [FromQuery] bool includeOrder = true,
            [FromQuery] string? relationshipType = null)
        {
            _logger.LogInformation("Getting child materials for material {MaterialId} in tenant: {TenantName}",
                materialId, tenantName);

            var children = await _materialService.GetChildMaterialsAsync(materialId, includeOrder, relationshipType);

            var result = children.Select(m => new
            {
                id = m.id,
                Name = m.Name,
                Description = m.Description,
                Type = GetLowercaseType(m.Type)
            });

            _logger.LogInformation("Found {Count} child materials for material {MaterialId} in tenant: {TenantName}",
                result.Count(), materialId, tenantName);

            return Ok(result);
        }

        // GET: api/{tenantName}/materials/5/parents
        [HttpGet("{materialId}/parents")]
        public async Task<ActionResult<IEnumerable<object>>> GetParentMaterials(
            string tenantName, int materialId,
            [FromQuery] string? relationshipType = null)
        {
            _logger.LogInformation("Getting parent materials for material {MaterialId} in tenant: {TenantName}",
                materialId, tenantName);

            var parents = await _materialService.GetParentMaterialsAsync(materialId, relationshipType);

            var result = parents.Select(m => new
            {
                id = m.id,
                Name = m.Name,
                Description = m.Description,
                Type = GetLowercaseType(m.Type)
            });

            _logger.LogInformation("Found {Count} parent materials for material {MaterialId} in tenant: {TenantName}",
                result.Count(), materialId, tenantName);

            return Ok(result);
        }

        // GET: api/{tenantName}/materials/5/hierarchy
        [HttpGet("{materialId}/hierarchy")]
        public async Task<ActionResult<object>> GetMaterialHierarchy(
            string tenantName, int materialId,
            [FromQuery] int maxDepth = 5)
        {
            _logger.LogInformation("Getting hierarchy for material {MaterialId} in tenant: {TenantName}",
                materialId, tenantName);

            try
            {
                var hierarchy = await _materialService.GetMaterialHierarchyAsync(materialId, maxDepth);

                var result = new
                {
                    RootMaterial = new
                    {
                        id = hierarchy.RootMaterial.id,
                        Name = hierarchy.RootMaterial.Name,
                        Description = hierarchy.RootMaterial.Description,
                        Type = GetLowercaseType(hierarchy.RootMaterial.Type)
                    },
                    Children = MapHierarchyNodes(hierarchy.Children),
                    TotalDepth = hierarchy.TotalDepth,
                    TotalMaterials = hierarchy.TotalMaterials
                };

                _logger.LogInformation("Retrieved hierarchy for material {MaterialId} with {TotalMaterials} materials at {TotalDepth} depth levels",
                    materialId, hierarchy.TotalMaterials, hierarchy.TotalDepth);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Material {MaterialId} not found", materialId);
                return NotFound(ex.Message);
            }
        }

        private List<object> MapHierarchyNodes(List<MaterialHierarchyNode> nodes)
        {
            return nodes.Select(node => (object)new
            {
                Material = new
                {
                    id = node.Material.id,
                    Name = node.Material.Name,
                    Description = node.Material.Description,
                    Type = GetLowercaseType(node.Material.Type)
                },
                RelationshipType = node.RelationshipType,
                DisplayOrder = node.DisplayOrder,
                Depth = node.Depth,
                Children = MapHierarchyNodes(node.Children)
            }).ToList();
        }

        private async Task<List<object>> GetRelatedMaterialsAsync(int materialId)
        {
            var childMaterials = await _materialService.GetChildMaterialsAsync(materialId, includeOrder: true);

            return childMaterials.Select(m => new
            {
                id = m.id.ToString(),
                Name = m.Name,
                Description = m.Description
            }).ToList<object>();
        }

        private async Task<List<object>> GetSubcomponentRelatedMaterialsAsync(int subcomponentId, string subcomponentType)
        {
            var relatedMaterials = await _materialService.GetMaterialsForSubcomponentAsync(subcomponentId, subcomponentType, includeOrder: true);

            return relatedMaterials.Select(m => new
            {
                id = m.id.ToString(),
                name = m.Name,
                description = m.Description
            }).ToList<object>();
        }

        private async Task<List<object>> GetAnnotationRelatedMaterialsAsync(int annotationId)
        {
            var relatedMaterials = await _materialService.GetMaterialsForSubcomponentAsync(annotationId, "ImageAnnotation", includeOrder: true);

            return relatedMaterials.Select(m => new
            {
                id = m.id.ToString(),
                name = m.Name,
                description = m.Description
            }).ToList<object>();
        }

        private async Task ProcessRelatedMaterialsAsync(int parentMaterialId, JsonElement jsonElement)
        {
            // Check for 'related' array in the JSON
            if (!TryGetPropertyCaseInsensitive(jsonElement, "related", out var relatedElement))
                return;

            if (relatedElement.ValueKind != JsonValueKind.Array)
                return;

            _logger.LogInformation("Processing {Count} related materials for parent material {ParentId}",
                relatedElement.GetArrayLength(), parentMaterialId);

            int displayOrder = 1;
            foreach (var relatedItem in relatedElement.EnumerateArray())
            {
                // Get the child material ID
                if (!TryGetPropertyCaseInsensitive(relatedItem, "id", out var idProp))
                {
                    _logger.LogWarning("Related material missing 'id' property, skipping");
                    continue;
                }

                int childMaterialId;
                if (idProp.ValueKind == JsonValueKind.String)
                {
                    if (!int.TryParse(idProp.GetString(), out childMaterialId))
                    {
                        _logger.LogWarning("Related material has invalid 'id' value: {Id}, skipping",
                            idProp.GetString());
                        continue;
                    }
                }
                else if (idProp.ValueKind == JsonValueKind.Number)
                {
                    childMaterialId = idProp.GetInt32();
                }
                else
                {
                    _logger.LogWarning("Related material 'id' must be string or number, skipping");
                    continue;
                }

                try
                {
                    // Assign the relationship
                    await _materialService.AssignMaterialToMaterialAsync(
                        parentMaterialId,
                        childMaterialId,
                        relationshipType: "contains",
                        displayOrder: displayOrder);

                    _logger.LogInformation("Assigned material {ChildId} to material {ParentId} with order {Order}",
                        childMaterialId, parentMaterialId, displayOrder);

                    displayOrder++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to assign material {ChildId} to material {ParentId}, continuing with others",
                        childMaterialId, parentMaterialId);
                    // Continue processing other relationships even if one fails
                }
            }
        }

        /// <summary>
        /// Processes related materials for subcomponents (checklist entries, workflow steps, etc.)
        /// after they have been created and have IDs assigned.
        /// </summary>
        /// <typeparam name="T">The type of subcomponent (e.g., ChecklistEntry, WorkflowStep)</typeparam>
        /// <param name="subcomponentType">The subcomponent type string for the relationship table</param>
        /// <param name="subcomponents">List of created subcomponents with their IDs</param>
        /// <param name="relatedMaterialsMap">Dictionary mapping subcomponent index to list of related material IDs</param>
        private async Task ProcessSubcomponentRelatedMaterialsAsync<T>(
            string subcomponentType,
            List<T> subcomponents,
            Dictionary<int, List<int>> relatedMaterialsMap,
            Func<T, int> getIdFunc)
        {
            if (relatedMaterialsMap == null || !relatedMaterialsMap.Any())
                return;

            _logger.LogInformation("Processing related materials for {Count} {Type} subcomponents",
                relatedMaterialsMap.Count, subcomponentType);

            foreach (var kvp in relatedMaterialsMap)
            {
                int index = kvp.Key;
                var relatedMaterialIds = kvp.Value;

                if (index >= subcomponents.Count)
                {
                    _logger.LogWarning("Subcomponent index {Index} out of range, skipping", index);
                    continue;
                }

                var subcomponent = subcomponents[index];
                int subcomponentId = getIdFunc(subcomponent);

                int displayOrder = 1;
                foreach (var relatedMaterialId in relatedMaterialIds)
                {
                    try
                    {
                        await _materialService.AssignMaterialToSubcomponentAsync(
                            subcomponentId, subcomponentType, relatedMaterialId, "related", displayOrder);

                        _logger.LogInformation("Assigned material {MaterialId} to {SubcomponentType} {SubcomponentId}",
                            relatedMaterialId, subcomponentType, subcomponentId);

                        displayOrder++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to assign material {MaterialId} to {SubcomponentType} {SubcomponentId}",
                            relatedMaterialId, subcomponentType, subcomponentId);
                    }
                }
            }
        }

        /// <summary>
        /// Processes related materials for quiz answers after the quiz has been created.
        /// </summary>
        /// <param name="questions">List of created questions with their answers (IDs populated after save)</param>
        /// <param name="answerRelatedMaterials">Dictionary mapping (questionIndex, answerIndex) to list of related material IDs</param>
        private async Task ProcessQuizAnswerRelatedMaterialsAsync(
            List<QuizQuestion> questions,
            Dictionary<(int questionIndex, int answerIndex), List<int>> answerRelatedMaterials)
        {
            if (answerRelatedMaterials == null || !answerRelatedMaterials.Any())
                return;

            _logger.LogInformation("Processing related materials for {Count} quiz answers",
                answerRelatedMaterials.Count);

            foreach (var kvp in answerRelatedMaterials)
            {
                var (questionIndex, answerIndex) = kvp.Key;
                var relatedMaterialIds = kvp.Value;

                if (questionIndex >= questions.Count)
                {
                    _logger.LogWarning("Question index {Index} out of range, skipping answer related materials", questionIndex);
                    continue;
                }

                var question = questions[questionIndex];
                if (question.Answers == null || answerIndex >= question.Answers.Count)
                {
                    _logger.LogWarning("Answer index {AnswerIndex} out of range for question {QuestionIndex}, skipping", answerIndex, questionIndex);
                    continue;
                }

                var answer = question.Answers[answerIndex];
                int displayOrder = 1;

                foreach (var relatedMaterialId in relatedMaterialIds)
                {
                    try
                    {
                        await _materialService.AssignMaterialToSubcomponentAsync(
                            answer.QuizAnswerId, "QuizAnswer", relatedMaterialId, "related", displayOrder);

                        _logger.LogInformation("Assigned material {MaterialId} to QuizAnswer {AnswerId}",
                            relatedMaterialId, answer.QuizAnswerId);

                        displayOrder++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to assign material {MaterialId} to QuizAnswer {AnswerId}",
                            relatedMaterialId, answer.QuizAnswerId);
                    }
                }
            }
        }

        /// <summary>
        /// Processes related materials for all subcomponent types after a material update.
        /// </summary>
        private async Task ProcessSubcomponentRelatedMaterialsForUpdateAsync(Material material, JsonElement jsonElement)
        {
            switch (material)
            {
                case ChecklistMaterial checklist when checklist.Entries?.Any() == true:
                    await ProcessChecklistEntryRelatedMaterialsAsync(jsonElement, checklist.Entries.ToList());
                    break;

                case QuestionnaireMaterial questionnaire when questionnaire.QuestionnaireEntries?.Any() == true:
                    await ProcessQuestionnaireEntryRelatedMaterialsAsync(jsonElement, questionnaire.QuestionnaireEntries.ToList());
                    break;

                case WorkflowMaterial workflow when workflow.WorkflowSteps?.Any() == true:
                    await ProcessWorkflowStepRelatedMaterialsAsync(jsonElement, workflow.WorkflowSteps.ToList());
                    break;

                case VideoMaterial video when video.Timestamps?.Any() == true:
                    await ProcessVideoTimestampRelatedMaterialsAsync(jsonElement, video.Timestamps.ToList());
                    break;

                case ImageMaterial image:
                    // Re-fetch the image with annotations to ensure we have the database-assigned annotation IDs
                    // (the original material may not have the navigation property populated after save)
                    _logger.LogInformation("🖼️ Processing ImageMaterial {Id} - checking for annotations", image.id);
                    var freshImage = await _materialService.GetImageMaterialAsync(image.id);
                    _logger.LogInformation("🖼️ Fresh image has {Count} annotations", freshImage?.ImageAnnotations?.Count ?? 0);
                    if (freshImage?.ImageAnnotations?.Any() == true)
                    {
                        await ProcessImageAnnotationRelatedMaterialsAsync(jsonElement, freshImage.ImageAnnotations.ToList());
                    }
                    break;

                case QuizMaterial quiz when quiz.Questions?.Any() == true:
                    await ProcessQuizQuestionRelatedMaterialsAsync(jsonElement, quiz.Questions.ToList());
                    break;
            }
        }

        /// <summary>
        /// Processes related materials for quiz questions and their answers after update.
        /// </summary>
        private async Task ProcessQuizQuestionRelatedMaterialsAsync(JsonElement jsonElement, List<QuizQuestion> savedQuestions)
        {
            var questionsElement = GetEntriesElementFromJson(jsonElement, "questions");
            if (!questionsElement.HasValue) return;

            int questionIndex = 0;
            foreach (var questionElement in questionsElement.Value.EnumerateArray())
            {
                // Process related materials for the question itself
                var questionRelatedIds = ParseRelatedMaterialIds(questionElement);
                if (questionRelatedIds.Any() && questionIndex < savedQuestions.Count)
                {
                    var question = savedQuestions[questionIndex];
                    await AssignRelatedMaterialsToSubcomponentAsync(question.QuizQuestionId, "QuizQuestion", questionRelatedIds);
                }

                // Process related materials for answers within this question
                if (questionIndex < savedQuestions.Count &&
                    TryGetPropertyCaseInsensitive(questionElement, "answers", out var answersElement) &&
                    answersElement.ValueKind == JsonValueKind.Array)
                {
                    var question = savedQuestions[questionIndex];
                    if (question.Answers != null)
                    {
                        int answerIndex = 0;
                        foreach (var answerElement in answersElement.EnumerateArray())
                        {
                            var answerRelatedIds = ParseRelatedMaterialIds(answerElement);
                            if (answerRelatedIds.Any() && answerIndex < question.Answers.Count)
                            {
                                var answer = question.Answers[answerIndex];
                                await AssignRelatedMaterialsToSubcomponentAsync(answer.QuizAnswerId, "QuizAnswer", answerRelatedIds);
                            }
                            answerIndex++;
                        }
                    }
                }

                questionIndex++;
            }
        }

        /// <summary>
        /// Processes related materials for checklist entries after update.
        /// </summary>
        private async Task ProcessChecklistEntryRelatedMaterialsAsync(JsonElement jsonElement, List<ChecklistEntry> savedEntries)
        {
            var entriesElement = GetEntriesElementFromJson(jsonElement, "entries");
            if (!entriesElement.HasValue) return;

            int entryIndex = 0;
            foreach (var entryElement in entriesElement.Value.EnumerateArray())
            {
                var relatedIds = ParseRelatedMaterialIds(entryElement);
                if (relatedIds.Any() && entryIndex < savedEntries.Count)
                {
                    var entry = savedEntries[entryIndex];
                    await AssignRelatedMaterialsToSubcomponentAsync(entry.ChecklistEntryId, "ChecklistEntry", relatedIds);
                }
                entryIndex++;
            }
        }

        /// <summary>
        /// Processes related materials for questionnaire entries after update.
        /// </summary>
        private async Task ProcessQuestionnaireEntryRelatedMaterialsAsync(JsonElement jsonElement, List<QuestionnaireEntry> savedEntries)
        {
            var entriesElement = GetEntriesElementFromJson(jsonElement, "entries");
            if (!entriesElement.HasValue) return;

            int entryIndex = 0;
            foreach (var entryElement in entriesElement.Value.EnumerateArray())
            {
                var relatedIds = ParseRelatedMaterialIds(entryElement);
                if (relatedIds.Any() && entryIndex < savedEntries.Count)
                {
                    var entry = savedEntries[entryIndex];
                    await AssignRelatedMaterialsToSubcomponentAsync(entry.QuestionnaireEntryId, "QuestionnaireEntry", relatedIds);
                }
                entryIndex++;
            }
        }

        /// <summary>
        /// Processes related materials for workflow steps after update.
        /// </summary>
        private async Task ProcessWorkflowStepRelatedMaterialsAsync(JsonElement jsonElement, List<WorkflowStep> savedSteps)
        {
            var stepsElement = GetEntriesElementFromJson(jsonElement, "steps");
            if (!stepsElement.HasValue) return;

            int stepIndex = 0;
            foreach (var stepElement in stepsElement.Value.EnumerateArray())
            {
                var relatedIds = ParseRelatedMaterialIds(stepElement);
                if (relatedIds.Any() && stepIndex < savedSteps.Count)
                {
                    var step = savedSteps[stepIndex];
                    await AssignRelatedMaterialsToSubcomponentAsync(step.Id, "WorkflowStep", relatedIds);
                }
                stepIndex++;
            }
        }

        /// <summary>
        /// Processes related materials for video timestamps after update.
        /// </summary>
        private async Task ProcessVideoTimestampRelatedMaterialsAsync(JsonElement jsonElement, List<VideoTimestamp> savedTimestamps)
        {
            var timestampsElement = GetEntriesElementFromJson(jsonElement, "timestamps");
            if (!timestampsElement.HasValue) return;

            int timestampIndex = 0;
            foreach (var timestampElement in timestampsElement.Value.EnumerateArray())
            {
                var relatedIds = ParseRelatedMaterialIds(timestampElement);
                if (relatedIds.Any() && timestampIndex < savedTimestamps.Count)
                {
                    var timestamp = savedTimestamps[timestampIndex];
                    await AssignRelatedMaterialsToSubcomponentAsync(timestamp.id, "VideoTimestamp", relatedIds);
                }
                timestampIndex++;
            }
        }

        /// <summary>
        /// Helper to get entries/steps/timestamps element from JSON (checks both root and config).
        /// </summary>
        private JsonElement? GetEntriesElementFromJson(JsonElement jsonElement, string propertyName)
        {
            // Try root level first
            if (TryGetPropertyCaseInsensitive(jsonElement, propertyName, out var rootElement) &&
                rootElement.ValueKind == JsonValueKind.Array)
            {
                return rootElement;
            }

            // Try inside config object
            if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configElement) &&
                TryGetPropertyCaseInsensitive(configElement, propertyName, out var configEntries) &&
                configEntries.ValueKind == JsonValueKind.Array)
            {
                return configEntries;
            }

            return null;
        }

        /// <summary>
        /// Helper to assign related materials to a subcomponent.
        /// </summary>
        private async Task AssignRelatedMaterialsToSubcomponentAsync(int subcomponentId, string subcomponentType, List<int> relatedMaterialIds)
        {
            _logger.LogInformation("Assigning {Count} related materials to {Type} {Id}",
                relatedMaterialIds.Count, subcomponentType, subcomponentId);

            int displayOrder = 1;
            foreach (var relatedMaterialId in relatedMaterialIds)
            {
                try
                {
                    await _materialService.AssignMaterialToSubcomponentAsync(
                        subcomponentId, subcomponentType, relatedMaterialId, "related", displayOrder);

                    _logger.LogInformation("Assigned material {MaterialId} to {SubcomponentType} {SubcomponentId}",
                        relatedMaterialId, subcomponentType, subcomponentId);

                    displayOrder++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to assign material {MaterialId} to {SubcomponentType} {SubcomponentId}",
                        relatedMaterialId, subcomponentType, subcomponentId);
                }
            }
        }

        /// <summary>
        /// Processes related materials for image annotations after the image material has been saved.
        /// Matches annotations from JSON with saved annotations by ClientId or index.
        /// </summary>
        private async Task ProcessImageAnnotationRelatedMaterialsAsync(JsonElement jsonElement, List<ImageAnnotation> savedAnnotations)
        {
            _logger.LogInformation("🖼️ ProcessImageAnnotationRelatedMaterialsAsync called with {Count} saved annotations", savedAnnotations.Count);

            // Find annotations in JSON
            JsonElement? annotationsElement = null;

            if (TryGetPropertyCaseInsensitive(jsonElement, "annotations", out var rootAnnotations) &&
                rootAnnotations.ValueKind == JsonValueKind.Array)
            {
                _logger.LogInformation("🖼️ Found annotations at root level");
                annotationsElement = rootAnnotations;
            }
            else if (TryGetPropertyCaseInsensitive(jsonElement, "config", out var configElement) &&
                     TryGetPropertyCaseInsensitive(configElement, "annotations", out var configAnnotations) &&
                     configAnnotations.ValueKind == JsonValueKind.Array)
            {
                _logger.LogInformation("🖼️ Found annotations in config object");
                annotationsElement = configAnnotations;
            }

            if (!annotationsElement.HasValue)
            {
                _logger.LogWarning("🖼️ No annotations element found in JSON");
                return;
            }

            var annotationRelatedMaterials = new Dictionary<int, List<int>>();
            int annotationIndex = 0;

            foreach (var annotationElement in annotationsElement.Value.EnumerateArray())
            {
                var relatedIds = ParseRelatedMaterialIds(annotationElement);
                _logger.LogInformation("🖼️ Annotation {Index}: found {Count} related material IDs: [{Ids}]",
                    annotationIndex, relatedIds.Count, string.Join(", ", relatedIds));

                if (relatedIds.Any())
                {
                    // Try to match by ClientId first
                    string? clientId = null;
                    if (TryGetPropertyCaseInsensitive(annotationElement, "id", out var clientIdProp) ||
                        TryGetPropertyCaseInsensitive(annotationElement, "clientId", out clientIdProp))
                    {
                        clientId = clientIdProp.GetString();
                    }
                    _logger.LogInformation("🖼️ Looking for annotation with ClientId: {ClientId}", clientId);

                    // Find matching saved annotation
                    ImageAnnotation? matchedAnnotation = null;
                    if (!string.IsNullOrEmpty(clientId))
                    {
                        matchedAnnotation = savedAnnotations.FirstOrDefault(a => a.ClientId == clientId);
                        _logger.LogInformation("🖼️ ClientId match result: {Found}", matchedAnnotation != null);
                    }

                    // Fall back to index matching
                    if (matchedAnnotation == null && annotationIndex < savedAnnotations.Count)
                    {
                        matchedAnnotation = savedAnnotations[annotationIndex];
                        _logger.LogInformation("🖼️ Using index fallback, matched annotation {Id}", matchedAnnotation?.ImageAnnotationId);
                    }

                    if (matchedAnnotation != null)
                    {
                        _logger.LogInformation("🖼️ Matched annotation: Id={Id}, ClientId={ClientId}",
                            matchedAnnotation.ImageAnnotationId, matchedAnnotation.ClientId);

                        int displayOrder = 1;
                        foreach (var relatedMaterialId in relatedIds)
                        {
                            try
                            {
                                await _materialService.AssignMaterialToSubcomponentAsync(
                                    matchedAnnotation.ImageAnnotationId, "ImageAnnotation", relatedMaterialId, "related", displayOrder);

                                _logger.LogInformation("Assigned material {MaterialId} to ImageAnnotation {AnnotationId}",
                                    relatedMaterialId, matchedAnnotation.ImageAnnotationId);

                                displayOrder++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning("Failed to assign material {MaterialId} to ImageAnnotation {AnnotationId}: {Error}",
                                    relatedMaterialId, matchedAnnotation.ImageAnnotationId, ex.Message);
                            }
                        }
                    }
                }

                annotationIndex++;
            }
        }

        /// <summary>
        /// Parses related material IDs from a JSON element's 'related' array.
        /// Accepts both array of objects with 'id' property: [{"id": 1}, {"id": 2}]
        /// and array of plain IDs: [1, 2] or ["1", "2"]
        /// </summary>
        private List<int> ParseRelatedMaterialIds(JsonElement element)
        {
            var result = new List<int>();

            if (!TryGetPropertyCaseInsensitive(element, "related", out var relatedElement))
                return result;

            if (relatedElement.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var relatedItem in relatedElement.EnumerateArray())
            {
                int materialId;

                // Handle plain number: [1, 2, 3]
                if (relatedItem.ValueKind == JsonValueKind.Number)
                {
                    result.Add(relatedItem.GetInt32());
                    continue;
                }

                // Handle plain string: ["1", "2", "3"]
                if (relatedItem.ValueKind == JsonValueKind.String)
                {
                    if (int.TryParse(relatedItem.GetString(), out materialId))
                        result.Add(materialId);
                    continue;
                }

                // Handle object with id: [{"id": 1}, {"id": 2}]
                if (relatedItem.ValueKind != JsonValueKind.Object)
                    continue;

                if (!TryGetPropertyCaseInsensitive(relatedItem, "id", out var idProp))
                    continue;

                if (idProp.ValueKind == JsonValueKind.String)
                {
                    if (!int.TryParse(idProp.GetString(), out materialId))
                        continue;
                }
                else if (idProp.ValueKind == JsonValueKind.Number)
                {
                    materialId = idProp.GetInt32();
                }
                else
                {
                    continue;
                }

                result.Add(materialId);
            }

            return result;
        }

        #region Subcomponent Material Relationships

        // POST: api/{tenantName}/materials/subcomponent/{subcomponentType}/{subcomponentId}/assign-material/{materialId}
        [HttpPost("subcomponent/{subcomponentType}/{subcomponentId}/assign-material/{materialId}")]
        public async Task<ActionResult<object>> AssignMaterialToSubcomponent(
            string tenantName,
            string subcomponentType,
            int subcomponentId,
            int materialId,
            [FromQuery] string? relationshipType = null,
            [FromQuery] int? displayOrder = null)
        {
            _logger.LogInformation("Assigning material {MaterialId} to {SubcomponentType} {SubcomponentId} for tenant: {TenantName}",
                materialId, subcomponentType, subcomponentId, tenantName);

            try
            {
                var relationshipId = await _materialService.AssignMaterialToSubcomponentAsync(
                    subcomponentId, subcomponentType, materialId, relationshipType, displayOrder);

                return Ok(new
                {
                    Message = $"Material successfully assigned to {subcomponentType}",
                    RelationshipId = relationshipId,
                    SubcomponentType = subcomponentType,
                    SubcomponentId = subcomponentId,
                    MaterialId = materialId,
                    RelationshipType = relationshipType,
                    DisplayOrder = displayOrder
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Failed to assign material {MaterialId} to {SubcomponentType} {SubcomponentId}",
                    materialId, subcomponentType, subcomponentId);
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation assigning material {MaterialId} to {SubcomponentType} {SubcomponentId}",
                    materialId, subcomponentType, subcomponentId);
                return BadRequest(ex.Message);
            }
        }

        // DELETE: api/{tenantName}/materials/subcomponent/{subcomponentType}/{subcomponentId}/remove-material/{materialId}
        [HttpDelete("subcomponent/{subcomponentType}/{subcomponentId}/remove-material/{materialId}")]
        public async Task<IActionResult> RemoveMaterialFromSubcomponent(
            string tenantName,
            string subcomponentType,
            int subcomponentId,
            int materialId)
        {
            _logger.LogInformation("Removing material {MaterialId} from {SubcomponentType} {SubcomponentId} for tenant: {TenantName}",
                materialId, subcomponentType, subcomponentId, tenantName);

            var success = await _materialService.RemoveMaterialFromSubcomponentAsync(
                subcomponentId, subcomponentType, materialId);

            if (!success)
            {
                return NotFound("Relationship not found");
            }

            return Ok(new { Message = $"Material successfully removed from {subcomponentType}" });
        }

        // GET: api/{tenantName}/materials/subcomponent/{subcomponentType}/{subcomponentId}/materials
        [HttpGet("subcomponent/{subcomponentType}/{subcomponentId}/materials")]
        public async Task<ActionResult<IEnumerable<object>>> GetMaterialsForSubcomponent(
            string tenantName,
            string subcomponentType,
            int subcomponentId,
            [FromQuery] bool includeOrder = true)
        {
            _logger.LogInformation("Getting materials for {SubcomponentType} {SubcomponentId} in tenant: {TenantName}",
                subcomponentType, subcomponentId, tenantName);

            var materials = await _materialService.GetMaterialsForSubcomponentAsync(
                subcomponentId, subcomponentType, includeOrder);

            var result = materials.Select(m => new
            {
                id = m.id,
                Name = m.Name,
                Description = m.Description,
                Type = GetLowercaseType(m.Type)
            });

            return Ok(result);
        }

        // PUT: api/{tenantName}/materials/subcomponent/{subcomponentType}/{subcomponentId}/reorder-materials
        [HttpPut("subcomponent/{subcomponentType}/{subcomponentId}/reorder-materials")]
        public async Task<IActionResult> ReorderSubcomponentMaterials(
            string tenantName,
            string subcomponentType,
            int subcomponentId,
            [FromBody] Dictionary<int, int> materialOrderMap)
        {
            _logger.LogInformation("Reordering materials for {SubcomponentType} {SubcomponentId} in tenant: {TenantName}",
                subcomponentType, subcomponentId, tenantName);

            var success = await _materialService.ReorderSubcomponentMaterialsAsync(
                subcomponentId, subcomponentType, materialOrderMap);

            if (!success)
            {
                return BadRequest("Failed to reorder materials");
            }

            return Ok(new { Message = "Materials reordered successfully" });
        }

        // GET: api/{tenantName}/materials/subcomponent/{subcomponentType}/{subcomponentId}/relationships
        [HttpGet("subcomponent/{subcomponentType}/{subcomponentId}/relationships")]
        public async Task<ActionResult<IEnumerable<object>>> GetSubcomponentRelationships(
            string tenantName,
            string subcomponentType,
            int subcomponentId)
        {
            _logger.LogInformation("Getting relationships for {SubcomponentType} {SubcomponentId} in tenant: {TenantName}",
                subcomponentType, subcomponentId, tenantName);

            var relationships = await _materialService.GetSubcomponentRelationshipsAsync(
                subcomponentId, subcomponentType);

            var result = relationships.Select(r => new
            {
                RelationshipId = r.Id,
                SubcomponentId = r.SubcomponentId,
                SubcomponentType = r.SubcomponentType,
                MaterialId = r.RelatedMaterialId,
                MaterialName = r.RelatedMaterial?.Name,
                MaterialType = r.RelatedMaterial != null ? GetLowercaseType(r.RelatedMaterial.Type) : null,
                RelationshipType = r.RelationshipType,
                DisplayOrder = r.DisplayOrder
            });

            return Ok(result);
        }

        #endregion

        // GET: api/{tenantName}/materials/summary
        [HttpGet("summary")]
        public async Task<ActionResult<MaterialTypeSummary>> GetMaterialTypeSummary(string tenantName)
        {
            _logger.LogInformation("Getting material type summary for tenant: {TenantName}", tenantName);

            try
            {
                var summary = new MaterialTypeSummary
                {
                    TenantName = tenantName,
                    Videos = (await _materialService.GetAllVideoMaterialsAsync()).Count(),
                    Images = (await _materialService.GetAllImageMaterialsAsync()).Count(),
                    Checklists = (await _materialService.GetAllChecklistMaterialsAsync()).Count(),
                    Workflows = (await _materialService.GetAllWorkflowMaterialsAsync()).Count(),
                    PDFs = (await _materialService.GetAllPDFMaterialsAsync()).Count(),
                    Chatbots = (await _materialService.GetAllChatbotMaterialsAsync()).Count(),
                    Questionnaires = (await _materialService.GetAllQuestionnaireMaterialsAsync()).Count(),
                    MQTTTemplates = (await _materialService.GetAllMQTTTemplateMaterialsAsync()).Count(),
                    Unitys = (await _materialService.GetAllUnityMaterialsAsync()).Count(),
                    Total = (await _materialService.GetAllMaterialsAsync()).Count()
                };

                _logger.LogInformation("Generated material summary for tenant: {TenantName} ({Total} total materials)",
                    tenantName, summary.Total);

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating material summary for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error generating summary: {ex.Message}");
            }
        }

        /// Get all learning paths that contain this material
        
        [HttpGet("{materialId}/learning-paths")]
        public async Task<ActionResult<IEnumerable<LearningPath>>> GetMaterialLearningPaths(
            string tenantName,
            int materialId)
        {
            _logger.LogInformation("Getting learning paths containing material {MaterialId} for tenant: {TenantName}",
                materialId, tenantName);

            // Get relationships where this material is assigned to learning paths
            var relationships = await _materialService.GetRelationshipsByTypeAsync(materialId, "LearningPath");

            // Extract learning path IDs and fetch the actual learning paths
            var LearningPaths = relationships.Select(r => int.Parse(r.RelatedEntityId)).ToList();

            var learningPaths = new List<LearningPath>();
            foreach (var id in LearningPaths)
            {
                var path = await _learningPathService.GetLearningPathAsync(id);
                if (path != null)
                    learningPaths.Add(path);
            }

            _logger.LogInformation("Found {Count} learning paths containing material {MaterialId} for tenant: {TenantName}",
                learningPaths.Count, materialId, tenantName);

            return Ok(learningPaths);
        }

       
        /// Get all training programs that contain this material
        
      /*  [HttpGet("{materialId}/training-programs")]
        public async Task<ActionResult<IEnumerable<TrainingProgram>>> GetMaterialTrainingPrograms(
            string tenantName,
            int materialId)
        {
            _logger.LogInformation("Getting training programs containing material {MaterialId} for tenant: {TenantName}",
                materialId, tenantName);

            var programs = await _materialService.GetTrainingProgramsContainingMaterialAsync(materialId);

            _logger.LogInformation("Found {Count} training programs containing material {MaterialId} for tenant: {TenantName}",
                programs.Count(), materialId, tenantName);

            return Ok(programs);
        }*/

       
        /// Get all relationships for this material
        
        [HttpGet("{materialId}/all-relationships")]
        public async Task<ActionResult<object>> GetMaterialAllRelationships(
            string tenantName,
            int materialId)
        {
            _logger.LogInformation("Getting all relationships for material {MaterialId} for tenant: {TenantName}",
                materialId, tenantName);

            var relationships = await _materialService.GetMaterialRelationshipsAsync(materialId);

            var groupedRelationships = relationships
                .GroupBy(r => r.RelatedEntityType)
                .ToDictionary(g => g.Key, g => g.ToList());

            _logger.LogInformation("Found {Count} total relationships for material {MaterialId} for tenant: {TenantName}",
                relationships.Count(), materialId, tenantName);

            return Ok(new
            {
                MaterialId = materialId,
                TotalRelationships = relationships.Count(),
                RelationshipsByType = groupedRelationships,
                RelationshipTypes = groupedRelationships.Keys.ToList()
            });
        }
        private System.Type GetSystemTypeFromMaterialType(MaterialType materialType)
        {
            return materialType switch
            {
                MaterialType.Video => typeof(VideoMaterial),
                MaterialType.Image => typeof(ImageMaterial),
                MaterialType.PDF => typeof(PDFMaterial),
                MaterialType.Checklist => typeof(ChecklistMaterial),
                MaterialType.Workflow => typeof(WorkflowMaterial),
                MaterialType.Questionnaire => typeof(QuestionnaireMaterial),
                MaterialType.Unity => typeof(UnityMaterial),
                MaterialType.Chatbot => typeof(ChatbotMaterial),
                MaterialType.MQTT_Template => typeof(MQTT_TemplateMaterial),
                MaterialType.Voice => typeof(VoiceMaterial),
                _ => typeof(Material)
            };
        }

        #region Voice Material Endpoints

        /// <summary>
        /// Get all voice materials
        /// </summary>
        [HttpGet("voice")]
        public async Task<ActionResult<IEnumerable<object>>> GetVoiceMaterials(string tenantName)
        {
            _logger.LogInformation("Getting voice materials for tenant: {TenantName}", tenantName);

            try
            {
                var voiceMaterials = await _voiceMaterialService.GetAllAsync();
                var result = new List<object>();

                foreach (var voice in voiceMaterials)
                {
                    result.Add(new
                    {
                        id = voice.id,
                        Name = voice.Name,
                        Description = voice.Description,
                        Type = GetLowercaseType(voice.Type),
                        VoiceStatus = voice.VoiceStatus,
                        AssetIds = voice.GetAssetIdsList(),
                        Created_at = voice.Created_at,
                        Updated_at = voice.Updated_at
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting voice materials");
                return StatusCode(500, new { Error = "Failed to get voice materials", Details = ex.Message });
            }
        }

        /// <summary>
        /// Submit a voice material for AI processing
        /// </summary>
        [HttpPost("{id}/voice/submit")]
        public async Task<ActionResult<object>> SubmitVoiceForProcessing(string tenantName, int id)
        {
            _logger.LogInformation("Submitting voice material {Id} for AI processing in tenant {TenantName}", id, tenantName);

            try
            {
                var voice = await _voiceMaterialService.SubmitForProcessingAsync(id);

                return Ok(new
                {
                    Status = "success",
                    Message = "Voice material submitted for AI processing",
                    MaterialId = voice.id,
                    VoiceStatus = voice.VoiceStatus,
                    AssetIds = voice.GetAssetIdsList()
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { Error = $"Voice material {id} not found" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting voice material {Id} for AI processing", id);
                return StatusCode(500, new { Error = "Failed to submit voice material for AI processing", Details = ex.Message });
            }
        }

        /// <summary>
        /// Refresh the status of a voice material based on its assets
        /// </summary>
        [HttpPost("{id}/voice/refresh-status")]
        public async Task<ActionResult<object>> RefreshVoiceStatus(string tenantName, int id)
        {
            _logger.LogInformation("Refreshing voice material {Id} status in tenant {TenantName}", id, tenantName);

            try
            {
                var voice = await _voiceMaterialService.UpdateStatusFromAssetsAsync(id);

                return Ok(new
                {
                    Status = "success",
                    MaterialId = voice.id,
                    VoiceStatus = voice.VoiceStatus
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { Error = $"Voice material {id} not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing voice material {Id} status", id);
                return StatusCode(500, new { Error = "Failed to refresh voice material status", Details = ex.Message });
            }
        }

        /// <summary>
        /// Add an asset to a voice material
        /// </summary>
        [HttpPost("{id}/voice/assets/{assetId}")]
        public async Task<ActionResult<object>> AddAssetToVoice(string tenantName, int id, int assetId)
        {
            _logger.LogInformation("Adding asset {AssetId} to voice material {Id} in tenant {TenantName}", assetId, id, tenantName);

            try
            {
                var result = await _voiceMaterialService.AddAssetAsync(id, assetId);

                if (!result)
                {
                    return NotFound(new { Error = $"Voice material {id} not found" });
                }

                return Ok(new
                {
                    Status = "success",
                    Message = $"Asset {assetId} added to voice material {id}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding asset {AssetId} to voice material {Id}", assetId, id);
                return StatusCode(500, new { Error = "Failed to add asset to voice material", Details = ex.Message });
            }
        }

        /// <summary>
        /// Remove an asset from a voice material
        /// </summary>
        [HttpDelete("{id}/voice/assets/{assetId}")]
        public async Task<ActionResult<object>> RemoveAssetFromVoice(string tenantName, int id, int assetId)
        {
            _logger.LogInformation("Removing asset {AssetId} from voice material {Id} in tenant {TenantName}", assetId, id, tenantName);

            try
            {
                var result = await _voiceMaterialService.RemoveAssetAsync(id, assetId);

                if (!result)
                {
                    return NotFound(new { Error = $"Voice material {id} not found or asset not in list" });
                }

                return Ok(new
                {
                    Status = "success",
                    Message = $"Asset {assetId} removed from voice material {id}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing asset {AssetId} from voice material {Id}", assetId, id);
                return StatusCode(500, new { Error = "Failed to remove asset from voice material", Details = ex.Message });
            }
        }

        #endregion
    }

        // Supporting DTOs
        public class BulkMaterialAssignment
        {
            public int MaterialId { get; set; }
            public string? RelationshipType { get; set; }
            public int? DisplayOrder { get; set; }
            public string? Notes { get; set; }
        }

        public class BulkAssignmentResult
        {
            public int SuccessfulAssignments { get; set; }
            public int FailedAssignments { get; set; }
            public List<string> Errors { get; set; } = new();
            public List<string> Warnings { get; set; } = new();

    
        }
        public class CompleteWorkflowRequest
        {
            public WorkflowMaterial Workflow { get; set; } = new();
            public List<WorkflowStep>? Steps { get; set; }
        }

        public class CompleteVideoRequest
        {
            public VideoMaterial Video { get; set; } = new();
            public List<VideoTimestamp>? Timestamps { get; set; }
            /// <summary>
            /// Dictionary mapping timestamp index to list of related material IDs.
            /// Example: { 0: [1, 2], 2: [3] } means timestamp at index 0 has related materials 1 and 2,
            /// and timestamp at index 2 has related material 3.
            /// </summary>
            public Dictionary<int, List<int>>? TimestampRelatedMaterials { get; set; }
        }

        public class CompleteChecklistRequest
        {
            public ChecklistMaterial Checklist { get; set; } = new();
            public List<ChecklistEntry>? Entries { get; set; }
        }

        public class MaterialTypeSummary
        {
            public string TenantName { get; set; } = "";
            public int Videos { get; set; }
            public int Images { get; set; }
            public int Checklists { get; set; }
            public int Workflows { get; set; }
            public int PDFs { get; set; }
            public int Chatbots { get; set; }
            public int Questionnaires { get; set; }
            public int MQTTTemplates { get; set; }
            public int Unitys { get; set; }
            public int Total { get; set; }
        }


    }
