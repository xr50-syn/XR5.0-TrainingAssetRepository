using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;
using XR50TrainingAssetRepo.Infrastructure.ErrorHandling;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/[controller]")]
    [Authorize(Policy = "TenantMember")]
    [ApiController]
    public class UsersProgressController : ControllerBase
    {
        private readonly IUserMaterialService _userMaterialService;
        private readonly ILogger<UsersProgressController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IamOptions _iamOptions;

        public UsersProgressController(
            IUserMaterialService userMaterialService,
            ILogger<UsersProgressController> logger,
            IWebHostEnvironment environment,
            IOptions<IamOptions> iamOptions)
        {
            _userMaterialService = userMaterialService;
            _logger = logger;
            _environment = environment;
            _iamOptions = iamOptions.Value;
        }

        /// <summary>
        /// A member owns their progress and nobody else's: reading another user's records needs
        /// a tenant-administration role. Returns null when the caller is allowed to proceed.
        /// </summary>
        private ActionResult? DenyIfNotOwnProgress(string userId)
        {
            if (User.CanActForUser(userId, _iamOptions, _environment))
            {
                return null;
            }

            _logger.LogWarning("Rejected cross-user progress access by {Actor}", User.GetUserId());
            return this.ProblemForbidden("You can only access your own progress.");
        }

        /// <summary>
        /// Get overall progress for a specific user
        /// GET /api/{tenantName}/users/{userId}/progress
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/{userId}/progress")]
        public async Task<ActionResult<UserProgressResponse>> GetUserProgressByUserId(
            string tenantName,
            string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    return this.ProblemBadRequest("userId is required.");
                }

                if (DenyIfNotOwnProgress(userId) is { } denied)
                {
                    return denied;
                }

                var result = await _userMaterialService.GetUserProgressAsync(userId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "User not found");
                return this.ProblemNotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user progress");
                return this.ProblemServerError("Failed to get progress.");
            }
        }

        /// <summary>
        /// Get overall progress for all users. Tenant-wide visibility is a management view,
        /// so it needs a tenant-administration role; members read their own progress instead.
        /// GET /api/{tenantName}/users/progress
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/progress")]
        [Authorize(Policy = "TenantAdmin")]
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
                return this.ProblemServerError("Failed to get users progress.");
            }
        }

        /// <summary>
        /// Get detailed data for a specific material
        /// GET /api/{tenantName}/users/{userId}/materials/{materialId}
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/{userId}/materials/{materialId}")]
        public async Task<ActionResult<UserMaterialDetailResponse>> GetUserMaterialDetail(
            string tenantName,
            string userId,
            int materialId)
        {
            try
            {
                if (DenyIfNotOwnProgress(userId) is { } denied)
                {
                    return denied;
                }

                var result = await _userMaterialService.GetUserMaterialDetailAsync(userId, materialId);

                if (result == null)
                {
                    return this.ProblemNotFound("No data found for this user/material combination.");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting material detail for user {UserId}", userId);
                return this.ProblemServerError("Failed to get material detail.");
            }
        }

        /// <summary>
        /// Get all materials with detailed data for a program
        /// GET /api/{tenantName}/users/{userId}/programs/{programId}/materials
        /// </summary>
        [HttpGet("~/api/{tenantName}/users/{userId}/programs/{programId}/materials")]
        public async Task<ActionResult<UserProgramMaterialsResponse>> GetUserProgramMaterials(
            string tenantName,
            string userId,
            int programId)
        {
            try
            {
                if (DenyIfNotOwnProgress(userId) is { } denied)
                {
                    return denied;
                }

                var result = await _userMaterialService.GetUserProgramMaterialsAsync(userId, programId);

                if (result == null)
                {
                    return this.ProblemNotFound("Program not found or no data for user.");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting program materials for user {UserId}", userId);
                return this.ProblemServerError("Failed to get program materials.");
            }
        }

    }
}
