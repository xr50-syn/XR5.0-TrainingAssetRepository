using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services
{
    // Enhanced Tenant Service for XR50 (MySQL)
    public interface IXR50TenantService
    {
        string GetCurrentTenant();
        Task<bool> ValidateTenantAsync(string tenantName);
        Task<bool> TenantExistsAsync(string tenantName);
        Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant);
        string GetTenantSchema(string tenantName);
    }

    public class XR50TenantService : IXR50TenantService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<XR50TenantService> _logger;

        public XR50TenantService(
            IHttpContextAccessor httpContextAccessor, 
            IConfiguration configuration,
            IServiceProvider serviceProvider,
            ILogger<XR50TenantService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

       public string GetCurrentTenant()
        {
            var context = _httpContextAccessor.HttpContext;
            
            // Log the incoming request for debugging
            var path = context?.Request.Path.Value;
            _logger.LogDebug("Resolving tenant for path: {Path}", path);
            
            if (!string.IsNullOrEmpty(path))
            {
                var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                _logger.LogDebug("Path segments: [{Segments}]", string.Join(", ", pathSegments));
                
                if (pathSegments.Length >= 2)
                {
                    // Pattern: /api/{tenant}/...
                    if (pathSegments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
                    {
                        var tenant = pathSegments[1];
                        _logger.LogInformation("Resolved tenant from API pattern: {TenantName}", tenant);
                        return tenant;
                    }
                    
                    // Pattern: /xr50/{tenant}/... or /xr50/trainingAssetRepository/tenants (admin endpoint)
                    if (pathSegments[0].Equals("xr50", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pathSegments.Length >= 3 && 
                            !pathSegments[1].Equals("trainingAssetRepository", StringComparison.OrdinalIgnoreCase))
                        {
                            var tenant = pathSegments[1];
                            _logger.LogInformation(" Resolved tenant from XR50 pattern: {TenantName}", tenant);
                            return tenant; // /xr50/{tenant}/...
                        }
                        else
                        {
                            _logger.LogDebug("Admin endpoint detected: /xr50/trainingAssetRepository/...");
                        }
                    }
                    
                    // Pattern: /{tenant}/api/... (tenant-first routing)
                    if (pathSegments.Length >= 3 && 
                        pathSegments[1].Equals("api", StringComparison.OrdinalIgnoreCase))
                    {
                        var tenant = pathSegments[0];
                        _logger.LogInformation(" Resolved tenant from tenant-first pattern: {TenantName}", tenant);
                        return tenant;
                    }
                }
            }
            
            // Fallback: From header (useful for testing/admin operations)
            if (context?.Request.Headers.TryGetValue("X-Tenant-Name", out var tenantHeader) == true)
            {
                var tenant = tenantHeader.FirstOrDefault();
                _logger.LogInformation("Resolved tenant from header: {TenantName}", tenant);
                return tenant;
            }
            
            // From JWT claims
            var tenantClaim = context?.User?.FindFirst("tenantName")?.Value;
            if (!string.IsNullOrEmpty(tenantClaim))
            {
                _logger.LogInformation("Resolved tenant from JWT claim: {TenantName}", tenantClaim);
                return tenantClaim;
            }
            
            _logger.LogInformation("No tenant resolved, using default");
            return "default";
        }
        public async Task<bool> ValidateTenantAsync(string tenantName)
        {
            return await TenantExistsAsync(tenantName);
        }

        public async Task<bool> TenantExistsAsync(string tenantName)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if tenant database exists
                var databaseName = GetTenantSchema(tenantName);
                var sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @databaseName";
                
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@databaseName", databaseName);
                
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if tenant {TenantName} exists", tenantName);
                return false;
            }
        }

        public async Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant)
        {
            try
            {
                // Create the database and run migrations
                using var scope = _serviceProvider.CreateScope();
                var migrationService = scope.ServiceProvider.GetRequiredService<XR50MigrationService>();

                await migrationService.CreateTenantDatabaseAsync(tenant);

                _logger.LogInformation("Successfully created tenant database: {TenantName}", tenant.TenantName);
                return tenant;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tenant {TenantName}", tenant.TenantName);
                throw;
            }
            
        }

        public string GetTenantSchema(string tenantName) => XR50TenantDatabase.SchemaFor(tenantName);
    }
}