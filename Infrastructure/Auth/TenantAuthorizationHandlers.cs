using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace XR50TrainingAssetRepo.Infrastructure.Auth
{
    /// <summary>
    /// Requires the token to belong to the tenant named in the {tenantName} route segment
    /// (system administrators are exempt from the tenant match).
    /// </summary>
    public class TenantMemberRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Requires tenant membership plus a tenant-administration role
    /// (system administrators are exempt from the tenant match).
    /// </summary>
    public class TenantAdminRequirement : IAuthorizationRequirement { }

    /// <summary>Requires a system-administration role.</summary>
    public class SystemAdminRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Gate for tenant creation. System administrators always qualify; additionally, any
    /// principal authenticated through the XR5.0 Hub scheme qualifies for SELF-SERVICE
    /// provisioning - the controller then binds the new tenant to the caller's own Hub
    /// tenant id, so a Hub user can only ever provision the tenant they belong to.
    /// JWT/Keycloak principals without a system-admin role stay excluded.
    /// </summary>
    public class TenantCreatorRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Requires any authenticated principal. Used as the global fallback policy so endpoints
    /// without an explicit [Authorize]/[AllowAnonymous] attribute still demand a valid token.
    /// </summary>
    public class AuthenticatedUserRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Shared plumbing for the tenant/role handlers: the single place the Development-only
    /// anonymous bypass is evaluated, plus tenant-route matching against the configured claims.
    /// </summary>
    public abstract class XR50AuthorizationHandler<TRequirement> : AuthorizationHandler<TRequirement>
        where TRequirement : IAuthorizationRequirement
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IHttpContextAccessor _httpContextAccessor;
        protected readonly IamOptions Options;

        protected XR50AuthorizationHandler(
            IWebHostEnvironment environment,
            IOptions<IamOptions> options,
            IHttpContextAccessor httpContextAccessor)
        {
            _environment = environment;
            _httpContextAccessor = httpContextAccessor;
            Options = options.Value;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TRequirement requirement)
        {
            if (DevelopmentBypassActive() || IsSatisfied(context))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }

        protected abstract bool IsSatisfied(AuthorizationHandlerContext context);

        /// <summary>Development-only anonymous escape hatch; the ONLY place it is evaluated.</summary>
        private bool DevelopmentBypassActive()
        {
            return _environment.IsDevelopment() && Options.AllowAnonymousInDevelopment;
        }

        /// <summary>
        /// True when the token's tenant claim matches the {tenantName} route value of the current
        /// request. Requests without a tenant route value fail closed for non-system-admins.
        /// </summary>
        protected bool TokenMatchesRouteTenant(AuthorizationHandlerContext context)
        {
            var httpContext = context.Resource as HttpContext ?? _httpContextAccessor.HttpContext;
            var routeTenant = httpContext?.GetRouteValue("tenantName") as string;
            if (string.IsNullOrEmpty(routeTenant))
            {
                return false;
            }

            var tokenTenant = context.User.GetTenantName(Options);
            return string.Equals(routeTenant, tokenTenant, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class TenantMemberHandler : XR50AuthorizationHandler<TenantMemberRequirement>
    {
        public TenantMemberHandler(
            IWebHostEnvironment environment,
            IOptions<IamOptions> options,
            IHttpContextAccessor httpContextAccessor)
            : base(environment, options, httpContextAccessor) { }

        protected override bool IsSatisfied(AuthorizationHandlerContext context)
        {
            return context.User.IsSystemAdmin(Options) || TokenMatchesRouteTenant(context);
        }
    }

    public class TenantAdminHandler : XR50AuthorizationHandler<TenantAdminRequirement>
    {
        public TenantAdminHandler(
            IWebHostEnvironment environment,
            IOptions<IamOptions> options,
            IHttpContextAccessor httpContextAccessor)
            : base(environment, options, httpContextAccessor) { }

        protected override bool IsSatisfied(AuthorizationHandlerContext context)
        {
            return context.User.IsSystemAdmin(Options)
                || (context.User.IsTenantAdmin(Options) && TokenMatchesRouteTenant(context));
        }
    }

    public class SystemAdminHandler : XR50AuthorizationHandler<SystemAdminRequirement>
    {
        public SystemAdminHandler(
            IWebHostEnvironment environment,
            IOptions<IamOptions> options,
            IHttpContextAccessor httpContextAccessor)
            : base(environment, options, httpContextAccessor) { }

        protected override bool IsSatisfied(AuthorizationHandlerContext context)
        {
            return context.User.IsSystemAdmin(Options);
        }
    }

    public class TenantCreatorHandler : XR50AuthorizationHandler<TenantCreatorRequirement>
    {
        public TenantCreatorHandler(
            IWebHostEnvironment environment,
            IOptions<IamOptions> options,
            IHttpContextAccessor httpContextAccessor)
            : base(environment, options, httpContextAccessor) { }

        protected override bool IsSatisfied(AuthorizationHandlerContext context)
        {
            return context.User.IsSystemAdmin(Options) || context.User.IsHubAuthenticated();
        }
    }

    public class AuthenticatedUserHandler : XR50AuthorizationHandler<AuthenticatedUserRequirement>
    {
        public AuthenticatedUserHandler(
            IWebHostEnvironment environment,
            IOptions<IamOptions> options,
            IHttpContextAccessor httpContextAccessor)
            : base(environment, options, httpContextAccessor) { }

        protected override bool IsSatisfied(AuthorizationHandlerContext context)
        {
            return context.User.Identity?.IsAuthenticated == true;
        }
    }
}
