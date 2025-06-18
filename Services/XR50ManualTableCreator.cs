using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Services
{
    public interface IXR50ManualTableCreator
    {
        Task<bool> CreateAllTablesAsync(string tenantName);
        Task<bool> CreateTablesInDatabaseAsync(string databaseName);
        Task<List<string>> GetExistingTablesAsync(string tenantName);
        Task<bool> DropAllTablesAsync(string tenantName);
    }

    public class XR50ManualTableCreator : IXR50ManualTableCreator
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<XR50ManualTableCreator> _logger;
        private readonly IXR50TenantService _tenantService;

        public XR50ManualTableCreator(
            IConfiguration configuration,
            ILogger<XR50ManualTableCreator> logger,
            IXR50TenantService tenantService)
        {
            _configuration = configuration;
            _logger = logger;
            _tenantService = tenantService;
        }

        public async Task<bool> CreateAllTablesAsync(string tenantName)
        {
            var tenantDbName = _tenantService.GetTenantSchema(tenantName);
            return await CreateTablesInDatabaseAsync(tenantDbName);
        }

        public async Task<bool> CreateTablesInDatabaseAsync(string databaseName)
        {
            try
            {
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                
                _logger.LogInformation("=== Creating tables in database: {DatabaseName} ===", databaseName);
                _logger.LogInformation("Base connection: {BaseConnection}", baseConnectionString.Replace("Password=", "Password=***"));
                _logger.LogInformation("Base database name: {BaseDatabaseName}", baseDatabaseName);
                
                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={databaseName}", StringComparison.OrdinalIgnoreCase);
                _logger.LogInformation("Target connection: {TargetConnection}", connectionString.Replace("Password=", "Password=***"));
                
                // Check if replacement worked
                if (connectionString == baseConnectionString)
                {
                    _logger.LogError("Connection string replacement FAILED!");
                    _logger.LogError("Looking for: 'database={BaseDatabaseName}' in connection string", baseDatabaseName);
                    _logger.LogError("Full base connection: {FullConnection}", baseConnectionString);
                    throw new InvalidOperationException($"Could not replace database name in connection string. Looking for 'database={baseDatabaseName}' in: {baseConnectionString}");
                }

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Verify which database we're actually connected to
                var currentDbCommand = new MySqlCommand("SELECT DATABASE()", connection);
                var actualDatabase = await currentDbCommand.ExecuteScalarAsync();
                _logger.LogInformation(" Actually connected to database: {ActualDatabase}", actualDatabase);

                if (!actualDatabase.ToString().Equals(databaseName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Connected to wrong database! Expected: {databaseName}, Actual: {actualDatabase}");
                }

                // Execute each CREATE TABLE statement separately
                var createStatements = GetCreateTableStatements();
                _logger.LogInformation("Executing {StatementCount} CREATE TABLE statements...", createStatements.Count);
                
                foreach (var statement in createStatements)
                {
                    try
                    {
                        var tableName = ExtractTableName(statement);
                        _logger.LogDebug("Creating table: {TableName}", tableName);
                        var command = new MySqlCommand(statement, connection);
                        await command.ExecuteNonQueryAsync();
                        _logger.LogDebug(" Created table: {TableName}", tableName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to execute CREATE TABLE statement: {Statement}", 
                            statement.Substring(0, Math.Min(100, statement.Length)));
                        throw; // Re-throw to stop the process
                    }
                }

                // Verify tables were created in the correct database
                var tables = await GetExistingTablesInDatabaseAsync(databaseName);
                _logger.LogInformation(" Successfully created {TableCount} tables in database {DatabaseName}: {Tables}", 
                    tables.Count, databaseName, string.Join(", ", tables));

                return tables.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tables in database: {DatabaseName}", databaseName);
                return false;
            }
        }

        private string ExtractTableName(string createStatement)
        {
            // Simple extraction of table name from CREATE TABLE statement
            var lines = createStatement.Split('\n');
            var createLine = lines[0].Trim();
            var parts = createLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 6 && parts[0].Equals("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                return parts[5].Trim('`');
            }
            return "unknown";
        }

        public async Task<List<string>> GetExistingTablesAsync(string tenantName)
        {
            var tenantDbName = _tenantService.GetTenantSchema(tenantName);
            return await GetExistingTablesInDatabaseAsync(tenantDbName);
        }

        private async Task<List<string>> GetExistingTablesInDatabaseAsync(string databaseName)
        {
            var tables = new List<string>();

            try
            {
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                
                // Use case-insensitive replacement (same as table creation)
                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={databaseName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("Getting tables from database: {DatabaseName}", databaseName);
                _logger.LogInformation("Connection string for verification: {ConnectionString}", connectionString.Replace("Password=", "Password=***"));

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Verify which database we're actually connected to
                var currentDbCommand = new MySqlCommand("SELECT DATABASE()", connection);
                var actualDatabase = await currentDbCommand.ExecuteScalarAsync();
                _logger.LogInformation("Actually connected to database for table check: {ActualDatabase}", actualDatabase);

                // Try SHOW TABLES first
                var showTablesCommand = new MySqlCommand("SHOW TABLES", connection);
                using var reader = await showTablesCommand.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
                reader.Close();

                _logger.LogInformation("SHOW TABLES returned {TableCount} tables: {Tables}", 
                    tables.Count, string.Join(", ", tables));

                // Also try INFORMATION_SCHEMA query as backup
                var infoSchemaCommand = new MySqlCommand(
                    "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema", 
                    connection);
                infoSchemaCommand.Parameters.AddWithValue("@schema", databaseName);
                
                using var reader2 = await infoSchemaCommand.ExecuteReaderAsync();
                var infoSchemaTables = new List<string>();
                
                while (await reader2.ReadAsync())
                {
                    infoSchemaTables.Add(reader2.GetString(0));
                }

                _logger.LogInformation("INFORMATION_SCHEMA query returned {TableCount} tables: {Tables}", 
                    infoSchemaTables.Count, string.Join(", ", infoSchemaTables));

                // Return the larger list (in case one method works better)
                return tables.Count >= infoSchemaTables.Count ? tables : infoSchemaTables;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get tables from database: {DatabaseName}", databaseName);
            }

            return tables;
        }

        public async Task<bool> DropAllTablesAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var connectionString = baseConnectionString.Replace($"Database={baseDatabaseName}", $"Database={tenantDbName}");

                _logger.LogInformation("Dropping all tables in tenant database: {TenantDatabase}", tenantDbName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Get all tables first
                var tables = await GetExistingTablesInDatabaseAsync(tenantDbName);

                // Drop all tables
                foreach (var table in tables)
                {
                    var dropCommand = new MySqlCommand($"DROP TABLE IF EXISTS `{table}`", connection);
                    await dropCommand.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Successfully dropped {TableCount} tables from tenant database: {TenantDatabase}", 
                    tables.Count, tenantDbName);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to drop tables for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        private List<string> GetCreateTableStatements()
        {
            return new List<string>
            {
                @"CREATE TABLE IF NOT EXISTS `Users` (
                    `UserName` varchar(50) NOT NULL,
                    `FullName` varchar(100) NOT NULL,
                    `UserEmail` varchar(255) DEFAULT NULL,
                    `Password` varchar(50) DEFAULT NULL,
                    `admin` tinyint(1) NOT NULL DEFAULT 0,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`UserName`)
                )",

                @"CREATE TABLE IF NOT EXISTS `TrainingPrograms` (
                    `TrainingProgramId` varchar(50) NOT NULL,
                    `ProgramName` varchar(255) NOT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`TrainingProgramId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `LearningPaths` (
                    `LearningPathId` varchar(50) NOT NULL,
                    `PathName` varchar(255) NOT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`LearningPathId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Materials` (
                    `MaterialId` varchar(50) NOT NULL,
                    `MaterialName` varchar(255) NOT NULL,
                    `MaterialType` varchar(50) DEFAULT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `Discriminator` varchar(255) NOT NULL,
                    `VideoPath` varchar(500) DEFAULT NULL,
                    `ImagePath` varchar(500) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`MaterialId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Assets` (
                    `AssetId` varchar(50) NOT NULL,
                    `AssetName` varchar(255) NOT NULL,
                    `AssetType` varchar(50) DEFAULT NULL,
                    `FilePath` varchar(500) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`AssetId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Shares` (
                    `ShareId` varchar(50) NOT NULL,
                    `ShareType` varchar(50) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`ShareId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `ChecklistEntries` (
                    `EntryId` varchar(50) NOT NULL,
                    `EntryText` varchar(500) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`EntryId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `VideoTimestamps` (
                    `TimestampId` varchar(50) NOT NULL,
                    `Description` varchar(500) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`TimestampId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `WorkflowSteps` (
                    `StepId` varchar(50) NOT NULL,
                    `StepName` varchar(255) DEFAULT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`StepId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Tenants` (
                    `TenantName` varchar(100) NOT NULL,
                    `TenantGroup` varchar(100) DEFAULT NULL,
                    `Description` varchar(500) DEFAULT NULL,
                    `TenantDirectory` varchar(500) DEFAULT NULL,
                    `OwnerName` varchar(255) DEFAULT NULL,
                    `TenantSchema` varchar(255) DEFAULT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdatedDate` datetime(6) NOT NULL,
                    PRIMARY KEY (`TenantName`)
                )"
            };
        }

        private string GetCreateTablesScript()
        {
            return @"
CREATE TABLE IF NOT EXISTS `Users` (
    `UserName` varchar(50) NOT NULL,
    `FullName` varchar(100) NOT NULL,
    `UserEmail` varchar(255) DEFAULT NULL,
    `Password` varchar(50) DEFAULT NULL,
    `admin` tinyint(1) NOT NULL DEFAULT 0,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`UserName`)
)

CREATE TABLE IF NOT EXISTS `TrainingPrograms` (
    `TrainingProgramId` varchar(50) NOT NULL,
    `ProgramName` varchar(255) NOT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`TrainingProgramId`)
)

CREATE TABLE IF NOT EXISTS `LearningPaths` (
    `LearningPathId` varchar(50) NOT NULL,
    `PathName` varchar(255) NOT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`LearningPathId`)
)

CREATE TABLE IF NOT EXISTS `Materials` (
    `MaterialId` varchar(50) NOT NULL,
    `MaterialName` varchar(255) NOT NULL,
    `MaterialType` varchar(50) DEFAULT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    `Discriminator` varchar(255) NOT NULL,
    `VideoPath` varchar(500) DEFAULT NULL,
    `ImagePath` varchar(500) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`MaterialId`)
)

CREATE TABLE IF NOT EXISTS `Assets` (
    `AssetId` varchar(50) NOT NULL,
    `AssetName` varchar(255) NOT NULL,
    `AssetType` varchar(50) DEFAULT NULL,
    `FilePath` varchar(500) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`AssetId`)
)

CREATE TABLE IF NOT EXISTS `Shares` (
    `ShareId` varchar(50) NOT NULL,
    `ShareType` varchar(50) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`ShareId`)
)

CREATE TABLE IF NOT EXISTS `ChecklistEntries` (
    `EntryId` varchar(50) NOT NULL,
    `EntryText` varchar(500) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`EntryId`)
)

CREATE TABLE IF NOT EXISTS `VideoTimestamps` (
    `TimestampId` varchar(50) NOT NULL,
    `Description` varchar(500) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`TimestampId`)
)

CREATE TABLE IF NOT EXISTS `WorkflowSteps` (
    `StepId` varchar(50) NOT NULL,
    `StepName` varchar(255) DEFAULT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`StepId`)
)

CREATE TABLE IF NOT EXISTS `Tenants` (
    `TenantName` varchar(100) NOT NULL,
    `TenantGroup` varchar(100) DEFAULT NULL,
    `Description` varchar(500) DEFAULT NULL,
    `TenantDirectory` varchar(500) DEFAULT NULL,
    `OwnerName` varchar(255) DEFAULT NULL,
    `TenantSchema` varchar(255) DEFAULT NULL,
    `CreatedDate` datetime(6) NOT NULL,
    `UpdatedDate` datetime(6) NOT NULL,
    PRIMARY KEY (`TenantName`)
)";
        }
    }
}