using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;
using XR50TrainingAssetRepo.Infrastructure.ErrorHandling;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/innov-chatbot")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "innov-chatbot")]
    public class InnovChatbotController : ControllerBase
    {
        private readonly IInnovChatbotMaterialService _innovChatbotMaterialService;
        private readonly ILogger<InnovChatbotController> _logger;

        public InnovChatbotController(
            IInnovChatbotMaterialService innovChatbotMaterialService,
            ILogger<InnovChatbotController> logger)
        {
            _innovChatbotMaterialService = innovChatbotMaterialService;
            _logger = logger;
        }

        /// <summary>
        /// Sends a query to an INNOV chatbot material's pilot and returns the response.
        /// </summary>
        [HttpPost("{innovChatbotId}/chat")]
        public async Task<ActionResult<InnovChatResponse>> Chat(
            string tenantName,
            int innovChatbotId,
            [FromBody] InnovChatRequest request)
        {
            _logger.LogInformation("INNOV chat request for material {InnovChatbotId} in tenant {TenantName}: {Query}",
                innovChatbotId, tenantName, request.Query);

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return this.ProblemBadRequest("Query cannot be empty.");
            }

            try
            {
                var response = await _innovChatbotMaterialService.ChatAsync(
                    innovChatbotId, request.Query, request.ExpertiseLevel);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("INNOV chatbot material {InnovChatbotId} not found in tenant {TenantName}", innovChatbotId, tenantName);
                return this.ProblemNotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "INNOV chat operation failed for material {InnovChatbotId}", innovChatbotId);
                return this.ProblemBadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in INNOV chat for material {InnovChatbotId}", innovChatbotId);
                return this.ProblemServerError("Internal server error.");
            }
        }

        /// <summary>
        /// Sends a query to an INNOV chatbot material using form data.
        /// </summary>
        [HttpPost("{innovChatbotId}/chat/form")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<ActionResult<InnovChatResponse>> ChatForm(
            string tenantName,
            int innovChatbotId,
            [FromForm] string query,
            [FromForm] string? expertise_level = null)
        {
            return await Chat(tenantName, innovChatbotId, new InnovChatRequest
            {
                Query = query,
                ExpertiseLevel = expertise_level
            });
        }

        /// <summary>
        /// Uploads a document directly to an INNOV chatbot material's pilot.
        /// </summary>
        [HttpPost("{innovChatbotId}/documents")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<InnovDocumentUploadResponse>> UploadDocument(
            string tenantName,
            int innovChatbotId,
            IFormFile file)
        {
            _logger.LogInformation("Document upload to INNOV chatbot material {InnovChatbotId} in tenant {TenantName}: {FileName}",
                innovChatbotId, tenantName, file?.FileName);

            if (file == null || file.Length == 0)
            {
                return this.ProblemBadRequest("No file provided.");
            }

            try
            {
                using var stream = file.OpenReadStream();
                var response = await _innovChatbotMaterialService.UploadDocumentAsync(
                    innovChatbotId, stream, file.FileName, file.ContentType);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("INNOV chatbot material {InnovChatbotId} not found in tenant {TenantName}", innovChatbotId, tenantName);
                return this.ProblemNotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Document upload failed for INNOV chatbot material {InnovChatbotId}", innovChatbotId);
                return this.ProblemBadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error uploading document to INNOV chatbot material {InnovChatbotId}", innovChatbotId);
                return this.ProblemServerError("Internal server error.");
            }
        }

        /// <summary>
        /// Submits the material's associated assets for ingestion into the pilot.
        /// </summary>
        [HttpPost("{innovChatbotId}/submit")]
        public async Task<ActionResult<object>> Submit(string tenantName, int innovChatbotId)
        {
            _logger.LogInformation("Submitting INNOV chatbot material {InnovChatbotId} for processing in tenant {TenantName}",
                innovChatbotId, tenantName);

            try
            {
                var material = await _innovChatbotMaterialService.SubmitForProcessingAsync(innovChatbotId);
                return Ok(new
                {
                    id = material.id,
                    status = material.InnovStatus,
                    pilot = material.Pilot
                });
            }
            catch (KeyNotFoundException ex)
            {
                return this.ProblemNotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Submission failed for INNOV chatbot material {InnovChatbotId}", innovChatbotId);
                return this.ProblemBadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error submitting INNOV chatbot material {InnovChatbotId}", innovChatbotId);
                return this.ProblemServerError("Internal server error.");
            }
        }

        /// <summary>
        /// Gets documents (assets) associated with an INNOV chatbot material and their ingest status.
        /// </summary>
        [HttpGet("{innovChatbotId}/documents")]
        public async Task<ActionResult<IEnumerable<InnovChatbotDocumentInfo>>> GetDocuments(
            string tenantName,
            int innovChatbotId)
        {
            var material = await _innovChatbotMaterialService.GetByIdAsync(innovChatbotId);
            if (material == null)
            {
                return this.ProblemNotFound($"INNOV chatbot material with ID {innovChatbotId} not found.");
            }

            var jobs = await _innovChatbotMaterialService.GetAssetJobsAsync(innovChatbotId);
            var jobByAsset = jobs.ToDictionary(j => j.AssetId, j => j);
            var assets = await _innovChatbotMaterialService.GetAssetsAsync(innovChatbotId);

            var documents = assets.Select(a =>
            {
                jobByAsset.TryGetValue(a.Id, out var job);
                return new InnovChatbotDocumentInfo
                {
                    AssetId = a.Id,
                    FileName = a.Filename,
                    Status = job?.Status ?? "notready",
                    Pilot = job?.Pilot,
                    CollectionName = job?.CollectionName,
                    ErrorMessage = job?.ErrorMessage
                };
            });

            return Ok(documents);
        }

        /// <summary>
        /// Checks if the INNOV chatbot backend is available for the tenant.
        /// </summary>
        [HttpGet("{innovChatbotId}/health")]
        public async Task<ActionResult<object>> CheckHealth(string tenantName, int innovChatbotId)
        {
            var isAvailable = await _innovChatbotMaterialService.IsEndpointAvailableAsync(innovChatbotId);
            return Ok(new
            {
                innovChatbotMaterialId = innovChatbotId,
                available = isAvailable,
                checkedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Gets all INNOV chatbot materials for the tenant.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetInnovChatbotMaterials(string tenantName)
        {
            var materials = await _innovChatbotMaterialService.GetAllAsync();

            var result = materials.Select(m => new
            {
                id = m.id,
                name = m.Name,
                description = m.Description,
                status = m.InnovStatus,
                pilot = m.Pilot,
                expertiseLevel = m.ExpertiseLevel,
                assetIds = m.GetAssetIdsList(),
                created_at = m.Created_at,
                updated_at = m.Updated_at
            });

            return Ok(result);
        }

        /// <summary>
        /// Gets a specific INNOV chatbot material's details.
        /// </summary>
        [HttpGet("{innovChatbotId}")]
        public async Task<ActionResult<object>> GetInnovChatbotMaterial(string tenantName, int innovChatbotId)
        {
            var material = await _innovChatbotMaterialService.GetByIdAsync(innovChatbotId);
            if (material == null)
            {
                return this.ProblemNotFound($"INNOV chatbot material with ID {innovChatbotId} not found.");
            }

            return Ok(new
            {
                id = material.id,
                name = material.Name,
                description = material.Description,
                status = material.InnovStatus,
                pilot = material.Pilot,
                expertiseLevel = material.ExpertiseLevel,
                assetIds = material.GetAssetIdsList(),
                created_at = material.Created_at,
                updated_at = material.Updated_at
            });
        }

        /// <summary>
        /// Clears the server-side chat history for the material's pilot.
        /// </summary>
        [HttpDelete("{innovChatbotId}/history")]
        public async Task<ActionResult<object>> ClearHistory(string tenantName, int innovChatbotId)
        {
            try
            {
                await _innovChatbotMaterialService.ClearHistoryAsync(innovChatbotId);
                return Ok(new
                {
                    message = "Chat history cleared for the pilot.",
                    innovChatbotMaterialId = innovChatbotId
                });
            }
            catch (KeyNotFoundException ex)
            {
                return this.ProblemNotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Failed to clear history for INNOV chatbot material {InnovChatbotId}", innovChatbotId);
                return this.ProblemBadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error clearing history for INNOV chatbot material {InnovChatbotId}", innovChatbotId);
                return this.ProblemServerError("Internal server error.");
            }
        }
    }
}
