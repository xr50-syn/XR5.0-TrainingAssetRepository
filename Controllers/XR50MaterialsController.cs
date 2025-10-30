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
            MaterialType.Image => await GetImageDetails(id),
            MaterialType.PDF => await GetPDFDetails(id),
            MaterialType.UnityDemo => await GetUnityDemoDetails(id),
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

    return new
    {
        Id = workflow.Id,
        Name = workflow.Name,
        Description = workflow.Description,
        Type = workflow.Type.ToString(),
        Created_at = workflow.Created_at,
        Updated_at = workflow.Updated_at,
        WorkflowSteps = workflow.WorkflowSteps?.Select(ws => new
        {
            Id = ws.Id,
            Title = ws.Title,
            Content = ws.Content
        }) ?? Enumerable.Empty<object>()
    };
}

private async Task<object?> GetVideoDetails(int materialId)
{
    var video = await _materialService.GetVideoMaterialWithTimestampsAsync(materialId);
    if (video == null) return null;

    return new
    {
        Id = video.Id,
        Name = video.Name,
        Description = video.Description,
        Type = video.Type.ToString(),
        Created_at = video.Created_at,
        Updated_at = video.Updated_at,
        AssetId = video.AssetId,
        VideoPath = video.VideoPath,
        VideoDuration = video.VideoDuration,
        VideoResolution = video.VideoResolution,
        VideoTimestamps = video.VideoTimestamps?.Select(vt => new
        {
            Id = vt.id,
            Title = vt.Title,
            Time = vt.Time,
            Description = vt.Description
        }) ?? Enumerable.Empty<object>()
    };
}

private async Task<object?> GetChecklistDetails(int materialId)
{
    var checklist = await _materialService.GetChecklistMaterialWithEntriesAsync(materialId);
    if (checklist == null) return null;

    return new
    {
        Id = checklist.Id,
        Name = checklist.Name,
        Description = checklist.Description,
        Type = checklist.Type.ToString(),
        Created_at = checklist.Created_at,
        Updated_at = checklist.Updated_at,
        ChecklistEntries = checklist.ChecklistEntries?.Select(ce => new
        {
            Id = ce.ChecklistEntryId,
            Text = ce.Text,
            Description = ce.Description
        }) ?? Enumerable.Empty<object>()
    };
}

private async Task<object?> GetQuestionnaireDetails(int materialId)
{
    var questionnaire = await _materialService.GetQuestionnaireMaterialWithEntriesAsync(materialId);
    if (questionnaire == null) return null;

    return new
    {
        Id = questionnaire.Id,
        Name = questionnaire.Name,
        Description = questionnaire.Description,
        Type = questionnaire.Type.ToString(),
        Created_at = questionnaire.Created_at,
        Updated_at = questionnaire.Updated_at,
        QuestionnaireType = questionnaire.QuestionnaireType,
        PassingScore = questionnaire.PassingScore,
        QuestionnaireConfig = questionnaire.QuestionnaireConfig,
        QuestionnaireEntries = questionnaire.QuestionnaireEntries?.Select(qe => new
        {
            Id = qe.QuestionnaireEntryId,
            Text = qe.Text,
            Description = qe.Description
        }) ?? Enumerable.Empty<object>()
    };
}

private async Task<object?> GetImageDetails(int materialId)
{
    var image = await _materialService.GetImageMaterialAsync(materialId);
    if (image == null) return null;

    return new
    {
        Id = image.Id,
        Name = image.Name,
        Description = image.Description,
        Type = image.Type.ToString(),
        Created_at = image.Created_at,
        Updated_at = image.Updated_at,
        AssetId = image.AssetId,
        ImagePath = image.ImagePath,
        ImageWidth = image.ImageWidth,
        ImageHeight = image.ImageHeight,
        ImageFormat = image.ImageFormat
    };
}

private async Task<object?> GetPDFDetails(int materialId)
{
    var pdf = await _materialService.GetPDFMaterialAsync(materialId);
    if (pdf == null) return null;

    return new
    {
        Id = pdf.Id,
        Name = pdf.Name,
        Description = pdf.Description,
        Type = pdf.Type.ToString(),
        Created_at = pdf.Created_at,
        Updated_at = pdf.Updated_at,
        AssetId = pdf.AssetId,
        PdfPath = pdf.PdfPath,
        PdfPageCount = pdf.PdfPageCount,
        PdfFileSize = pdf.PdfFileSize
    };
}

private async Task<object?> GetUnityDemoDetails(int materialId)
{
    var unity = await _materialService.GetUnityDemoMaterialAsync(materialId);
    if (unity == null) return null;

    return new
    {
        Id = unity.Id,
        Name = unity.Name,
        Description = unity.Description,
        Type = unity.Type.ToString(),
        Created_at = unity.Created_at,
        Updated_at = unity.Updated_at,
        AssetId = unity.AssetId,
        UnityVersion = unity.UnityVersion,
        UnityBuildTarget = unity.UnityBuildTarget,
        UnitySceneName = unity.UnitySceneName
    };
}

private async Task<object?> GetChatbotDetails(int materialId)
{
    var chatbot = await _materialService.GetChatbotMaterialAsync(materialId);
    if (chatbot == null) return null;

    return new
    {
        Id = chatbot.Id,
        Name = chatbot.Name,
        Description = chatbot.Description,
        Type = chatbot.Type.ToString(),
        Created_at = chatbot.Created_at,
        Updated_at = chatbot.Updated_at,
        ChatbotConfig = chatbot.ChatbotConfig,
        ChatbotModel = chatbot.ChatbotModel,
        ChatbotPrompt = chatbot.ChatbotPrompt
    };
}

private async Task<object?> GetMQTTTemplateDetails(int materialId)
{
    var mqtt = await _materialService.GetMQTTTemplateMaterialAsync(materialId);
    if (mqtt == null) return null;

    return new
    {
        Id = mqtt.Id,
        Name = mqtt.Name,
        Description = mqtt.Description,
        Type = mqtt.Type.ToString(),
        Created_at = mqtt.Created_at,
        Updated_at = mqtt.Updated_at,
        MessageType = mqtt.message_type,
        MessageText = mqtt.message_text
    };
}

private async Task<object?> GetBasicMaterialDetails(int materialId)
{
    var material = await _materialService.GetMaterialAsync(materialId);
    if (material == null) return null;

    return new
    {
        Id = material.Id,
        Name = material.Name,
        Description = material.Description,
        Type = material.Type.ToString(),
        Created_at = material.Created_at,
        Updated_at = material.Updated_at
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
                    var completeDetails = await _materialService.GetCompleteMaterialDetailsAsync(material.Id);
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
                    Id = material.Id,
                    Name = material.Name,
                    Description = material.Description,
                    Type = material.Type.ToString(),
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
        // POST: api/{tenantName}/materials - Generic material creation
        // Accepts both JSON (application/json) and form-data (application/x-www-form-urlencoded, multipart/form-data)
        [HttpPost]
        public async Task<ActionResult<Material>> PostMaterial(string tenantName)
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
                    createdMaterial.Name, createdMaterial.Id, tenantName);

                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.Id },
                    createdMaterial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating material for tenant: {TenantName}", tenantName);
                return StatusCode(500, $"Error creating material: {ex.Message}");
            }
        }
        // NEW Enhanced endpoint in XR50materialsController.cs
        // This accepts both JSON data AND file uploads for complete asset creation during material creation
        // Keeps the existing PostMaterialDetailed intact for backward compatibility

        [HttpPost("detail-with-asset")]
        public async Task<ActionResult<Material>> PostMaterialDetailedWithAsset(
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
        private async Task<ActionResult<Material>> CreateMaterialWithAsset(
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
                    var createdMaterial = await _materialService.CreateMaterialAsync(material);
                    
                    _logger.LogInformation("Created material {MaterialId} ({Name}) with asset {AssetId}", 
                        createdMaterial.Id, createdMaterial.Name, createdAsset.Id);

                    return CreatedAtAction(nameof(GetMaterial),
                        new { tenantName, id = createdMaterial.Id },
                        createdMaterial);
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
                case UnityDemoMaterial unity:
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
        [HttpPost("detail")]
        // NEW: Data class for asset reference creation
        public async Task<ActionResult<Material>> PostMaterialDetailed(string tenantName, [FromBody] JsonElement materialData)
        {
            try
            {
                // Parse the incoming JSON to determine material type
                var materialType = GetMaterialTypeFromJson(materialData);

                _logger.LogInformation("Creating detailed material of type: {MaterialType} for tenant: {TenantName}",
                    materialType, tenantName);

                // Delegate to the appropriate specialized method based on material type
                return materialType.ToLower() switch
                {
                    "workflow" => await CreateWorkflowFromJson(tenantName, materialData),
                    "video" => await CreateVideoFromJson(tenantName, materialData),
                    "checklist" => await CreateChecklistFromJson(tenantName, materialData),
                    "questionnaire" => await CreateQuestionnaireFromJson(tenantName, materialData),
                    _ => await CreateBasicMaterialFromJson(tenantName, materialData)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating detailed material for tenant: {TenantName}", tenantName);
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

        private async Task<ActionResult<Material>> CreateWorkflowFromJson(string tenantName, JsonElement jsonElement)
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
                
                // Parse the steps
                var steps = new List<WorkflowStep>();
                if (TryGetPropertyCaseInsensitive(jsonElement, "steps", out var stepsElement) && 
                    stepsElement.ValueKind == JsonValueKind.Array)
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
                    createdMaterial.Name, createdMaterial.Id);
                
                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.Id },
                    createdMaterial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating workflow from JSON");
                throw;
            }
        }

        private async Task<ActionResult<Material>> CreateVideoFromJson(string tenantName, JsonElement jsonElement)
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
                        
                        if (TryGetPropertyCaseInsensitive(timestampElement, "time", out var timeProp))
                            timestamp.Time = timeProp.GetString() ?? "";
                        
                        if (TryGetPropertyCaseInsensitive(timestampElement, "description", out var descriptionProp))
                            timestamp.Description = descriptionProp.GetString();
                        
                        timestamps.Add(timestamp);
                    }
                }
                
                _logger.LogInformation("🎬 Parsed video: {Name} with {TimestampCount} timestamps", video.Name, timestamps.Count);
                
                // Use the service method directly instead of the controller method
                var createdMaterial = await _materialService.CreateVideoWithTimestampsAsync(video, timestamps);
                
                _logger.LogInformation("Created video material {Name} with ID {Id}", 
                    createdMaterial.Name, createdMaterial.Id);
                
                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.Id },
                    createdMaterial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating video from JSON");
                throw;
            }
        }

        private async Task<ActionResult<Material>> CreateChecklistFromJson(string tenantName, JsonElement jsonElement)
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
                
                // Parse the entries
                var entries = new List<ChecklistEntry>();
                if (TryGetPropertyCaseInsensitive(jsonElement, "entries", out var entriesElement) && 
                    entriesElement.ValueKind == JsonValueKind.Array)
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
                    createdMaterial.Name, createdMaterial.Id);
                
                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.Id },
                    createdMaterial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating checklist from JSON");
                throw;
            }
        }

        private async Task<ActionResult<Material>> CreateQuestionnaireFromJson(string tenantName, JsonElement jsonElement)
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
                
                // Parse the entries
                var entries = new List<QuestionnaireEntry>();
                if (TryGetPropertyCaseInsensitive(jsonElement, "entries", out var entriesElement) && 
                    entriesElement.ValueKind == JsonValueKind.Array)
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
                    createdMaterial.Name, createdMaterial.Id);
                
                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.Id },
                    createdMaterial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Error creating questionnaire from JSON");
                throw;
            }
        }

        private async Task<ActionResult<Material>> CreateBasicMaterialFromJson(string tenantName, JsonElement jsonElement)
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
                    createdMaterial.Name, createdMaterial.Id);
                
                return CreatedAtAction(nameof(GetMaterial),
                    new { tenantName, id = createdMaterial.Id },
                    createdMaterial);
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
                        10 => "default",
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
                        10 => "default",
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
                ("unitydemo", _) or (_, "unitydemo") => new UnityDemoMaterial(),
                ("chatbotmaterial", _) or (_, "chatbot") => new ChatbotMaterial(),
                ("questionnairematerial", _) or (_, "questionnaire") => new QuestionnaireMaterial(),
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
                        checklist.ChecklistEntries = entries;
                        _logger.LogInformation("Added {Count} checklist entries", entries.Count);
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
                                
                                if (TryGetPropertyCaseInsensitive(timestampElement, "time", out var timeProp))
                                    timestamp.Time = timeProp.GetString() ?? "";
                                
                                if (TryGetPropertyCaseInsensitive(timestampElement, "description", out var descProp))
                                    timestamp.Description = descProp.GetString();
                                
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

                case UnityDemoMaterial unity:
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
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMaterial(string tenantName, int id, Material material)
        {
            if (id != material.Id)
            {
                return BadRequest("ID mismatch");
            }

            _logger.LogInformation("Updating material {Id} for tenant: {TenantName}", id, tenantName);

            try
            {
                await _materialService.UpdateMaterialAsync(material);
                _logger.LogInformation("Updated material {Id} for tenant: {TenantName}", id, tenantName);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _materialService.MaterialExistsAsync(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/{tenantName}/materials/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaterial(string tenantName, int id)
        {
            _logger.LogInformation("Deleting material {Id} for tenant: {TenantName}", id, tenantName);

            var deleted = await _materialService.DeleteMaterialAsync(id);

            if (!deleted)
            {
                return NotFound();
            }

            _logger.LogInformation("Deleted material {Id} for tenant: {TenantName}", id, tenantName);

            return NoContent();
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
        public async Task<ActionResult<IEnumerable<UnityDemoMaterial>>> GetUnityDemoMaterials(string tenantName)
        {
            _logger.LogInformation("🎮 Getting Unity demo materials for tenant: {TenantName}", tenantName);

            var unityDemos = await _materialService.GetAllUnityDemoMaterialsAsync();

            _logger.LogInformation("Found {Count} Unity demo materials for tenant: {TenantName}",
                unityDemos.Count(), tenantName);

            return Ok(unityDemos);
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
                    new { tenantName, id = createdMaterial.Id },
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
                    new { tenantName, id = createdMaterial.Id },
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
                    new { tenantName, id = createdMaterial.Id },
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
                id, checklist.ChecklistEntries?.Count() ?? 0, tenantName);

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
                    UnityDemos = (await _materialService.GetAllUnityDemoMaterialsAsync()).Count(),
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
                MaterialType.UnityDemo => typeof(UnityDemoMaterial),
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
            public int UnityDemos { get; set; }
            public int Total { get; set; }
        }


    }
