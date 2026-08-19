using System.Text.RegularExpressions;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Single source of truth for how a tenant name maps onto MySQL identifiers.
    /// Every derivation of a per-tenant database name (or a name-derived identifier
    /// like the default AI collection) must go through this class, so the mapping
    /// can never drift between provisioning, lookup, migration and diagnostics.
    /// </summary>
    public static class XR50TenantDatabase
    {
        public const string SchemaPrefix = "xr50_tenant_";

        /// <summary>
        /// Characters accepted in a tenant name: those that survive <see cref="Sanitize"/>
        /// unchanged, plus '-' so UUIDs can be used as tenant names. '-' folds to '_' in the
        /// derived database name, so distinct names can still collide ("foo-bar" vs "foo_bar");
        /// TenantsController rejects that with a 409 at creation time by checking whether the
        /// derived database already exists.
        /// </summary>
        private static readonly Regex AcceptedTenantNamePattern =
            new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

        public static bool IsAcceptableTenantName(string tenantName) =>
            AcceptedTenantNamePattern.IsMatch(tenantName);

        /// <summary>Folds a tenant name to the MySQL-identifier-safe form used in database names.</summary>
        public static string Sanitize(string tenantName) =>
            Regex.Replace(tenantName, @"[^a-zA-Z0-9_]", "_");

        /// <summary>The per-tenant database (schema) name for a tenant.</summary>
        public static string SchemaFor(string tenantName) => $"{SchemaPrefix}{Sanitize(tenantName)}";
    }

    /// <summary>
    /// An <see cref="IXR50TenantService"/> pinned to one tenant, for code paths that have no
    /// HttpContext to resolve the current tenant from (background services, initializers,
    /// diagnostics). Existence checks are stubbed to true: callers already know which tenant
    /// they are operating on.
    /// </summary>
    public sealed class DirectTenantService : IXR50TenantService
    {
        private readonly string _tenantName;

        public DirectTenantService(string tenantName)
        {
            _tenantName = tenantName;
        }

        public string GetCurrentTenant() => _tenantName;
        public Task<bool> ValidateTenantAsync(string tenantName) => Task.FromResult(true);
        public Task<bool> TenantExistsAsync(string tenantName) => Task.FromResult(true);
        public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
        public string GetTenantSchema(string tenantName) => XR50TenantDatabase.SchemaFor(tenantName);
    }
}
