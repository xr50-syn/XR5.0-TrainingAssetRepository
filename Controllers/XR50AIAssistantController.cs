using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/ai-assistant")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "ai-assistant")]
    public class AIAssistantController : ControllerBase
    {
        private readonly IAIAssistantService _aiAssistantService;
        private readonly IAIAssistantMaterialService _aiAssistantMaterialService;
        private readonly ILogger<AIAssistantController> _logger;

        public AIAssistantController(
            IAIAssistantService aiAssistantService,
            IAIAssistantMaterialService aiAssistantMaterialService,
            ILogger<AIAssistantController> logger)
        {
            _aiAssistantService = aiAssistantService;
            _aiAssistantMaterialService = aiAssistantMaterialService;
            _logger = logger;
        }

        #region Default Endpoint Operations (no material required)

        /// <summary>
        /// Sends a query to the default AI assistant and returns the response with audio.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="request">The request containing the query and optional session ID</param>
        /// <returns>The AI assistant's response including text and audio URL</returns>
        [HttpPost("ask")]
        public async Task<ActionResult<AIAssistantAskResponse>> Ask(
            string tenantName,
            [FromBody] AIAssistantAskRequest request)
        {
            _logger.LogInformation("AI assistant request in tenant {TenantName}: {Query}", tenantName, request.Query);

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new { Error = "Query cannot be empty" });
            }

            try
            {
                var response = await _aiAssistantService.AskAsync(request.Query, request.SessionId);

                _logger.LogInformation("AI assistant response received, session {SessionId}", response.SessionId);

                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "AI assistant operation failed");
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in AI assistant");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Sends a query to the default AI assistant using form data.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="query">The question to ask</param>
        /// <param name="session_id">Optional session ID for conversation continuity</param>
        /// <returns>The AI assistant's response including text and audio URL</returns>
        [HttpPost("ask/form")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<ActionResult<AIAssistantAskResponse>> AskForm(
            string tenantName,
            [FromForm] string query,
            [FromForm] string? session_id = null)
        {
            return await Ask(tenantName, new AIAssistantAskRequest
            {
                Query = query,
                SessionId = session_id
            });
        }

        /// <summary>
        /// Uploads a document to the default AI assistant for knowledge extraction.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="file">The document file to upload (PDF, DOC, DOCX, TXT)</param>
        /// <returns>Upload response with job ID for tracking</returns>
        [HttpPost("documents")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<AIAssistantDocumentUploadResponse>> UploadDocument(
            string tenantName,
            IFormFile file)
        {
            _logger.LogInformation("Document upload to AI assistant in tenant {TenantName}: {FileName}",
                tenantName, file?.FileName);

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { Error = "No file provided" });
            }

            try
            {
                using var stream = file.OpenReadStream();
                var response = await _aiAssistantService.UploadDocumentAsync(
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
        /// Checks if the default AI assistant endpoint is available.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <returns>Health status of the default AI assistant endpoint</returns>
        [HttpGet("health")]
        public async Task<ActionResult<object>> CheckHealth(string tenantName)
        {
            _logger.LogInformation("Checking default AI assistant health in tenant: {TenantName}", tenantName);

            var isAvailable = await _aiAssistantService.IsDefaultEndpointAvailableAsync();

            return Ok(new
            {
                available = isAvailable,
                checkedAt = DateTime.UtcNow
            });
        }

        #endregion

        #region AIAssistantMaterial-specific Operations

        /// <summary>
        /// Sends a query to an AI assistant using a specific AIAssistantMaterial.
        /// On first call, appends document filenames to establish context and stores the session.
        /// Subsequent calls use the stored session for context continuity.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="aiAssistantId">The ID of the AIAssistantMaterial to use</param>
        /// <param name="request">The request containing the query and optional session ID</param>
        /// <returns>The AI assistant's response including text and audio URL</returns>
        [HttpPost("{aiAssistantId}/ask")]
        public async Task<ActionResult<AIAssistantAskResponse>> AskWithMaterial(
            string tenantName,
            int aiAssistantId,
            [FromBody] AIAssistantAskRequest request)
        {
            _logger.LogInformation("AI assistant request for material {AIAssistantId} in tenant {TenantName}: {Query}",
                aiAssistantId, tenantName, request.Query);

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new { Error = "Query cannot be empty" });
            }

            try
            {
                var response = await _aiAssistantService.AskAsync(aiAssistantId, request.Query, request.SessionId);

                _logger.LogInformation("AI assistant response received for material {AIAssistantId}, session {SessionId}",
                    aiAssistantId, response.SessionId);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("AIAssistantMaterial {AIAssistantId} not found in tenant {TenantName}", aiAssistantId, tenantName);
                return NotFound(new { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "AI assistant operation failed for material {AIAssistantId}", aiAssistantId);
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in AI assistant for material {AIAssistantId}", aiAssistantId);
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Sends a query to an AI assistant using form data for a specific AIAssistantMaterial.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="aiAssistantId">The ID of the AIAssistantMaterial to use</param>
        /// <param name="query">The question to ask</param>
        /// <param name="session_id">Optional session ID for conversation continuity</param>
        /// <returns>The AI assistant's response including text and audio URL</returns>
        [HttpPost("{aiAssistantId}/ask/form")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<ActionResult<AIAssistantAskResponse>> AskWithMaterialForm(
            string tenantName,
            int aiAssistantId,
            [FromForm] string query,
            [FromForm] string? session_id = null)
        {
            return await AskWithMaterial(tenantName, aiAssistantId, new AIAssistantAskRequest
            {
                Query = query,
                SessionId = session_id
            });
        }

        /// <summary>
        /// Uploads a document to a specific AIAssistantMaterial for knowledge extraction.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="aiAssistantId">The ID of the AIAssistantMaterial</param>
        /// <param name="file">The document file to upload</param>
        /// <returns>Upload response with job ID for tracking</returns>
        [HttpPost("{aiAssistantId}/documents")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<AIAssistantDocumentUploadResponse>> UploadDocumentToMaterial(
            string tenantName,
            int aiAssistantId,
            IFormFile file)
        {
            _logger.LogInformation("Document upload to AI assistant material {AIAssistantId} in tenant {TenantName}: {FileName}",
                aiAssistantId, tenantName, file?.FileName);

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { Error = "No file provided" });
            }

            try
            {
                using var stream = file.OpenReadStream();
                var response = await _aiAssistantService.UploadDocumentAsync(
                    aiAssistantId, stream, file.FileName, file.ContentType);

                _logger.LogInformation("Document uploaded successfully to material {AIAssistantId}. Job ID: {JobId}",
                    aiAssistantId, response.JobId);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("AIAssistantMaterial {AIAssistantId} not found in tenant {TenantName}", aiAssistantId, tenantName);
                return NotFound(new { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Document upload failed for material {AIAssistantId}", aiAssistantId);
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error uploading document to material {AIAssistantId}", aiAssistantId);
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Gets documents (assets) associated with an AIAssistantMaterial.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="aiAssistantId">The ID of the AIAssistantMaterial</param>
        /// <returns>List of document information</returns>
        [HttpGet("{aiAssistantId}/documents")]
        public async Task<ActionResult<IEnumerable<AIAssistantDocumentInfo>>> GetDocuments(
            string tenantName,
            int aiAssistantId)
        {
            _logger.LogInformation("Getting documents for AI assistant material {AIAssistantId} in tenant {TenantName}",
                aiAssistantId, tenantName);

            try
            {
                var documents = await _aiAssistantService.GetDocumentsAsync(aiAssistantId);

                _logger.LogInformation("Found {Count} documents for AI assistant material {AIAssistantId}",
                    documents.Count(), aiAssistantId);

                return Ok(documents);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("AIAssistantMaterial {AIAssistantId} not found in tenant {TenantName}", aiAssistantId, tenantName);
                return NotFound(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting documents for material {AIAssistantId}", aiAssistantId);
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Checks if the AI assistant endpoint for an AIAssistantMaterial is available.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="aiAssistantId">The ID of the AIAssistantMaterial</param>
        /// <returns>Health status of the AI assistant endpoint</returns>
        [HttpGet("{aiAssistantId}/health")]
        public async Task<ActionResult<object>> CheckMaterialHealth(
            string tenantName,
            int aiAssistantId)
        {
            _logger.LogInformation("Checking AI assistant health for material {AIAssistantId} in tenant: {TenantName}",
                aiAssistantId, tenantName);

            var isAvailable = await _aiAssistantService.IsEndpointAvailableAsync(aiAssistantId);

            return Ok(new
            {
                aiAssistantMaterialId = aiAssistantId,
                available = isAvailable,
                checkedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Gets all AIAssistantMaterials for the tenant.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <returns>List of AI assistant materials</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAIAssistantMaterials(string tenantName)
        {
            _logger.LogInformation("Getting AI assistant materials for tenant: {TenantName}", tenantName);

            var materials = await _aiAssistantMaterialService.GetAllAsync();

            var result = materials.Select(m => new
            {
                id = m.id,
                name = m.Name,
                description = m.Description,
                status = m.AIAssistantStatus,
                assetIds = m.GetAssetIdsList(),
                serviceJobId = m.ServiceJobId,
                created_at = m.Created_at,
                updated_at = m.Updated_at
            });

            _logger.LogInformation("Found {Count} AI assistant materials for tenant: {TenantName}", materials.Count(), tenantName);

            return Ok(result);
        }

        /// <summary>
        /// Gets a specific AIAssistantMaterial's details.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="aiAssistantId">The AIAssistantMaterial ID</param>
        /// <returns>AIAssistantMaterial details</returns>
        [HttpGet("{aiAssistantId}")]
        public async Task<ActionResult<object>> GetAIAssistantMaterial(string tenantName, int aiAssistantId)
        {
            _logger.LogInformation("Getting AI assistant material {AIAssistantId} for tenant: {TenantName}", aiAssistantId, tenantName);

            var material = await _aiAssistantMaterialService.GetByIdAsync(aiAssistantId);
            if (material == null)
            {
                _logger.LogWarning("AIAssistantMaterial {AIAssistantId} not found in tenant: {TenantName}", aiAssistantId, tenantName);
                return NotFound(new { Error = $"AIAssistantMaterial with ID {aiAssistantId} not found" });
            }

            // Get active session info
            var activeSession = await _aiAssistantMaterialService.GetActiveSessionAsync(aiAssistantId);

            return Ok(new
            {
                id = material.id,
                name = material.Name,
                description = material.Description,
                status = material.AIAssistantStatus,
                assetIds = material.GetAssetIdsList(),
                serviceJobId = material.ServiceJobId,
                hasActiveSession = activeSession != null,
                sessionCreatedAt = activeSession?.CreatedAt,
                created_at = material.Created_at,
                updated_at = material.Updated_at
            });
        }

        /// <summary>
        /// Invalidates the current session for an AIAssistantMaterial.
        /// The next /ask call will create a new session with updated document context.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="aiAssistantId">The AIAssistantMaterial ID</param>
        /// <returns>Success message</returns>
        [HttpPost("{aiAssistantId}/session/invalidate")]
        public async Task<ActionResult<object>> InvalidateSession(string tenantName, int aiAssistantId)
        {
            _logger.LogInformation("Invalidating session for AI assistant material {AIAssistantId} in tenant: {TenantName}",
                aiAssistantId, tenantName);

            var material = await _aiAssistantMaterialService.GetByIdAsync(aiAssistantId);
            if (material == null)
            {
                return NotFound(new { Error = $"AIAssistantMaterial with ID {aiAssistantId} not found" });
            }

            await _aiAssistantMaterialService.InvalidateSessionAsync(aiAssistantId);

            return Ok(new
            {
                message = "Session invalidated. Next /ask call will create a new session with current document context.",
                aiAssistantMaterialId = aiAssistantId
            });
        }

        #endregion
    }
}
