namespace XR50TrainingAssetRepo.Infrastructure.Auth
{
    /// <summary>
    /// Provider-agnostic IAM settings bound from the "IAM" configuration section.
    /// The production identity provider is not decided yet (Keycloak is the development stand-in),
    /// so the claim names and role values the authorization handlers look at are configurable here
    /// rather than hardcoded to any one provider's token shape. Defaults match the bundled
    /// Keycloak realm (keycloak-config/xr50-realm.json).
    /// </summary>
    public class IamOptions
    {
        public const string SectionName = "IAM";

        /// <summary>Claim carrying the tenant the token is scoped to.</summary>
        public string TenantClaim { get; set; } = "tenantName";

        /// <summary>Claim carrying role values (may appear multiple times in the token).</summary>
        public string RoleClaim { get; set; } = "role";

        /// <summary>Role values that grant tenant-administration rights within the token's own tenant.</summary>
        public string[] TenantAdminRoles { get; set; } = new[] { "admin", "tenantadmin" };

        /// <summary>Role values that grant system-wide administration (cross-tenant, tenant provisioning).</summary>
        public string[] SystemAdminRoles { get; set; } = new[] { "systemadmin", "superadmin" };

        /// <summary>Development-only escape hatch: allow anonymous requests when the hosting environment is Development.</summary>
        public bool AllowAnonymousInDevelopment { get; set; }

        /// <summary>User id substituted for anonymous requests when the development bypass is active.</summary>
        public string DevelopmentUserId { get; set; } = "demoadmin";
    }
}
