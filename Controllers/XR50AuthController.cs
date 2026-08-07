using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Infrastructure.Auth;
using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Controllers
{
    /// <summary>
    /// Reports how the current credential resolved against our own tables. Because the XR5.0 Hub
    /// session token carries no roles, users and their roles are kept on both sides; this is the
    /// endpoint that tells you what the local side made of a token - which user row it joined to,
    /// which tenant it mapped to, and which role it ended up with. It is also the simplest way to
    /// obtain the Hub user id a tenant administrator needs in order to grant a role.
    /// </summary>
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IamOptions _iamOptions;

        public AuthController(IOptions<IamOptions> iamOptions)
        {
            _iamOptions = iamOptions.Value;
        }

        // GET: api/auth/me
        [HttpGet("me")]
        public ActionResult<CurrentIdentityResponse> Me()
        {
            var isTenantAdmin = User.IsTenantAdmin(_iamOptions);
            var isSystemAdmin = User.IsSystemAdmin(_iamOptions);

            return new CurrentIdentityResponse
            {
                Authenticated = User.Identity?.IsAuthenticated == true,
                AuthenticationScheme = User.Identity?.AuthenticationType,
                UserName = User.GetUserId(),
                HubUserId = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                HubTenantId = User.GetHubTenantId(),
                FullName = User.FindFirst("name")?.Value,
                Email = User.FindFirst("email")?.Value,
                SkillLevel = User.FindFirst(HubSessionTokenDefaults.SkillLevelClaim)?.Value,
                TenantName = User.GetTenantName(_iamOptions),
                Role = isTenantAdmin ? TenantRoles.TenantAdmin : TenantRoles.Member,
                IsTenantAdmin = isTenantAdmin,
                IsSystemAdmin = isSystemAdmin,
            };
        }
    }
}
