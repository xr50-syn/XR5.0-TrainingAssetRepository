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
    [Route("api/{tenantName}/program-progress")]
    [Authorize(Policy = "TenantMember")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "users")]
    public class ProgramProgressController : ControllerBase
    {
        private readonly IUserMaterialService _userMaterialService;
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<ProgramProgressController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IamOptions _iamOptions;

        public ProgramProgressController(
            IUserMaterialService userMaterialService,
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<ProgramProgressController> logger,
            IWebHostEnvironment environment,
            IOptions<IamOptions> iamOptions)
        {
            _userMaterialService = userMaterialService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _environment = environment;
            _iamOptions = iamOptions.Value;
        }

        /// <summary>
        /// Get progress for a specific training program.
        /// Admins see all users' progress, regular users see only their own.
        /// </summary>
        [HttpGet("program/{programId}")]
        public async Task<ActionResult<ProgramProgressResponse>> GetProgramProgress(
            string tenantName,
            int programId)
        {
            try
            {
                var (userId, isAdmin) = await GetUserContextAsync();

                _logger.LogInformation(
                    "Getting program {ProgramId} progress for tenant {TenantName}. User: {UserId}, IsAdmin: {IsAdmin}",
                    programId, tenantName, userId, isAdmin);

                var result = await _userMaterialService.GetProgramProgressAsync(
                    programId, userId, isAdmin);

                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("Program {ProgramId} not found: {Message}", programId, ex.Message);
                return this.ProblemNotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting program progress for {ProgramId}", programId);
                return this.ProblemServerError("Failed to retrieve program progress.");
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
