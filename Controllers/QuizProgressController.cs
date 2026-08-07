using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Materials;
using XR50TrainingAssetRepo.Infrastructure.ErrorHandling;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/quiz-progress")]
    [Authorize(Policy = "TenantMember")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "users")]
    public class QuizProgressController : ControllerBase
    {
        private readonly IQuizProgressService _quizProgressService;
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<QuizProgressController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IamOptions _iamOptions;

        public QuizProgressController(
            IQuizProgressService quizProgressService,
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<QuizProgressController> logger,
            IWebHostEnvironment environment,
            IOptions<IamOptions> iamOptions)
        {
            _quizProgressService = quizProgressService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _environment = environment;
            _iamOptions = iamOptions.Value;
        }

        /// <summary>
        /// Get all quiz progress in tenant (admins see all users, users see only their own)
        /// </summary>
        [HttpGet("tenant")]
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
                return this.ProblemServerError("Failed to retrieve quiz progress.");
            }
        }

        /// <summary>
        /// Get quiz progress for a specific training program
        /// </summary>
        [HttpGet("program/{programId}")]
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
                return this.ProblemNotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting program quiz progress for {ProgramId}", programId);
                return this.ProblemServerError("Failed to retrieve quiz progress.");
            }
        }

        /// <summary>
        /// Get quiz progress for a specific learning path
        /// </summary>
        [HttpGet("learning-path/{learningPathId}")]
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
                return this.ProblemNotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting learning path quiz progress for {LearningPathId}", learningPathId);
                return this.ProblemServerError("Failed to retrieve quiz progress.");
            }
        }

        /// <summary>
        /// Get quiz progress for a specific material
        /// </summary>
        [HttpGet("material/{materialId}")]
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
                return this.ProblemNotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting material quiz progress for {MaterialId}", materialId);
                return this.ProblemServerError("Failed to retrieve quiz progress.");
            }
        }

        /// <summary>
        /// Gets user ID from JWT claims and admin status from database
        /// </summary>
        private async Task<(string? userId, bool isAdmin)> GetUserContextAsync()
        {
            // The development user only applies under the Development anonymous bypass; without
            // a resolvable identity there is no progress to scope to.
            var userId = User.GetEffectiveUserId(_iamOptions, _environment);

            _logger.LogDebug("Extracted userId from claims: {UserId}", userId);

            if (string.IsNullOrEmpty(userId))
            {
                return (null, false);
            }

            // Tenant-wide visibility follows the tenant-administration role (the managers' view).
            // The Users.admin flag stays honored for principals whose roles are not in the token.
            if (User.CanReadOthersProgress(_iamOptions, _environment))
            {
                return (userId, true);
            }

            using var context = _dbContextFactory.CreateDbContext();
            var user = await context.Users.FirstOrDefaultAsync(u => u.UserName == userId);

            var isAdmin = user?.admin ?? false;

            _logger.LogDebug("User {UserId} admin status: {IsAdmin}", userId, isAdmin);

            return (userId, isAdmin);
        }
    }
}
