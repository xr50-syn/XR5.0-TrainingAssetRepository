using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/quiz-progress")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "users")]
    public class QuizProgressController : ControllerBase
    {
        private readonly IQuizProgressService _quizProgressService;
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<QuizProgressController> _logger;

        public QuizProgressController(
            IQuizProgressService quizProgressService,
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<QuizProgressController> logger)
        {
            _quizProgressService = quizProgressService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        /// <summary>
        /// Get all quiz progress in tenant (admins see all users, users see only their own)
        /// </summary>
        [HttpGet("tenant")]
        [Authorize(Policy = "RequireAuthenticatedUser")]
        public async Task<ActionResult<TenantQuizProgressResponse>> GetTenantQuizProgress(
            string tenantName)
        {
            try
            {
                var (userId, isAdmin) = await GetUserContextAsync();

                _logger.LogInformation(
                    "Getting tenant quiz progress for {TenantName}. User: {UserId}, IsAdmin: {IsAdmin}",
                    tenantName, userId, isAdmin);

                var result = await _quizProgressService.GetTenantQuizProgressAsync(userId, isAdmin);
                result.TenantName = tenantName;

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tenant quiz progress for {TenantName}", tenantName);
                return StatusCode(500, new { Error = "Failed to retrieve quiz progress" });
            }
        }

        /// <summary>
        /// Get quiz progress for a specific training program
        /// </summary>
        [HttpGet("program/{programId}")]
        [Authorize(Policy = "RequireAuthenticatedUser")]
        public async Task<ActionResult<TrainingProgramQuizProgressResponse>> GetTrainingProgramQuizProgress(
            string tenantName,
            int programId)
        {
            try
            {
                var (userId, isAdmin) = await GetUserContextAsync();

                _logger.LogInformation(
                    "Getting program {ProgramId} quiz progress for tenant {TenantName}. User: {UserId}, IsAdmin: {IsAdmin}",
                    programId, tenantName, userId, isAdmin);

                var result = await _quizProgressService.GetTrainingProgramQuizProgressAsync(
                    programId, userId, isAdmin);

                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("Training program {ProgramId} not found: {Message}", programId, ex.Message);
                return NotFound(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting program quiz progress for {ProgramId}", programId);
                return StatusCode(500, new { Error = "Failed to retrieve quiz progress" });
            }
        }

        /// <summary>
        /// Get quiz progress for a specific learning path
        /// </summary>
        [HttpGet("learning-path/{learningPathId}")]
        [Authorize(Policy = "RequireAuthenticatedUser")]
        public async Task<ActionResult<LearningPathQuizProgressResponse>> GetLearningPathQuizProgress(
            string tenantName,
            int learningPathId)
        {
            try
            {
                var (userId, isAdmin) = await GetUserContextAsync();

                _logger.LogInformation(
                    "Getting learning path {LearningPathId} quiz progress for tenant {TenantName}. User: {UserId}, IsAdmin: {IsAdmin}",
                    learningPathId, tenantName, userId, isAdmin);

                var result = await _quizProgressService.GetLearningPathQuizProgressAsync(
                    learningPathId, userId, isAdmin);

                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("Learning path {LearningPathId} not found: {Message}", learningPathId, ex.Message);
                return NotFound(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting learning path quiz progress for {LearningPathId}", learningPathId);
                return StatusCode(500, new { Error = "Failed to retrieve quiz progress" });
            }
        }

        /// <summary>
        /// Get quiz progress for a specific material
        /// </summary>
        [HttpGet("material/{materialId}")]
        [Authorize(Policy = "RequireAuthenticatedUser")]
        public async Task<ActionResult<MaterialQuizProgressResponse>> GetMaterialQuizProgress(
            string tenantName,
            int materialId)
        {
            try
            {
                var (userId, isAdmin) = await GetUserContextAsync();

                _logger.LogInformation(
                    "Getting material {MaterialId} quiz progress for tenant {TenantName}. User: {UserId}, IsAdmin: {IsAdmin}",
                    materialId, tenantName, userId, isAdmin);

                var result = await _quizProgressService.GetMaterialQuizProgressAsync(
                    materialId, userId, isAdmin);

                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("Quiz material {MaterialId} not found: {Message}", materialId, ex.Message);
                return NotFound(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting material quiz progress for {MaterialId}", materialId);
                return StatusCode(500, new { Error = "Failed to retrieve quiz progress" });
            }
        }

        /// <summary>
        /// Gets user ID from JWT claims and admin status from database
        /// </summary>
        private async Task<(string? userId, bool isAdmin)> GetUserContextAsync()
        {
            // Extract user ID from JWT claims (same pattern as UsersProgressController)
            var userId = User.FindFirst("preferred_username")?.Value
                ?? User.FindFirst(ClaimTypes.Name)?.Value
                ?? User.FindFirst("name")?.Value
                ?? User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            _logger.LogDebug("Extracted userId from claims: {UserId}", userId);

            // Authorization disabled - return default admin user if not authenticated
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogInformation("No auth token - using default demoadmin user");
                return ("demoadmin", true);
            }

            // Check admin status from database
            using var context = _dbContextFactory.CreateDbContext();
            var user = await context.Users.FirstOrDefaultAsync(u => u.UserName == userId);

            var isAdmin = user?.admin ?? false;

            _logger.LogDebug("User {UserId} admin status: {IsAdmin}", userId, isAdmin);

            return (userId, isAdmin);
        }
    }
}
