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
    /// the token's tenantId, and the tenant-database user matched by Hub userId (e-mail as
    /// fallback), from which the tenant-admin (TenantAdmins membership) and system-admin
    /// (Users.admin) roles are derived. The Hub authenticates the user and owns the profile;
    /// authorization stays grounded in our own tables. Since the Hub token carries no roles, the
    /// two systems must agree on who exists: unknown identities are provisioned here as plain
    /// members, leaving administrators only the role grant to make.
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
                return await ResolveRolesAsync(tenantName, databaseName, claims, cancellationToken);
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
            string tenantName, string databaseName, HubClaims claims, CancellationToken cancellationToken)
        {
            var hubUserId = claims.UserId.ToString("D");
            var email = claims.User.Email;
            var fullName = BuildFullName(claims.User);

            var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
            var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, databaseName);

            using var connection = new MySqlConnection(tenantConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Primary join: the Hub userId GUID against Users.UserName - the only identifier
            // every Hub identity carries (service accounts have no e-mail). Fallback for human
            // users provisioned by address before GUID-keying: case-insensitive e-mail match.
            var match = await FindUserAsync(connection, @"
                SELECT UserName, admin, FullName, UserEmail
                FROM Users
                WHERE LOWER(UserName) = LOWER(@key)", hubUserId, cancellationToken);

            if (match != null)
            {
                // The row is ours to keep current: the Hub owns the profile, we own the roles.
                await SyncProfileAsync(connection, match.Value, fullName, email, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(email))
            {
                // UserEmail has no unique index; take the first user by name so repeated
                // logins resolve deterministically.
                match = await FindUserAsync(connection, @"
                    SELECT UserName, admin, FullName, UserEmail
                    FROM Users
                    WHERE UserEmail IS NOT NULL AND LOWER(UserEmail) = LOWER(@key)
                    ORDER BY UserName
                    LIMIT 1", email, cancellationToken);
            }

            if (match == null && _options.AutoProvisionUsers)
            {
                match = await ProvisionUserAsync(connection, tenantName, hubUserId, fullName, email, cancellationToken);
            }

            if (match == null)
            {
                _logger.LogDebug("No local user matches the Hub identity in tenant {TenantName}", tenantName);
                return new HubLocalIdentity(tenantName, null, false, false);
            }

            var (userName, isSystemAdmin, _, _) = match.Value;

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

        /// <summary>
        /// Creates the local counterpart of a Hub identity, keyed by the Hub userId so that
        /// e-mail-less service accounts join as reliably as human users. The row is a plain
        /// member with no password: it exists so administrators have something to grant a role
        /// to, and so writes (progress, submissions) attribute to a known user.
        /// </summary>
        private async Task<(string UserName, bool IsSystemAdmin, string? FullName, string? Email)?> ProvisionUserAsync(
            MySqlConnection connection, string tenantName, string hubUserId, string? fullName, string? email,
            CancellationToken cancellationToken)
        {
            var sql = @"
                INSERT IGNORE INTO `Users` (`UserName`, `FullName`, `UserEmail`, `Password`, `admin`)
                VALUES (@userName, @fullName, @userEmail, NULL, 0)";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@userName", hubUserId);
            command.Parameters.AddWithValue("@fullName", (object?)fullName ?? DBNull.Value);
            command.Parameters.AddWithValue("@userEmail", (object?)email ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation(
                "Provisioned local user for Hub identity {HubUserId} in tenant {TenantName} (member; roles must be granted)",
                hubUserId, tenantName);

            return (hubUserId, false, fullName, email);
        }

        /// <summary>
        /// Keeps the display fields of a GUID-keyed row in step with the Hub profile. Writes
        /// only when a value actually changed, so the common request path stays read-only.
        /// </summary>
        private static async Task SyncProfileAsync(
            MySqlConnection connection,
            (string UserName, bool IsSystemAdmin, string? FullName, string? Email) user,
            string? fullName, string? email, CancellationToken cancellationToken)
        {
            var nameChanged = !string.IsNullOrEmpty(fullName) && !string.Equals(user.FullName, fullName, StringComparison.Ordinal);
            var emailChanged = !string.IsNullOrEmpty(email) && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase);
            if (!nameChanged && !emailChanged)
            {
                return;
            }

            var sql = @"
                UPDATE `Users`
                SET `FullName` = IF(@syncName, @fullName, `FullName`),
                    `UserEmail` = IF(@syncEmail, @userEmail, `UserEmail`)
                WHERE `UserName` = @userName";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@syncName", nameChanged);
            command.Parameters.AddWithValue("@syncEmail", emailChanged);
            command.Parameters.AddWithValue("@fullName", (object?)fullName ?? DBNull.Value);
            command.Parameters.AddWithValue("@userEmail", (object?)email ?? DBNull.Value);
            command.Parameters.AddWithValue("@userName", user.UserName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static string? BuildFullName(HubUser user)
        {
            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            return string.IsNullOrEmpty(fullName) ? null : fullName;
        }

        private static async Task<(string UserName, bool IsSystemAdmin, string? FullName, string? Email)?> FindUserAsync(
            MySqlConnection connection, string sql, string key, CancellationToken cancellationToken)
        {
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@key", key);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                var userName = reader["UserName"]?.ToString();
                if (!string.IsNullOrEmpty(userName))
                {
                    return (
                        userName,
                        Convert.ToBoolean(reader["admin"]),
                        reader["FullName"] as string,
                        reader["UserEmail"] as string);
                }
            }

            return null;
        }
    }
}
