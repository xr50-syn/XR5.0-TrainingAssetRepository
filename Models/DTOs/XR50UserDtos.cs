using System.ComponentModel.DataAnnotations;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Models.DTOs
{
    /// <summary>
    /// The tenant roles this service recognizes. The XR5.0 Hub session token carries no role
    /// information, so membership is held here and granted through this API; system
    /// administration (Users.admin) is deliberately not settable through the tenant surface.
    /// </summary>
    public static class TenantRoles
    {
        /// <summary>Read training content and record own progress/quiz scores.</summary>
        public const string Member = "member";

        /// <summary>Full access within the tenant: authoring, user management, role grants.</summary>
        public const string TenantAdmin = "tenantadmin";

        public static bool IsKnown(string? role) =>
            string.Equals(role, Member, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, TenantAdmin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A tenant user as returned by the API. Deliberately omits the stored password, which the
    /// User entity carries but no caller has any business reading.
    /// </summary>
    public class TenantUserResponse
    {
        public string UserName { get; set; } = "";
        public string? FullName { get; set; }
        public string? UserEmail { get; set; }

        /// <summary>System administrator (Users.admin); grants access across tenants.</summary>
        public bool Admin { get; set; }

        /// <summary>Tenant administrator (TenantAdmins membership) for this tenant.</summary>
        public bool IsTenantAdmin { get; set; }

        /// <summary>Effective tenant role: "tenantadmin" when an admin, otherwise "member".</summary>
        public string Role => IsTenantAdmin ? TenantRoles.TenantAdmin : TenantRoles.Member;

        public static TenantUserResponse From(User user, bool isTenantAdmin) => new()
        {
            UserName = user.UserName,
            FullName = user.FullName,
            UserEmail = user.UserEmail,
            Admin = user.admin,
            IsTenantAdmin = isTenantAdmin,
        };
    }

    /// <summary>
    /// What the service resolved the caller's credential to (GET api/auth/me). Carries no
    /// secrets: the token itself, the session id and the shared secret never appear here.
    /// </summary>
    public class CurrentIdentityResponse
    {
        public bool Authenticated { get; set; }

        /// <summary>Scheme that authenticated the request ("XR50Hub", "Bearer", …).</summary>
        public string? AuthenticationScheme { get; set; }

        /// <summary>Local user the credential joined to; also how writes are attributed.</summary>
        public string? UserName { get; set; }

        /// <summary>Hub userId - the value to provision/grant against for Hub identities.</summary>
        public string? HubUserId { get; set; }

        /// <summary>Hub tenantId from the token, before mapping to a local tenant.</summary>
        public Guid? HubTenantId { get; set; }

        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? SkillLevel { get; set; }

        /// <summary>Local tenant the Hub tenantId mapped to; null when nothing is mapped.</summary>
        public string? TenantName { get; set; }

        public string Role { get; set; } = TenantRoles.Member;
        public bool IsTenantAdmin { get; set; }
        public bool IsSystemAdmin { get; set; }
    }

    /// <summary>Body of PUT api/{tenantName}/users/{userName}/role.</summary>
    public class SetUserRoleRequest
    {
        /// <summary>"member" or "tenantadmin".</summary>
        [Required]
        public string Role { get; set; } = "";
    }
}
