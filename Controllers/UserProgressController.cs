using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/[controller]")]
    [ApiController]
    public class UserProgressController : ControllerBase
    {
        private readonly IUserMaterialService _userMaterialService;
        private readonly ILogger<UserProgressController> _logger;

        public UserProgressController(
            IUserMaterialService userMaterialService,
            ILogger<UserProgressController> logger)
        {
            _userMaterialService = userMaterialService;
            _logger = logger;
        }

        /// <summary>
        /// Submit quiz answers for evaluation
        /// POST /api/{tenantName}/materials/{materialId}/submit
        /// </summary>
        [HttpPost("~/api/{tenantName}/materials/{materialId}/submit")]
        [Authorize]
        public async Task<ActionResult<SubmitQuizAnswersResponse>> SubmitAnswers(
            string tenantName,
            int materialId,
            [FromBody] SubmitQuizAnswersRequest request)
        {
            try
            {
                var userId = GetUserIdFromAuth();

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "User not authenticated" });
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

        /// <summary>
        /// Get overall progress for current user
        /// GET /api/{tenantName}/users/progress
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/progress")]
        [Authorize]
        public async Task<ActionResult<UserProgressResponse>> GetUserProgress(string tenantName)
        {
            try
            {
                var userId = GetUserIdFromAuth();

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "User not authenticated" });
                }

                var result = await _userMaterialService.GetUserProgressAsync(userId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "User not found");
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user progress");
                return StatusCode(500, new { error = "Failed to get progress" });
            }
        }

        /// <summary>
        /// Get detailed data for a specific material
        /// GET /api/{tenantName}/users/{userId}/materials/{materialId}
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/{userId}/materials/{materialId}")]
        [Authorize]
        public async Task<ActionResult<UserMaterialDetailResponse>> GetUserMaterialDetail(
            string tenantName,
            string userId,
            int materialId)
        {
            try
            {
                var result = await _userMaterialService.GetUserMaterialDetailAsync(userId, materialId);

                if (result == null)
                {
                    return NotFound(new { error = "No data found for this user/material combination" });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting material detail for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to get material detail" });
            }
        }

        /// <summary>
        /// Get all materials with detailed data for a program
        /// GET /api/{tenantName}/users/{userId}/programs/{programId}/materials
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/{userId}/programs/{programId}/materials")]
        [Authorize]
        public async Task<ActionResult<UserProgramMaterialsResponse>> GetUserProgramMaterials(
            string tenantName,
            string userId,
            int programId)
        {
            try
            {
                var result = await _userMaterialService.GetUserProgramMaterialsAsync(userId, programId);

                if (result == null)
                {
                    return NotFound(new { error = "Program not found or no data for user" });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting program materials for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to get program materials" });
            }
        }

        private string? GetUserIdFromAuth()
        {
            // Get user ID from JWT claims
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst(ClaimTypes.Name)?.Value;
        }
    }
}
