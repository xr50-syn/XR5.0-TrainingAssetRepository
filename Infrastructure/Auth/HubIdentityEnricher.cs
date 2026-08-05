using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Infrastructure.Auth
{
    public interface IHubIdentityEnricher
    {
        Task<HubLocalIdentity> ResolveAsync(HubClaims claims, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Maps a decrypted Hub identity onto local data: the registry row whose HubTenantId matches
    /// the token's tenantId, and the tenant-database user matched by email, from which the
    /// tenant-admin (TenantAdmins membership) and system-admin (Users.admin) roles are derived.
    /// The Hub authenticates the user; authorization stays grounded in our own registry.
    /// </summary>
    public class HubIdentityEnricher : IHubIdentityEnricher
    {
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<HubIdentityEnricher> _logger;
        private readonly XR50HubOptions _options;

        public HubIdentityEnricher(
            IXR50TenantManagementService tenantManagementService,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<HubIdentityEnricher> logger,
            IOptions<XR50HubOptions> options)
        {
            _tenantManagementService = tenantManagementService;
            _configuration = configuration;
            _cache = cache;
            _logger = logger;
            _options = options.Value;
        }

        public async Task<HubLocalIdentity> ResolveAsync(HubClaims claims, CancellationToken cancellationToken = default)
        {
            var mapping = await ResolveTenantAsync(claims.TenantId);
            if (mapping == null)
            {
                _logger.LogWarning("No tenant registered for Hub tenantId {HubTenantId}; request will lack a tenant scope",
                    claims.TenantId);
                return HubLocalIdentity.Unmapped;
            }

            var (tenantName, databaseName) = mapping.Value;
            if (string.IsNullOrEmpty(databaseName))
            {
                return new HubLocalIdentity(tenantName, null, false, false);
            }

            try
            {
                return await ResolveRolesAsync(tenantName, databaseName, claims.UserId, claims.User.Email, cancellationToken);
            }
            catch (Exception ex)
            {
                // Authentication still succeeds; the principal just carries no roles.
                _logger.LogWarning(ex, "Role lookup failed for Hub user in tenant {TenantName}", tenantName);
                return new HubLocalIdentity(tenantName, null, false, false);
            }
        }

        private async Task<(string TenantName, string? DatabaseName)?> ResolveTenantAsync(Guid hubTenantId)
        {
            var cacheKey = $"hubtenant:{hubTenantId:D}";
            if (_cache.TryGetValue(cacheKey, out (string, string?)? cachedMapping))
            {
                return cachedMapping;
            }

            (string, string?)? mapping = null;
            try
            {
                var tenant = await _tenantManagementService.GetTenantByHubTenantIdAsync(hubTenantId);
                if (tenant != null)
                {
                    mapping = (tenant.TenantName, tenant.TenantSchema);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Registry lookup failed for Hub tenantId {HubTenantId}", hubTenantId);
                return null; // do not cache failures
            }

            _cache.Set(cacheKey, mapping, TimeSpan.FromSeconds(Math.Max(1, _options.CacheSeconds)));
            return mapping;
        }

        private async Task<HubLocalIdentity> ResolveRolesAsync(
            string tenantName, string databaseName, Guid hubUserId, string? email, CancellationToken cancellationToken)
        {
            var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
            var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, databaseName);

            using var connection = new MySqlConnection(tenantConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Primary join: the Hub userId GUID against Users.UserName - the only identifier
            // every Hub identity carries (service accounts have no e-mail). Provision such
            // users locally with UserName = the Hub userId. Fallback for human users that were
            // provisioned before GUID-keying: case-insensitive e-mail match.
            var (userName, isSystemAdmin) = await FindUserAsync(connection, @"
                SELECT UserName, admin
                FROM Users
                WHERE LOWER(UserName) = LOWER(@key)", hubUserId.ToString("D"), cancellationToken);

            if (userName == null && !string.IsNullOrEmpty(email))
            {
                // UserEmail has no unique index; take the first user by name so repeated
                // logins resolve deterministically.
                (userName, isSystemAdmin) = await FindUserAsync(connection, @"
                    SELECT UserName, admin
                    FROM Users
                    WHERE UserEmail IS NOT NULL AND LOWER(UserEmail) = LOWER(@key)
                    ORDER BY UserName
                    LIMIT 1", email, cancellationToken);
            }

            if (userName == null)
            {
                _logger.LogDebug("No local user matches the Hub identity in tenant {TenantName}", tenantName);
                return new HubLocalIdentity(tenantName, null, false, false);
            }

            var adminSql = @"
                SELECT COUNT(*)
                FROM TenantAdmins
                WHERE TenantName = @tenantName AND UserName = @userName";

            using var adminCommand = new MySqlCommand(adminSql, connection);
            adminCommand.Parameters.AddWithValue("@tenantName", tenantName);
            adminCommand.Parameters.AddWithValue("@userName", userName);
            var isTenantAdmin = Convert.ToInt32(await adminCommand.ExecuteScalarAsync(cancellationToken)) > 0;

            return new HubLocalIdentity(tenantName, userName, isTenantAdmin, isSystemAdmin);
        }

        private static async Task<(string? UserName, bool IsSystemAdmin)> FindUserAsync(
            MySqlConnection connection, string sql, string key, CancellationToken cancellationToken)
        {
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@key", key);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                return (reader["UserName"]?.ToString(), Convert.ToBoolean(reader["admin"]));
            }

            return (null, false);
        }
    }
}
