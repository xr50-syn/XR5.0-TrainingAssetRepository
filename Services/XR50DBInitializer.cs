using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Services
{
    public interface IXR50DatabaseInitializer
    {
        Task InitializeAsync();
        Task InitializeTenantDatabaseAsync(string tenantName);
        Task<bool> VerifyTenantDatabaseAsync(string tenantName);
    }

    public class XR50DatabaseInitializer : IXR50DatabaseInitializer
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<XR50DatabaseInitializer> _logger;

        public XR50DatabaseInitializer(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<XR50DatabaseInitializer> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Initialize the main database
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<XR50TrainingContext>();
                
                // Ensure the main database exists and is up to date
                await context.Database.MigrateAsync();
                
                _logger.LogInformation("Main database initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize main database");
                throw;
            }
        }

        public async Task InitializeTenantDatabaseAsync(string tenantName)
        {
            try
            {
                // Get tenant database name
                var tenantService = _serviceProvider.GetRequiredService<IXR50TenantService>();
                var tenantDbName = tenantService.GetTenantSchema(tenantName);
                
                // Build connection string for tenant database
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, tenantDbName);

                // Create DbContext options for tenant database
                var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
                optionsBuilder.UseMySql(tenantConnectionString, ServerVersion.AutoDetect(tenantConnectionString));

                // Create mock tenant service for this specific tenant
                var mockTenantService = new DirectTenantService(tenantName);

                // Initialize tenant database
                using var context = new XR50TrainingContext(optionsBuilder.Options, mockTenantService, _configuration);
                
                // Run migrations
                await context.Database.MigrateAsync();
                
                _logger.LogInformation("Tenant database {TenantDatabase} initialized successfully", tenantDbName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize tenant database for {TenantName}", tenantName);
                throw;
            }
        }

        public async Task<bool> VerifyTenantDatabaseAsync(string tenantName)
        {
            try
            {
                var tenantService = _serviceProvider.GetRequiredService<IXR50TenantService>();
                var tenantDbName = tenantService.GetTenantSchema(tenantName);
                
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, tenantDbName);

                var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
                optionsBuilder.UseMySql(tenantConnectionString, ServerVersion.AutoDetect(tenantConnectionString));

                var mockTenantService = new DirectTenantService(tenantName);

                using var context = new XR50TrainingContext(optionsBuilder.Options, mockTenantService, _configuration);
                
                // Check if database can be connected to
                var canConnect = await context.Database.CanConnectAsync();
                if (!canConnect)
                {
                    _logger.LogWarning("Cannot connect to tenant database {TenantDatabase}", tenantDbName);
                    return false;
                }

                // Check if essential tables exist
                var tableNames = new[]
                {
                    "Users", "TrainingPrograms", "LearningPaths", "Materials", "Assets"
                };

                foreach (var tableName in tableNames)
                {
                    var sql = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{tenantDbName}' AND TABLE_NAME = '{tableName}'";
                    var tableExists = await context.Database.ExecuteSqlRawAsync(sql) > 0;
                    
                    if (!tableExists)
                    {
                        _logger.LogWarning("Table {TableName} does not exist in tenant database {TenantDatabase}", tableName, tenantDbName);
                        return false;
                    }
                }

                _logger.LogInformation("Tenant database {TenantDatabase} verification successful", tenantDbName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify tenant database for {TenantName}", tenantName);
                return false;
            }
        }

        // Helper class for direct tenant service
        private class DirectTenantService : IXR50TenantService
        {
            private readonly string _tenantName;

            public DirectTenantService(string tenantName)
            {
                _tenantName = tenantName;
            }

            public string GetCurrentTenant() => _tenantName;
            public Task<bool> ValidateTenantAsync(string tenantId) => Task.FromResult(true);
            public Task<bool> TenantExistsAsync(string tenantName) => Task.FromResult(true);
            public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
            public string GetTenantSchema(string tenantName)
            {
                var sanitized = System.Text.RegularExpressions.Regex.Replace(tenantName, @"[^a-zA-Z0-9_]", "_");
                return $"xr50_tenant_{sanitized}";
            }
        }
    }
}