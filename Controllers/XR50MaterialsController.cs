using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Controllers
{
     public class FileUploadFormDataWithMaterial
        {
            public string materialData { get; set; }
            public string? assetData { get; set; }
            public IFormFile? File { get; set; }
        }
    [Route("api/{tenantName}/[controller]")]
    [ApiController]
    public class materialsController : ControllerBase
    {
        private readonly IMaterialService _materialService;
        private readonly IAssetService _assetService;
        private readonly ILearningPathService _learningPathService;
        private readonly ILogger<materialsController> _logger;

        public materialsController(
            IMaterialService materialService,
            IAssetService assetService,
            ILearningPathService learningPathService,
            ILogger<materialsController> logger)
        {
            _materialService = materialService;
            _assetService = assetService;
            _learningPathService = learningPathService;
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

    return new
    {
        id = workflow.id.ToString(),
        Name = workflow.Name,
        Description = workflow.Description,
        Type = GetLowercaseType(workflow.Type),
        Created_at = workflow.Created_at,
        Updated_at = workflow.Updated_at,
        Config = new
        {
            Steps = workflow.WorkflowSteps?.Select(ws => new
            {
                Id = ws.Id,
                Title = ws.Title,
                Content = ws.Content
            }) ?? Enumerable.Empty<object>()
        },
        Related = related
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
        id = video.id.ToString(),
        Name = video.Name,
        Description = video.Description,
        Type = GetLowercaseType(video.Type),
        Created_at = video.Created_at,
        Updated_at = video.Updated_at,
        Asset = asset,
        VideoPath = video.VideoPath,
        VideoDuration = video.VideoDuration,
        VideoResolution = video.VideoResolution,
        startTime = video.startTime,
        Annotations = video.Annotations,
        VideoTimestamps = video.VideoTimestamps?.Select(vt => new
        {
            Id = vt.id,
            Title = vt.Title,
            startTime = vt.startTime,
            endTime = vt.endTime,
            Description = vt.Description,
            Type = vt.Type
        }) ?? Enumerable.Empty<object>(),
        Related = related
    };
}

private async Task<object?> GetChecklistDetails(int materialId)
{
    var checklist = await _materialService.GetChecklistMaterialWithEntriesAsync(materialId);
    if (checklist == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = checklist.id.ToString(),
        Name = checklist.Name,
        Description = checklist.Description,
        Type = GetLowercaseType(checklist.Type),
        Created_at = checklist.Created_at,
        Updated_at = checklist.Updated_at,
        Config = new
        {
            Entries = checklist.Entries?.Select(ce => new
            {
                Id = ce.ChecklistEntryId,
                Text = ce.Text,
                Description = ce.Description
            }) ?? Enumerable.Empty<object>()
        },
        Related = related
    };
}

private async Task<object?> GetQuestionnaireDetails(int materialId)
{
    var questionnaire = await _materialService.GetQuestionnaireMaterialWithEntriesAsync(materialId);
    if (questionnaire == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = questionnaire.id.ToString(),
        Name = questionnaire.Name,
        Description = questionnaire.Description,
        Type = GetLowercaseType(questionnaire.Type),
        Created_at = questionnaire.Created_at,
        Updated_at = questionnaire.Updated_at,
        QuestionnaireType = questionnaire.QuestionnaireType,
        PassingScore = questionnaire.PassingScore,
        QuestionnaireConfig = questionnaire.QuestionnaireConfig,
        Config = new
        {
            Entries = questionnaire.QuestionnaireEntries?.Select(qe => new
            {
                Id = qe.QuestionnaireEntryId,
                Text = qe.Text,
                Description = qe.Description
            }) ?? Enumerable.Empty<object>()
        },
        Related = related
    };
}

private async Task<object?> GetQuizDetails(int materialId)
{
    var quiz = await _materialService.GetQuizMaterialWithQuestionsAsync(materialId);
    if (quiz == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = quiz.id.ToString(),
        Name = quiz.Name,
        Description = quiz.Description,
        Type = GetLowercaseType(quiz.Type),
        Created_at = quiz.Created_at,
        Updated_at = quiz.Updated_at,
        Config = new
        {
            Questions = quiz.Questions?.Select(q => new
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
                Answers = q.Answers?.Select(a => new
                {
                    Id = a.QuizAnswerId,
                    Text = a.Text,
                    IsCorrect = a.IsCorrect,
                    DisplayOrder = a.DisplayOrder
                }) ?? Enumerable.Empty<object>()
            }) ?? Enumerable.Empty<object>()
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

    return new
    {
        id = image.id.ToString(),
        Name = image.Name,
        Description = image.Description,
        Type = GetLowercaseType(image.Type),
        Created_at = image.Created_at,
        Updated_at = image.Updated_at,
        Asset = asset,
        ImagePath = image.ImagePath,
        ImageWidth = image.ImageWidth,
        ImageHeight = image.ImageHeight,
        ImageFormat = image.ImageFormat,
        Annotations = image.Annotations,
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
        Created_at = unity.Created_at,
        Updated_at = unity.Updated_at,
        Asset = asset,
        UnityVersion = unity.UnityVersion,
        UnityBuildTarget = unity.UnityBuildTarget,
        UnitySceneName = unity.UnitySceneName,
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
        Created_at = mqtt.Created_at,
        Updated_at = mqtt.Updated_at,
        MessageType = mqtt.message_type,
        MessageText = mqtt.message_text,
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

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = $"Material '{createdMaterial.Name}' created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_Id = createdMaterial.Unique_Id,
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

                    // Process related materials if provided
                    await ProcessRelatedMaterialsAsync(createdMaterial.id, materialData);

                    var response = new CreateMaterialResponse
                    {
                        Status = "success",
                        Message = "Material with asset created successfully",
                        id = createdMaterial.id,
                        Name = createdMaterial.Name,
                        Description = createdMaterial.Description,
                        Type = GetLowercaseType(createdMaterial.Type),
                        Unique_Id = createdMaterial.Unique_Id,
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
        [HttpPost]
        // POST: api/{tenantName}/materials - Unified material creation endpoint
        // Accepts both JSON (application/json) for material-only and multipart/form-data for material with optional file uploads
        // Form-data parameters: material (JSON string, required), file (binary, optional), assetData (JSON string, optional)
        public async Task<ActionResult<CreateMaterialResponse>> PostMaterialDetailed(string tenantName)
        {
            try
            {
                JsonElement materialData;
                IFormFile? file = null;
                JsonElement? assetData = null;

                var contentType = Request.ContentType?.ToLower() ?? "";

                _logger.LogInformation("Received material creation request with Content-Type: {ContentType}", contentType);

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
                        materialData = JsonSerializer.Deserialize<JsonElement>(materialDataString);
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

                    _logger.LogInformation("Parsed form-data request (file: {HasFile})", file != null);
                }
                else if (contentType.Contains("application/json"))
                {
                    // JSON request - material only (no file)
                    using var reader = new StreamReader(Request.Body);
                    var body = await reader.ReadToEndAsync();
                    materialData = JsonSerializer.Deserialize<JsonElement>(body);
                    _logger.LogInformation("Parsed JSON request body");
                }
                else
                {
                    _logger.LogWarning("Unsupported Content-Type: {ContentType}", contentType);
                    return StatusCode(415, $"Unsupported Media Type. Please use 'application/json' or 'multipart/form-data'. Received: {contentType}");
                }

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

                // Parse the steps - try to get from "config" object first, then direct "steps" array
                var steps = new List<WorkflowStep>();

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
                    foreach (var stepElement in stepsElement.EnumerateArray())
                    {
                        var step = new WorkflowStep();

                        if (TryGetPropertyCaseInsensitive(stepElement, "title", out var titleProp))
                            step.Title = titleProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(stepElement, "content", out var contentProp))
                            step.Content = contentProp.GetString();

                        steps.Add(step);
                    }
                }

                _logger.LogInformation("Parsed workflow: {Name} with {StepCount} steps", workflow.Name, steps.Count);

                // Use the service method directly instead of the controller method
                var createdMaterial = await _materialService.CreateWorkflowWithStepsAsync(workflow, steps);

                _logger.LogInformation("Created workflow material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

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
                    Unique_Id = createdMaterial.Unique_Id,
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

                // Parse the timestamps
                var timestamps = new List<VideoTimestamp>();
                if (TryGetPropertyCaseInsensitive(jsonElement, "timestamps", out var timestampsElement) &&
                    timestampsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var timestampElement in timestampsElement.EnumerateArray())
                    {
                        var timestamp = new VideoTimestamp();

                        if (TryGetPropertyCaseInsensitive(timestampElement, "title", out var titleProp))
                            timestamp.Title = titleProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(timestampElement, "startTime", out var startTimePropTs))
                            timestamp.startTime = startTimePropTs.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(timestampElement, "endTime", out var endTimeProp))
                            timestamp.endTime = endTimeProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(timestampElement, "description", out var descriptionProp))
                            timestamp.Description = descriptionProp.GetString();

                        if (TryGetPropertyCaseInsensitive(timestampElement, "type", out var typeProp))
                            timestamp.Type = typeProp.GetString();

                        timestamps.Add(timestamp);
                    }
                }
                
                _logger.LogInformation("🎬 Parsed video: {Name} with {TimestampCount} timestamps", video.Name, timestamps.Count);
                
                // Use the service method directly instead of the controller method
                var createdMaterial = await _materialService.CreateVideoWithTimestampsAsync(video, timestamps);
                
                _logger.LogInformation("Created video material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

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
                    Unique_Id = createdMaterial.Unique_Id,
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

                // Parse the entries - try to get from "config" object first, then direct "entries" array
                var entries = new List<ChecklistEntry>();

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
                    foreach (var entryElement in entriesElement.EnumerateArray())
                    {
                        var entry = new ChecklistEntry();

                        if (TryGetPropertyCaseInsensitive(entryElement, "text", out var textProp))
                            entry.Text = textProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(entryElement, "description", out var descriptionProp))
                            entry.Description = descriptionProp.GetString();

                        entries.Add(entry);
                    }
                }
                
                _logger.LogInformation("Parsed checklist: {Name} with {EntryCount} entries", checklist.Name, entries.Count);
                
                // Use the service method directly instead of the controller method
                var createdMaterial = await _materialService.CreateChecklistWithEntriesAsync(checklist, entries);
                
                _logger.LogInformation("Created checklist material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

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
                    Unique_Id = createdMaterial.Unique_Id,
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
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "questionnaireType", out var typeProp))
                    questionnaire.QuestionnaireType = typeProp.GetString();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "passingScore", out var scoreProp))
                    questionnaire.PassingScore = scoreProp.GetDecimal();
                
                if (TryGetPropertyCaseInsensitive(jsonElement, "questionnaireConfig", out var configProp))
                    questionnaire.QuestionnaireConfig = configProp.GetString();

                // Parse the entries - try to get from "config" object first, then direct "entries" array
                var entries = new List<QuestionnaireEntry>();

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
                    foreach (var entryElement in entriesElement.EnumerateArray())
                    {
                        var entry = new QuestionnaireEntry();

                        if (TryGetPropertyCaseInsensitive(entryElement, "text", out var textProp))
                            entry.Text = textProp.GetString() ?? "";

                        if (TryGetPropertyCaseInsensitive(entryElement, "description", out var descriptionProp))
                            entry.Description = descriptionProp.GetString();

                        entries.Add(entry);
                    }
                }
                
                _logger.LogInformation("Parsed questionnaire: {Name} with {EntryCount} entries", questionnaire.Name, entries.Count);
                
                // For questionnaires, we can use the existing service method directly
                var createdMaterial = await _materialService.CreateQuestionnaireMaterialWithEntriesAsync(questionnaire, entries);
                
                _logger.LogInformation("Created questionnaire material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

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
                    Unique_Id = createdMaterial.Unique_Id,
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
                _logger.LogInformation("📝 Creating quiz material from JSON");

                // Parse the quiz material properties
                var quiz = new QuizMaterial();

                if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
                    quiz.Name = nameProp.GetString();

                if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
                    quiz.Description = descProp.GetString();

                // Parse the questions - try to get from "config" object first, then direct "questions" array
                var questions = new List<QuizQuestion>();

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

                    foreach (var questionElement in questionsElement.EnumerateArray())
                    {
                        var question = new QuizQuestion();

                        if (TryGetPropertyCaseInsensitive(questionElement, "id", out var idProp))
                            question.QuestionNumber = idProp.GetInt32();

                        if (TryGetPropertyCaseInsensitive(questionElement, "type", out var typeProp))
                            question.QuestionType = typeProp.GetString() ?? "text";

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

                        // Handle answers (checking for both "answers" and "anwsers" typo)
                        if (TryGetPropertyCaseInsensitive(questionElement, "answers", out var answersElement) ||
                            TryGetPropertyCaseInsensitive(questionElement, "anwsers", out answersElement))
                        {
                            if (answersElement.ValueKind == JsonValueKind.Array)
                            {
                                var answers = new List<QuizAnswer>();
                                foreach (var answerElement in answersElement.EnumerateArray())
                                {
                                    var answer = new QuizAnswer();

                                    if (TryGetPropertyCaseInsensitive(answerElement, "text", out var ansTextProp))
                                        answer.Text = ansTextProp.GetString() ?? "";

                                    if (TryGetPropertyCaseInsensitive(answerElement, "isCorrect", out var correctProp))
                                        answer.IsCorrect = correctProp.GetBoolean();

                                    if (TryGetPropertyCaseInsensitive(answerElement, "displayOrder", out var orderProp))
                                        answer.DisplayOrder = orderProp.GetInt32();

                                    answers.Add(answer);
                                }
                                question.Answers = answers;
                                _logger.LogInformation("Added {Count} answers to question", answers.Count);
                            }
                        }

                        questions.Add(question);
                    }
                }

                _logger.LogInformation("Parsed quiz: {Name} with {QuestionCount} questions", quiz.Name, questions.Count);

                // Use the service method to create quiz with questions
                var createdMaterial = await _materialService.CreateQuizWithQuestionsAsync(quiz, questions);

                _logger.LogInformation("Created quiz material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

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
                    Unique_Id = createdMaterial.Unique_Id,
                    Created_at = createdMaterial.Created_at
                };

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.id },
                    response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "📝 Error creating quiz from JSON");
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
                
                // Use the basic creation method (not the complete one)
                var createdMaterial = await _materialService.CreateMaterialAsync(material);

                _logger.LogInformation("Created basic material {Name} with ID {Id}",
                    createdMaterial.Name, createdMaterial.id);

                // Process related materials if provided
                await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

                var response = new CreateMaterialResponse
                {
                    Status = "success",
                    Message = "Material created successfully",
                    id = createdMaterial.id,
                    Name = createdMaterial.Name,
                    Description = createdMaterial.Description,
                    Type = GetLowercaseType(createdMaterial.Type),
                    Unique_Id = createdMaterial.Unique_Id,
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
                ("unitydemo", _) or (_, "unitydemo") => new UnityMaterial(),
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

        private void PopulateTypeSpecificProperties(Material material, JsonElement jsonElement)
        {
            _logger.LogInformation(" Populating type-specific properties for {MaterialType}", material.GetType().Name);

            switch (material)
            {
                case WorkflowMaterial workflow:
                    _logger.LogInformation("Processing workflow material...");
                    
                    // Handle workflow steps (case-insensitive)
                    if (TryGetPropertyCaseInsensitive(jsonElement, "steps", out var stepsElement))
                    {
                        _logger.LogInformation("Found steps property, processing...");
                        
                        var steps = new List<WorkflowStep>();
                        if (stepsElement.ValueKind == JsonValueKind.Array)
                        {
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
                        }
                        else
                        {
                            _logger.LogWarning("Steps property is not an array, it's: {ValueKind}", stepsElement.ValueKind);
                        }
                        
                        workflow.WorkflowSteps = steps;
                        _logger.LogInformation("Added {Count} workflow steps", steps.Count);
                    }
                    else
                    {
                        _logger.LogWarning("No 'steps' property found in workflow JSON");
                        // Log all available properties for debugging
                        foreach (var prop in jsonElement.EnumerateObject())
                        {
                            _logger.LogInformation("Available property: {Name} = {Value}", prop.Name, prop.Value);
                        }
                    }
                    break;

                case ChecklistMaterial checklist:
                    // Handle checklist entries (case-insensitive)
                    if (TryGetPropertyCaseInsensitive(jsonElement, "entries", out var entriesElement))
                    {
                        var entries = new List<ChecklistEntry>();
                        if (entriesElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var entryElement in entriesElement.EnumerateArray())
                            {
                                var entry = new ChecklistEntry();

                                if (TryGetPropertyCaseInsensitive(entryElement, "text", out var textProp))
                                    entry.Text = textProp.GetString() ?? "";

                                if (TryGetPropertyCaseInsensitive(entryElement, "description", out var descProp))
                                    entry.Description = descProp.GetString();

                                entries.Add(entry);
                            }
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
                                question.QuestionNumber = idProp.GetInt32();

                            if (TryGetPropertyCaseInsensitive(questionElement, "type", out var typeProp))
                                question.QuestionType = typeProp.GetString() ?? "text";

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
                                question.AllowMultiple = multiProp.GetBoolean();

                            if (TryGetPropertyCaseInsensitive(questionElement, "scaleConfig", out var scaleProp))
                                question.ScaleConfig = scaleProp.GetString();

                            // Handle answers (checking for both "answers" and "anwsers" typo)
                            if (TryGetPropertyCaseInsensitive(questionElement, "answers", out var answersElement) ||
                                TryGetPropertyCaseInsensitive(questionElement, "anwsers", out answersElement))
                            {
                                if (answersElement.ValueKind == JsonValueKind.Array)
                                {
                                    var answers = new List<QuizAnswer>();
                                    foreach (var answerElement in answersElement.EnumerateArray())
                                    {
                                        var answer = new QuizAnswer();

                                        if (TryGetPropertyCaseInsensitive(answerElement, "text", out var ansTextProp))
                                            answer.Text = ansTextProp.GetString() ?? "";

                                        if (TryGetPropertyCaseInsensitive(answerElement, "isCorrect", out var correctProp))
                                            answer.IsCorrect = correctProp.GetBoolean();

                                        if (TryGetPropertyCaseInsensitive(answerElement, "displayOrder", out var orderProp))
                                            answer.DisplayOrder = orderProp.GetInt32();

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
                    // Handle video timestamps and properties
                    if (TryGetPropertyCaseInsensitive(jsonElement, "timestamps", out var timestampsElement))
                    {
                        var timestamps = new List<VideoTimestamp>();
                        if (timestampsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var timestampElement in timestampsElement.EnumerateArray())
                            {
                                var timestamp = new VideoTimestamp();

                                if (TryGetPropertyCaseInsensitive(timestampElement, "title", out var titleProp))
                                    timestamp.Title = titleProp.GetString() ?? "";

                                if (TryGetPropertyCaseInsensitive(timestampElement, "startTime", out var timeProp))
                                    timestamp.startTime = timeProp.GetString() ?? "";

                                if (TryGetPropertyCaseInsensitive(timestampElement, "endTime", out var endTimeProp))
                                    timestamp.endTime = endTimeProp.GetString() ?? "";

                                if (TryGetPropertyCaseInsensitive(timestampElement, "description", out var descProp))
                                    timestamp.Description = descProp.GetString();

                                if (TryGetPropertyCaseInsensitive(timestampElement, "type", out var typeProp))
                                    timestamp.Type = typeProp.GetString();

                                timestamps.Add(timestamp);
                            }
                        }
                        video.VideoTimestamps = timestamps;
                        _logger.LogInformation("Added {Count} video timestamps", timestamps.Count);
                    }
                    
                    // Video-specific properties
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var videoAssetId))
                        video.AssetId = videoAssetId.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "videoPath", out var videoPath))
                        video.VideoPath = videoPath.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "videoDuration", out var duration))
                        video.VideoDuration = duration.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "videoResolution", out var resolution))
                        video.VideoResolution = resolution.GetString();
                    break;

                case MQTT_TemplateMaterial mqtt:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "message_type", out var msgType))
                        mqtt.message_type = msgType.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "message_text", out var msgText))
                        mqtt.message_text = msgText.GetString();
                    break;

                case UnityMaterial unity:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var unityAssetId))
                        unity.AssetId = unityAssetId.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "unityVersion", out var version))
                        unity.UnityVersion = version.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "unityBuildTarget", out var buildTarget))
                        unity.UnityBuildTarget = buildTarget.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "unitySceneName", out var sceneName))
                        unity.UnitySceneName = sceneName.GetString();
                    break;

                case DefaultMaterial defaultMat:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var defaultAssetId))
                        defaultMat.AssetId = defaultAssetId.GetInt32();
                    break;

                case ImageMaterial image:
                    if (TryGetPropertyCaseInsensitive(jsonElement, "assetId", out var imageAssetId))
                        image.AssetId = imageAssetId.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "imagePath", out var imagePath))
                        image.ImagePath = imagePath.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "imageWidth", out var width))
                        image.ImageWidth = width.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "imageHeight", out var height))
                        image.ImageHeight = height.GetInt32();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "imageFormat", out var format))
                        image.ImageFormat = format.GetString();
                    if (TryGetPropertyCaseInsensitive(jsonElement, "annotations", out var annotations))
                        image.Annotations = annotations.GetRawText();
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
                    break;
            }
        }
        
        // PUT: api/{tenantName}/materials/5
        // JSON-based PUT endpoint that accepts the same format as GET responses
        // Supports updating material properties and managing relationships via 'related' array
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMaterial(string tenantName, string id)
        {
            try
            {
                // Parse the ID from string (supports future UUID migration)
                if (!int.TryParse(id, out int materialId))
                {
                    return BadRequest($"Invalid material ID format: {id}");
                }

                // Read the raw JSON body
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(body);

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

                // Parse the material from JSON (excluding 'related' array)
                var material = ParseMaterialFromJson(jsonElement);
                if (material == null)
                {
                    return BadRequest("Invalid material data or unsupported material type");
                }

                // Ensure the ID is set correctly
                material.id = materialId;

                // Update the material
                await _materialService.UpdateMaterialAsync(material);
                _logger.LogInformation("Updated material {Id} for tenant: {TenantName}", materialId, tenantName);

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

                return NoContent();
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
                id, video.VideoTimestamps?.Count() ?? 0, tenantName);

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
                _ => typeof(Material)
            };
        }
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
