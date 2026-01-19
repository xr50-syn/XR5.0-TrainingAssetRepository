using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/voice-assistant")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "voice")]
    public class VoiceAssistantController : ControllerBase
    {
        private readonly IVoiceAssistantService _voiceAssistantService;
        private readonly IVoiceMaterialService _voiceMaterialService;
        private readonly ILogger<VoiceAssistantController> _logger;

        public VoiceAssistantController(
            IVoiceAssistantService voiceAssistantService,
            IVoiceMaterialService voiceMaterialService,
            ILogger<VoiceAssistantController> logger)
        {
            _voiceAssistantService = voiceAssistantService;
            _voiceMaterialService = voiceMaterialService;
            _logger = logger;
        }

        #region Default Endpoint Operations (no material required)

        /// <summary>
        /// Sends a query to the default voice assistant and returns the response with audio.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="request">The request containing the query and optional session ID</param>
        /// <returns>The voice assistant's response including text and audio URL</returns>
        [HttpPost("ask")]
        public async Task<ActionResult<VoiceAskResponse>> Ask(
            string tenantName,
            [FromBody] VoiceAskRequest request)
        {
            _logger.LogInformation("Voice assistant request in tenant {TenantName}: {Query}", tenantName, request.Query);

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new { Error = "Query cannot be empty" });
            }

            try
            {
                var response = await _voiceAssistantService.AskAsync(request.Query, request.SessionId);

                _logger.LogInformation("Voice assistant response received, session {SessionId}", response.SessionId);

                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Voice assistant operation failed");
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in voice assistant");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Sends a query to the default voice assistant using form data.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="query">The question to ask</param>
        /// <param name="session_id">Optional session ID for conversation continuity</param>
        /// <returns>The voice assistant's response including text and audio URL</returns>
        [HttpPost("ask/form")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<ActionResult<VoiceAskResponse>> AskForm(
            string tenantName,
            [FromForm] string query,
            [FromForm] string? session_id = null)
        {
            return await Ask(tenantName, new VoiceAskRequest
            {
                Query = query,
                SessionId = session_id
            });
        }

        /// <summary>
        /// Uploads a document to the default voice assistant for knowledge extraction.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="file">The document file to upload (PDF, DOC, DOCX, TXT)</param>
        /// <returns>Upload response with job ID for tracking</returns>
        [HttpPost("documents")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<VoiceDocumentUploadResponse>> UploadDocument(
            string tenantName,
            IFormFile file)
        {
            _logger.LogInformation("Document upload to voice assistant in tenant {TenantName}: {FileName}",
                tenantName, file?.FileName);

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { Error = "No file provided" });
            }

            try
            {
                using var stream = file.OpenReadStream();
                var response = await _voiceAssistantService.UploadDocumentAsync(
                    stream, file.FileName, file.ContentType);

                _logger.LogInformation("Document uploaded successfully. Job ID: {JobId}", response.JobId);

                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Document upload failed");
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error uploading document");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Checks if the default voice assistant endpoint is available.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <returns>Health status of the default voice assistant endpoint</returns>
        [HttpGet("health")]
        public async Task<ActionResult<object>> CheckHealth(string tenantName)
        {
            _logger.LogInformation("Checking default voice assistant health in tenant: {TenantName}", tenantName);

            var isAvailable = await _voiceAssistantService.IsDefaultEndpointAvailableAsync();

            return Ok(new
            {
                available = isAvailable,
                checkedAt = DateTime.UtcNow
            });
        }

        #endregion

        #region VoiceMaterial-specific Operations

        /// <summary>
        /// Sends a query to a voice assistant using a specific VoiceMaterial.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="voiceId">The ID of the VoiceMaterial to use</param>
        /// <param name="request">The request containing the query and optional session ID</param>
        /// <returns>The voice assistant's response including text and audio URL</returns>
        [HttpPost("{voiceId}/ask")]
        public async Task<ActionResult<VoiceAskResponse>> AskWithMaterial(
            string tenantName,
            int voiceId,
            [FromBody] VoiceAskRequest request)
        {
            _logger.LogInformation("Voice assistant request for material {VoiceId} in tenant {TenantName}: {Query}",
                voiceId, tenantName, request.Query);

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new { Error = "Query cannot be empty" });
            }

            try
            {
                var response = await _voiceAssistantService.AskAsync(voiceId, request.Query, request.SessionId);

                _logger.LogInformation("Voice assistant response received for material {VoiceId}, session {SessionId}",
                    voiceId, response.SessionId);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("VoiceMaterial {VoiceId} not found in tenant {TenantName}", voiceId, tenantName);
                return NotFound(new { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Voice assistant operation failed for material {VoiceId}", voiceId);
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in voice assistant for material {VoiceId}", voiceId);
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Sends a query to a voice assistant using form data for a specific VoiceMaterial.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="voiceId">The ID of the VoiceMaterial to use</param>
        /// <param name="query">The question to ask</param>
        /// <param name="session_id">Optional session ID for conversation continuity</param>
        /// <returns>The voice assistant's response including text and audio URL</returns>
        [HttpPost("{voiceId}/ask/form")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<ActionResult<VoiceAskResponse>> AskWithMaterialForm(
            string tenantName,
            int voiceId,
            [FromForm] string query,
            [FromForm] string? session_id = null)
        {
            return await AskWithMaterial(tenantName, voiceId, new VoiceAskRequest
            {
                Query = query,
                SessionId = session_id
            });
        }

        /// <summary>
        /// Uploads a document to a specific VoiceMaterial for knowledge extraction.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="voiceId">The ID of the VoiceMaterial</param>
        /// <param name="file">The document file to upload</param>
        /// <returns>Upload response with job ID for tracking</returns>
        [HttpPost("{voiceId}/documents")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<VoiceDocumentUploadResponse>> UploadDocumentToMaterial(
            string tenantName,
            int voiceId,
            IFormFile file)
        {
            _logger.LogInformation("Document upload to voice material {VoiceId} in tenant {TenantName}: {FileName}",
                voiceId, tenantName, file?.FileName);

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { Error = "No file provided" });
            }

            try
            {
                using var stream = file.OpenReadStream();
                var response = await _voiceAssistantService.UploadDocumentAsync(
                    voiceId, stream, file.FileName, file.ContentType);

                _logger.LogInformation("Document uploaded successfully to material {VoiceId}. Job ID: {JobId}",
                    voiceId, response.JobId);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("VoiceMaterial {VoiceId} not found in tenant {TenantName}", voiceId, tenantName);
                return NotFound(new { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Document upload failed for material {VoiceId}", voiceId);
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error uploading document to material {VoiceId}", voiceId);
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Gets documents (assets) associated with a VoiceMaterial.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="voiceId">The ID of the VoiceMaterial</param>
        /// <returns>List of document information</returns>
        [HttpGet("{voiceId}/documents")]
        public async Task<ActionResult<IEnumerable<VoiceDocumentInfo>>> GetDocuments(
            string tenantName,
            int voiceId)
        {
            _logger.LogInformation("Getting documents for voice material {VoiceId} in tenant {TenantName}",
                voiceId, tenantName);

            try
            {
                var documents = await _voiceAssistantService.GetDocumentsAsync(voiceId);

                _logger.LogInformation("Found {Count} documents for voice material {VoiceId}",
                    documents.Count(), voiceId);

                return Ok(documents);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("VoiceMaterial {VoiceId} not found in tenant {TenantName}", voiceId, tenantName);
                return NotFound(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting documents for material {VoiceId}", voiceId);
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Checks if the voice assistant endpoint for a VoiceMaterial is available.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="voiceId">The ID of the VoiceMaterial</param>
        /// <returns>Health status of the voice assistant endpoint</returns>
        [HttpGet("{voiceId}/health")]
        public async Task<ActionResult<object>> CheckMaterialHealth(
            string tenantName,
            int voiceId)
        {
            _logger.LogInformation("Checking voice assistant health for material {VoiceId} in tenant: {TenantName}",
                voiceId, tenantName);

            var isAvailable = await _voiceAssistantService.IsEndpointAvailableAsync(voiceId);

            return Ok(new
            {
                voiceMaterialId = voiceId,
                available = isAvailable,
                checkedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Gets all VoiceMaterials for the tenant.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <returns>List of voice materials</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetVoiceMaterials(string tenantName)
        {
            _logger.LogInformation("Getting voice materials for tenant: {TenantName}", tenantName);

            var materials = await _voiceMaterialService.GetAllAsync();

            var result = materials.Select(m => new
            {
                id = m.id,
                name = m.Name,
                description = m.Description,
                status = m.VoiceStatus,
                assetIds = m.GetAssetIdsList(),
                serviceJobId = m.ServiceJobId,
                created_at = m.Created_at,
                updated_at = m.Updated_at
            });

            _logger.LogInformation("Found {Count} voice materials for tenant: {TenantName}", materials.Count(), tenantName);

            return Ok(result);
        }

        /// <summary>
        /// Gets a specific VoiceMaterial's details.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="voiceId">The VoiceMaterial ID</param>
        /// <returns>VoiceMaterial details</returns>
        [HttpGet("{voiceId}")]
        public async Task<ActionResult<object>> GetVoiceMaterial(string tenantName, int voiceId)
        {
            _logger.LogInformation("Getting voice material {VoiceId} for tenant: {TenantName}", voiceId, tenantName);

            var material = await _voiceMaterialService.GetByIdAsync(voiceId);
            if (material == null)
            {
                _logger.LogWarning("VoiceMaterial {VoiceId} not found in tenant: {TenantName}", voiceId, tenantName);
                return NotFound(new { Error = $"VoiceMaterial with ID {voiceId} not found" });
            }

            return Ok(new
            {
                id = material.id,
                name = material.Name,
                description = material.Description,
                status = material.VoiceStatus,
                assetIds = material.GetAssetIdsList(),
                serviceJobId = material.ServiceJobId,
                created_at = material.Created_at,
                updated_at = material.Updated_at
            });
        }

        #endregion
    }
}
