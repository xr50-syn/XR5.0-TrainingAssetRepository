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
    [Route("api/{tenantName}/program-progress")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "users")]
    public class ProgramProgressController : ControllerBase
    {
        private readonly IUserMaterialService _userMaterialService;
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<ProgramProgressController> _logger;

        public ProgramProgressController(
            IUserMaterialService userMaterialService,
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<ProgramProgressController> logger)
        {
            _userMaterialService = userMaterialService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        /// <summary>
        /// Get progress for a specific training program.
        /// Admins see all users' progress, regular users see only their own.
        /// </summary>
        [HttpGet("program/{programId}")]
        // [Authorize] - Disabled for development
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
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting program progress for {ProgramId}", programId);
                return StatusCode(500, new { error = "Failed to retrieve program progress" });
            }
        }

        /// <summary>
        /// Gets user ID from JWT claims and admin status from database
        /// </summary>
        private async Task<(string? userId, bool isAdmin)> GetUserContextAsync()
        {
            // Extract user ID from JWT claims
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
                _logger.LogInformation("No auth token - using default admin user");
                return ("admin", true);
            }

            // Check admin status from database
            using var context = _dbContextFactory.CreateDbContext();
            var user = await context.Users.FirstOrDefaultAsync(u => u.UserName == userId);

            // Authorization disabled - treat all authenticated users as admin
            var isAdmin = true; // user?.admin ?? false;

            _logger.LogDebug("User {UserId} admin status: {IsAdmin}", userId, isAdmin);

            return (userId, isAdmin);
        }
    }
}
