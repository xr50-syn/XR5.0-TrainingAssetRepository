using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/[controller]")]
    [ApiController]
    public class UsersProgressController : ControllerBase
    {
        private readonly IUserMaterialService _userMaterialService;
        private readonly ILogger<UsersProgressController> _logger;

        public UsersProgressController(
            IUserMaterialService userMaterialService,
            ILogger<UsersProgressController> logger)
        {
            _userMaterialService = userMaterialService;
            _logger = logger;
        }

        /// <summary>
        /// Get overall progress for a specific user
        /// GET /api/{tenantName}/users/{userId}/progress
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/{userId}/progress")]
        // [Authorize] - Disabled for development
        public async Task<ActionResult<UserProgressResponse>> GetUserProgressByUserId(
            string tenantName,
            string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new { error = "userId is required" });
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
        /// Get overall progress for all users
        /// GET /api/{tenantName}/users/progress
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/progress")]
        // [Authorize] - Disabled for development
        public async Task<ActionResult<List<UserProgressResponse>>> GetAllUsersProgress(
            string tenantName)
        {
            try
            {
                var result = await _userMaterialService.GetAllUsersProgressAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all users progress");
                return StatusCode(500, new { error = "Failed to get users progress" });
            }
        }

        /// <summary>
        /// Get detailed data for a specific material
        /// GET /api/{tenantName}/users/{userId}/materials/{materialId}
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/{userId}/materials/{materialId}")]
        // [Authorize] - Disabled for development
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
        // [Authorize] - Disabled for development
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
            // Log all claims for debugging
            _logger.LogInformation("Token claims: {Claims}",
                string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));

            // Get user ID from JWT claims - prefer preferred_username over sub (UUID)
            var userId = User.FindFirst("preferred_username")?.Value
                ?? User.FindFirst(ClaimTypes.Name)?.Value
                ?? User.FindFirst("name")?.Value
                ?? User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            // Authorization disabled - return default admin user if not authenticated
            if (string.IsNullOrEmpty(userId))
            {
                userId = "demoadmin";
                _logger.LogInformation("No auth token - using default admin user");
            }

            _logger.LogInformation("Extracted userId: {UserId}", userId);
            return userId;
        }
    }
}
