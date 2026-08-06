using System.Security.Claims;

namespace XR50TrainingAssetRepo.Infrastructure.Auth
{
    /// <summary>
    /// Centralizes claim extraction so controllers do not each carry their own fallback chains.
    /// </summary>
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Resolves a human-usable user id from the token.
        /// Fallback order: preferred_username -> name -> email -> sub (UUID).
        /// Returns null for anonymous principals (callers decide whether the development
        /// bypass user applies).
        /// </summary>
        public static string? GetUserId(this ClaimsPrincipal user)
        {
            return user.FindFirst("preferred_username")?.Value
                ?? user.FindFirst(ClaimTypes.Name)?.Value
                ?? user.FindFirst("name")?.Value
                ?? user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.FindFirst("email")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
        }

        /// <summary>
        /// User id for attributing writes (progress records, submissions). Falls back to the
        /// configured development user ONLY under the Development anonymous bypass - the same
        /// condition the authorization handlers use. Returns null when there is no usable
        /// identity; callers must reject the request rather than invent one.
        /// </summary>
        public static string? GetEffectiveUserId(
            this ClaimsPrincipal user, IamOptions options, IWebHostEnvironment environment)
        {
            var userId = user.GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                return userId;
            }

            return environment.IsDevelopment() && options.AllowAnonymousInDevelopment
                ? options.DevelopmentUserId
                : null;
        }

        /// <summary>
        /// Whether the caller may see or touch progress recorded against another user. Members
        /// are confined to their own records; tenant and system administrators see the whole
        /// tenant. The Development anonymous bypass passes, as it does in the policy handlers.
        /// </summary>
        public static bool CanReadOthersProgress(
            this ClaimsPrincipal user, IamOptions options, IWebHostEnvironment environment)
        {
            return (environment.IsDevelopment() && options.AllowAnonymousInDevelopment)
                || user.IsTenantAdmin(options)
                || user.IsSystemAdmin(options);
        }

        /// <summary>
        /// Whether the caller may act on <paramref name="userId"/>'s progress: their own always,
        /// anyone else's only with a tenant-administration role.
        /// </summary>
        public static bool CanActForUser(
            this ClaimsPrincipal user, string? userId, IamOptions options, IWebHostEnvironment environment)
        {
            if (user.CanReadOthersProgress(options, environment))
            {
                return true;
            }

            var self = user.GetUserId();
            return !string.IsNullOrEmpty(self)
                && string.Equals(self, userId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Tenant the token is scoped to, per the configured tenant claim.</summary>
        public static string? GetTenantName(this ClaimsPrincipal user, IamOptions options)
        {
            return user.FindFirst(options.TenantClaim)?.Value;
        }

        /// <summary>
        /// The Hub tenant id (tenantId claim of the decrypted session token), present only on
        /// principals authenticated through the XR50Hub scheme. Null for JWT/Keycloak principals.
        /// </summary>
        public static Guid? GetHubTenantId(this ClaimsPrincipal user)
        {
            return Guid.TryParse(user.FindFirst(HubSessionTokenDefaults.HubTenantIdClaim)?.Value, out var id)
                ? id
                : null;
        }

        /// <summary>True when the principal was authenticated by the XR5.0 Hub session token scheme.</summary>
        public static bool IsHubAuthenticated(this ClaimsPrincipal user)
        {
            return user.Identity?.IsAuthenticated == true
                && user.Identity.AuthenticationType == HubSessionTokenDefaults.SchemeName
                && user.GetHubTenantId() != null;
        }

        public static bool IsSystemAdmin(this ClaimsPrincipal user, IamOptions options)
        {
            return HasAnyRole(user, options, options.SystemAdminRoles);
        }

        public static bool IsTenantAdmin(this ClaimsPrincipal user, IamOptions options)
        {
            return HasAnyRole(user, options, options.TenantAdminRoles);
        }

        private static bool HasAnyRole(ClaimsPrincipal user, IamOptions options, string[] roles)
        {
            return user.FindAll(options.RoleClaim)
                .Concat(user.FindAll(ClaimTypes.Role))
                .Any(c => roles.Contains(c.Value, StringComparer.OrdinalIgnoreCase));
        }
    }
}
