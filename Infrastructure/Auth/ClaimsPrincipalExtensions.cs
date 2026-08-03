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

        /// <summary>Tenant the token is scoped to, per the configured tenant claim.</summary>
        public static string? GetTenantName(this ClaimsPrincipal user, IamOptions options)
        {
            return user.FindFirst(options.TenantClaim)?.Value;
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
