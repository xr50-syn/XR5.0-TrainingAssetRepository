using MySql.Data.MySqlClient;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services.Migrations;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Tenant database lifecycle: create the database and bring it to the committed schema
    /// through <see cref="IXR50SchemaMigrator"/>, register the tenant centrally, seed its owner,
    /// and drop it again on deletion.
    /// </summary>
    public class XR50MigrationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<XR50MigrationService> _logger;
        private readonly IXR50SchemaMigrator _schemaMigrator;
        private readonly ISchemaInspector _schemaInspector;

        public XR50MigrationService(
            IConfiguration configuration,
            ILogger<XR50MigrationService> logger,
            IXR50SchemaMigrator schemaMigrator,
            ISchemaInspector schemaInspector)
        {
            _configuration = configuration;
            _logger = logger;
            _schemaMigrator = schemaMigrator;
            _schemaInspector = schemaInspector;
        }

        public async Task CreateTenantDatabaseAsync(XR50Tenant tenant)
        {
            var tenantDbName = GetTenantDatabase(tenant.TenantName);
            _logger.LogInformation("Creating tenant database {TenantDatabase} for tenant {TenantName}", tenantDbName, tenant.TenantName);

            // Only a database this call created is dropped if provisioning fails. The controller
            // refuses colliding names before getting here, but a pre-existing database must never
            // be destroyed by a failed re-provisioning attempt.
            var existedBefore = await _schemaInspector.DatabaseExistsAsync(tenantDbName);

            using var connection = new MySqlConnection(AdminConnectionString());
            await connection.OpenAsync();

            try
            {
                var createDbCommand = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{tenantDbName}`", connection);
                await createDbCommand.ExecuteNonQueryAsync();

                var migration = await _schemaMigrator.MigrateTenantAsync(tenant.TenantName);
                if (!migration.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Schema migration of tenant database {tenantDbName} failed ({migration.StateBefore}): {migration.Error}");
                }

                var tables = await _schemaInspector.ListTablesAsync(tenantDbName);
                _logger.LogInformation("Tenant database {TenantDatabase} is at the current schema with {TableCount} tables ({Applied} migration(s) applied)",
                    tenantDbName, tables.Count, migration.AppliedNow.Count);

                await StoreTenantMetadataInCentralRegistry(tenant, tenantDbName);
                _logger.LogInformation("Completed tenant creation for {TenantName}", tenant.TenantName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tenant database {TenantDatabase}", tenantDbName);

                if (!existedBefore)
                {
                    try
                    {
                        var dropDbCommand = new MySqlCommand($"DROP DATABASE IF EXISTS `{tenantDbName}`", connection);
                        await dropDbCommand.ExecuteNonQueryAsync();
                        _logger.LogInformation("Cleaned up partially provisioned database {TenantDatabase}", tenantDbName);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx, "Failed to clean up database {TenantDatabase} after creation failure", tenantDbName);
                    }
                }

                throw;
            }
        }

        /// <summary>
        /// Drops and recreates the tenant database from the committed schema. Destroys all data;
        /// exposed only through the development-only rebuild endpoint.
        /// </summary>
        public async Task<bool> RepairTenantDatabaseAsync(string tenantName)
        {
            try
            {
                var tenantDbName = GetTenantDatabase(tenantName);
                _logger.LogWarning("Rebuilding tenant database {TenantDatabase} from scratch", tenantDbName);

                using (var connection = new MySqlConnection(AdminConnectionString()))
                {
                    await connection.OpenAsync();
                    await new MySqlCommand($"DROP DATABASE IF EXISTS `{tenantDbName}`", connection).ExecuteNonQueryAsync();
                    await new MySqlCommand($"CREATE DATABASE `{tenantDbName}`", connection).ExecuteNonQueryAsync();
                }

                var migration = await _schemaMigrator.MigrateTenantAsync(tenantName);
                if (!migration.Succeeded)
                {
                    _logger.LogError("Rebuild of tenant database {TenantDatabase} failed: {Error}", tenantDbName, migration.Error);
                    return false;
                }

                var tables = await _schemaInspector.ListTablesAsync(tenantDbName);
                _logger.LogInformation("Rebuilt tenant {TenantName} with {TableCount} tables", tenantName, tables.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild tenant database for {TenantName}", tenantName);
                return false;
            }
        }

        private async Task StoreTenantMetadataInCentralRegistry(XR50Tenant tenant, string tenantDbName)
        {
            var baseConnectionString = XR50DatabaseSettings.BaseConnectionString(_configuration);

            using var connection = new MySqlConnection(baseConnectionString);
            await connection.OpenAsync();

            // The registry table itself is owned by XR50RegistryContext's migrations.
            var insertCommand = new MySqlCommand(@"
                INSERT INTO `XR50TenantRegistry`
                    (`TenantName`, `TenantGroup`, `Description`, `StorageType`, `TenantDirectory`,
                    `S3BucketName`, `S3BucketRegion`, `S3BucketArn`, `StorageEndpoint`,
                    `OwnerName`, `DefaultAICollection`, `InnovChatbotBaseUrl`, `InnovChatbotApiToken`,
                    `InnovChatbotDefaultPilot`, `HubTenantId`, `DatabaseName`, `CreatedAt`, `IsActive`)
                VALUES
                    (@tenantName, @tenantGroup, @description, @storageType, @tenantDirectory,
                    @s3BucketName, @s3BucketRegion, @s3BucketArn, @storageEndpoint,
                    @ownerName, @defaultAICollection, @innovChatbotBaseUrl, @innovChatbotApiToken,
                    @innovChatbotDefaultPilot, @hubTenantId, @databaseName, @createdAt, 1)
                ON DUPLICATE KEY UPDATE
                    `TenantGroup` = @tenantGroup,
                    `Description` = @description,
                    `StorageType` = @storageType,
                    `TenantDirectory` = @tenantDirectory,
                    `S3BucketName` = @s3BucketName,
                    `S3BucketRegion` = @s3BucketRegion,
                    `S3BucketArn` = @s3BucketArn,
                    `StorageEndpoint` = @storageEndpoint,
                    `OwnerName` = @ownerName,
                    `DefaultAICollection` = @defaultAICollection,
                    `InnovChatbotBaseUrl` = @innovChatbotBaseUrl,
                    `InnovChatbotApiToken` = @innovChatbotApiToken,
                    `InnovChatbotDefaultPilot` = @innovChatbotDefaultPilot,
                    `HubTenantId` = @hubTenantId,
                    `DatabaseName` = @databaseName", connection);

            insertCommand.Parameters.AddWithValue("@tenantName", tenant.TenantName ?? "");
            insertCommand.Parameters.AddWithValue("@tenantGroup", tenant.TenantGroup ?? "");
            insertCommand.Parameters.AddWithValue("@description", tenant.Description ?? "");
            insertCommand.Parameters.AddWithValue("@storageType", tenant.StorageType ?? "OwnCloud");
            insertCommand.Parameters.AddWithValue("@tenantDirectory", tenant.TenantDirectory ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@s3BucketName", tenant.S3BucketName ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@s3BucketRegion", tenant.S3BucketRegion ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@s3BucketArn", tenant.S3BucketArn ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@storageEndpoint", tenant.StorageEndpoint ?? (object)DBNull.Value);

            string ownerName = "";
            if (tenant.Owner != null && !string.IsNullOrEmpty(tenant.Owner.UserName))
            {
                ownerName = tenant.Owner.UserName;
                await CreateOwnerUserInTenantDatabase(tenant.Owner, tenantDbName);
            }
            else if (!string.IsNullOrEmpty(tenant.OwnerName))
            {
                ownerName = tenant.OwnerName;
                await CreateOwnerUserInTenantDatabase(tenant.Owner, tenantDbName);
            }
            insertCommand.Parameters.AddWithValue("@ownerName", ownerName);
            insertCommand.Parameters.AddWithValue("@defaultAICollection", tenant.DefaultAICollection ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@innovChatbotBaseUrl", tenant.InnovChatbotBaseUrl ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@innovChatbotApiToken", tenant.InnovChatbotApiToken ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@innovChatbotDefaultPilot", tenant.InnovChatbotDefaultPilot ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@hubTenantId", tenant.HubTenantId?.ToString("D") ?? (object)DBNull.Value);

            insertCommand.Parameters.AddWithValue("@databaseName", tenantDbName);
            insertCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);

            await insertCommand.ExecuteNonQueryAsync();
        }

        private async Task CreateOwnerUserInTenantDatabase(User? owner, string tenantDbName)
        {
            try
            {
                if (owner is null)
                {
                    return;
                }

                var tenantConnectionString = TenantConnectionString.ForDatabase(XR50DatabaseSettings.BaseConnectionString(_configuration), tenantDbName);

                _logger.LogInformation("Creating owner user {UserName} in tenant database {TenantDatabase}", owner.UserName, tenantDbName);

                using var connection = new MySqlConnection(tenantConnectionString);
                await connection.OpenAsync();

                var insertOwnerCommand = new MySqlCommand(@"
                    INSERT INTO `Users`
                        (`UserName`, `FullName`, `UserEmail`, `Password`, `admin`)
                    VALUES
                        (@userName, @fullName, @userEmail, @password, @admin)
                    ON DUPLICATE KEY UPDATE
                        `FullName` = @fullName,
                        `UserEmail` = @userEmail,
                        `Password` = @password,
                        `admin` = @admin", connection);

                insertOwnerCommand.Parameters.AddWithValue("@userName", owner.UserName ?? "");
                insertOwnerCommand.Parameters.AddWithValue("@fullName", owner.FullName ?? "");
                insertOwnerCommand.Parameters.AddWithValue("@userEmail", owner.UserEmail ?? "");
                insertOwnerCommand.Parameters.AddWithValue("@password", owner.Password ?? "");
                insertOwnerCommand.Parameters.AddWithValue("@admin", owner.admin);

                await insertOwnerCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create owner user {UserName} in tenant database {TenantDatabase}", owner?.UserName, tenantDbName);
                // Don't throw - tenant creation should continue even if owner user creation fails
            }
        }

        public async Task<bool> DeleteTenantDatabaseAsync(string tenantName)
        {
            try
            {
                var tenantDbName = GetTenantDatabase(tenantName);
                _logger.LogInformation("Deleting tenant database {TenantDatabase} for tenant {TenantName}", tenantDbName, tenantName);

                using var connection = new MySqlConnection(AdminConnectionString());
                await connection.OpenAsync();

                var dropDbCommand = new MySqlCommand($"DROP DATABASE IF EXISTS `{tenantDbName}`", connection);
                await dropDbCommand.ExecuteNonQueryAsync();

                _logger.LogInformation("Deleted tenant database {TenantDatabase}", tenantDbName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete tenant database for {TenantName}", tenantName);
                return false;
            }
        }

        private string AdminConnectionString() =>
            TenantConnectionString.ForDatabase(XR50DatabaseSettings.BaseConnectionString(_configuration), "mysql");

        private static string GetTenantDatabase(string tenantName) => XR50TenantDatabase.SchemaFor(tenantName);
    }
}
