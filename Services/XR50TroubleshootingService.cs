using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Services
{
    public interface IXR50TenantTroubleshootingService
    {
        Task<TenantDiagnosticResult> DiagnoseTenantAsync(string tenantName);
        Task<bool> RepairTenantDatabaseAsync(string tenantName);
        Task<List<string>> GetAllTenantDatabasesAsync();
        Task<bool> TestTenantConnectionAsync(string tenantName);
    }

    public class XR50TenantTroubleshootingService : IXR50TenantTroubleshootingService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<XR50TenantTroubleshootingService> _logger;
        private readonly IXR50TenantService _tenantService;

        public XR50TenantTroubleshootingService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<XR50TenantTroubleshootingService> logger,
            IXR50TenantService tenantService)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
            _tenantService = tenantService;
        }

        public async Task<TenantDiagnosticResult> DiagnoseTenantAsync(string tenantName)
        {
            var result = new TenantDiagnosticResult
            {
                TenantName = tenantName,
                DiagnosticTime = DateTime.UtcNow
            };

            try
            {
                // 1. Check if tenant exists in registry
                result.ExistsInRegistry = await CheckTenantRegistryAsync(tenantName);

                // 2. Check if database exists
                result.DatabaseExists = await CheckDatabaseExistsAsync(tenantName);

                // 3. Check connection
                result.CanConnect = await TestTenantConnectionAsync(tenantName);

                // 4. Check tables if connection works
                if (result.CanConnect)
                {
                    result.Tables = await GetTablesInTenantDatabaseAsync(tenantName);
                    result.HasRequiredTables = result.Tables.Count > 0;
                }

                // 5. Check migrations
                if (result.CanConnect)
                {
                    result.MigrationStatus = await CheckMigrationStatusAsync(tenantName);
                }

                result.IsHealthy = result.ExistsInRegistry && result.DatabaseExists && 
                                 result.CanConnect && result.HasRequiredTables;

                _logger.LogInformation("Diagnostic completed for tenant {TenantName}: Healthy={IsHealthy}", 
                    tenantName, result.IsHealthy);

            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _logger.LogError(ex, "Error during tenant diagnostic for {TenantName}", tenantName);
            }

            return result;
        }

        public async Task<bool> RepairTenantDatabaseAsync(string tenantName)
        {
            try
            {
                _logger.LogInformation("Starting repair for tenant {TenantName}", tenantName);

                // 1. Get or create tenant database name
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

                // 2. Ensure database exists
                var adminConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, "mysql");
                using (var connection = new MySqlConnection(adminConnectionString))
                {
                    await connection.OpenAsync();
                    var createDbCommand = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{tenantDbName}`", connection);
                    await createDbCommand.ExecuteNonQueryAsync();
                }

                // 3. Apply migrations
                var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, tenantDbName);
                var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
                optionsBuilder.UseMySql(tenantConnectionString, ServerVersion.AutoDetect(tenantConnectionString));

                var mockTenantService = new DirectTenantService(tenantName);
                using var context = new XR50TrainingContext(optionsBuilder.Options, mockTenantService, _configuration);

                // Try migrations first
                try
                {
                    await context.Database.MigrateAsync();
                    _logger.LogInformation("Successfully applied migrations for tenant {TenantName}", tenantName);
                }
                catch (Exception migrationEx)
                {
                    _logger.LogWarning(migrationEx, "Migration failed for tenant {TenantName}, trying EnsureCreated", tenantName);
                    
                    // Fallback to EnsureCreated
                    await context.Database.EnsureCreatedAsync();
                    _logger.LogInformation("Successfully created database structure for tenant {TenantName}", tenantName);
                }

                // 4. Verify repair
                var diagnostic = await DiagnoseTenantAsync(tenantName);
                return diagnostic.IsHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair tenant database for {TenantName}", tenantName);
                return false;
            }
        }

        public async Task<List<string>> GetAllTenantDatabasesAsync()
        {
            var databases = new List<string>();

            try
            {
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var adminConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, "mysql");

                using var connection = new MySqlConnection(adminConnectionString);
                await connection.OpenAsync();

                var command = new MySqlCommand("SHOW DATABASES LIKE 'xr50_tenant_%'", connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    databases.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get tenant databases");
            }

            return databases;
        }

        public async Task<bool> TestTenantConnectionAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, tenantDbName);

                using var connection = new MySqlConnection(tenantConnectionString);
                await connection.OpenAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Connection test failed for tenant {TenantName}", tenantName);
                return false;
            }
        }

        private async Task<bool> CheckTenantRegistryAsync(string tenantName)
        {
            try
            {
                var tenantManagement = _serviceProvider.GetRequiredService<IXR50TenantManagementService>();
                var tenant = await tenantManagement.GetTenantAsync(tenantName);
                return tenant != null;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckDatabaseExistsAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var adminConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, "mysql");

                using var connection = new MySqlConnection(adminConnectionString);
                await connection.OpenAsync();

                var command = new MySqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @dbName", connection);
                command.Parameters.AddWithValue("@dbName", tenantDbName);

                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task<List<string>> GetTablesInTenantDatabaseAsync(string tenantName)
        {
            var tables = new List<string>();

            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, tenantDbName);

                using var connection = new MySqlConnection(tenantConnectionString);
                await connection.OpenAsync();

                var command = new MySqlCommand("SHOW TABLES", connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get tables for tenant {TenantName}", tenantName);
            }

            return tables;
        }

        private async Task<string> CheckMigrationStatusAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, tenantDbName);

                var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
                optionsBuilder.UseMySql(tenantConnectionString, ServerVersion.AutoDetect(tenantConnectionString));

                var mockTenantService = new DirectTenantService(tenantName);
                using var context = new XR50TrainingContext(optionsBuilder.Options, mockTenantService, _configuration);

                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

                return $"Applied: {appliedMigrations.Count()}, Pending: {pendingMigrations.Count()}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

    }

    // Diagnostic result model
    public class TenantDiagnosticResult
    {
        public string TenantName { get; set; } = "";
        public DateTime DiagnosticTime { get; set; }
        public bool ExistsInRegistry { get; set; }
        public bool DatabaseExists { get; set; }
        public bool CanConnect { get; set; }
        public bool HasRequiredTables { get; set; }
        public List<string> Tables { get; set; } = new();
        public string MigrationStatus { get; set; } = "";
        public bool IsHealthy { get; set; }
        public string Error { get; set; } = "";
    }
}