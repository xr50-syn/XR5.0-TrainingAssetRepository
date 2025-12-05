using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/troubleshooting")]
    [ApiController]
    public class TenantTroubleshootingController : ControllerBase
    {
        private readonly IXR50TenantTroubleshootingService _troubleshootingService;
        private readonly XR50MigrationService _migrationService;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IXR50ManualTableCreator _tableCreator;
        private readonly ILogger<TenantTroubleshootingController> _logger;

        public TenantTroubleshootingController(
            IXR50TenantTroubleshootingService troubleshootingService,
            XR50MigrationService migrationService,
            IXR50TenantManagementService tenantManagementService,
            IXR50ManualTableCreator tableCreator,
            ILogger<TenantTroubleshootingController> logger)
        {
            _troubleshootingService = troubleshootingService;
            _migrationService = migrationService;
            _tenantManagementService = tenantManagementService;
            _tableCreator = tableCreator;
            _logger = logger;
        }

       
        /// Diagnose a specific tenant's database health
        
        [HttpGet("diagnose/{tenantName}")]
        public async Task<ActionResult<TenantDiagnosticResult>> DiagnoseTenant(string tenantName)
        {
            try
            {
                var result = await _troubleshootingService.DiagnoseTenantAsync(tenantName);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error diagnosing tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error diagnosing tenant: {ex.Message}");
            }
        }

       
        /// Repair a tenant's database
        
        [HttpPost("repair/{tenantName}")]
        public async Task<ActionResult> RepairTenant(string tenantName)
        {
            try
            {
                var success = await _troubleshootingService.RepairTenantDatabaseAsync(tenantName);
                
                if (success)
                {
                    return Ok(new { Message = $"Tenant {tenantName} repaired successfully" });
                }
                else
                {
                    return BadRequest(new { Message = $"Failed to repair tenant {tenantName}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error repairing tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error repairing tenant: {ex.Message}");
            }
        }

       
        /// Test tenant database connection
        
        [HttpGet("test-connection/{tenantName}")]
        public async Task<ActionResult> TestConnection(string tenantName)
        {
            try
            {
                var canConnect = await _troubleshootingService.TestTenantConnectionAsync(tenantName);
                
                return Ok(new { 
                    TenantName = tenantName, 
                    CanConnect = canConnect,
                    Message = canConnect ? "Connection successful" : "Connection failed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection for tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error testing connection: {ex.Message}");
            }
        }

       
        /// Get all tenant databases
        
        [HttpGet("databases")]
        public async Task<ActionResult<List<string>>> GetAllTenantDatabases()
        {
            try
            {
                var databases = await _troubleshootingService.GetAllTenantDatabasesAsync();
                return Ok(databases);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tenant databases");
                return StatusCode(500, $"Error getting databases: {ex.Message}");
            }
        }

       
        /// Create a test tenant for debugging
        
        [HttpPost("create-test-tenant/{tenantName}")]
        public async Task<ActionResult> CreateTestTenant(string tenantName)
        {
            try
            {
                var testTenant = new XR50Tenant
                {
                    TenantName = tenantName,
                    TenantGroup = "test",
                    Description = $"Test tenant created for troubleshooting - {DateTime.UtcNow}",
                    OwnerName = "System",
                    TenantDirectory = $"/test/{tenantName}"
                };

                var createdTenant = await _tenantManagementService.CreateTenantAsync(testTenant);
                
                // Immediately diagnose the created tenant
                var diagnostic = await _troubleshootingService.DiagnoseTenantAsync(tenantName);
                
                return Ok(new { 
                    Message = $"Test tenant {tenantName} created",
                    Tenant = createdTenant,
                    Diagnostic = diagnostic
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error creating test tenant: {ex.Message}");
            }
        }

       
        /// Force recreate a tenant database
        
        [HttpPost("force-recreate/{tenantName}")]
        public async Task<ActionResult> ForceRecreateTenant(string tenantName)
        {
            try
            {
                _logger.LogInformation("Force recreating tenant {TenantName}", tenantName);

                // Create tenant object
                var tenant = new XR50Tenant
                {
                    TenantName = tenantName,
                    TenantGroup = "recreated",
                    Description = $"Force recreated tenant - {DateTime.UtcNow}",
                    OwnerName = "System"
                };

                // Use migration service directly
                await _migrationService.CreateTenantDatabaseAsync(tenant);
                
                // Test the result
                var diagnostic = await _troubleshootingService.DiagnoseTenantAsync(tenantName);
                
                return Ok(new { 
                    Message = $"Tenant {tenantName} force recreated",
                    Diagnostic = diagnostic
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error force recreating tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error force recreating tenant: {ex.Message}");
            }
        }

       
        /// Manually create tables in tenant database
        
        [HttpPost("create-tables/{tenantName}")]
        public async Task<ActionResult> CreateTablesManually(string tenantName)
        {
            try
            {
                var success = await _tableCreator.CreateAllTablesAsync(tenantName);
                
                if (success)
                {
                    var tables = await _tableCreator.GetExistingTablesAsync(tenantName);
                    var diagnostic = await _troubleshootingService.DiagnoseTenantAsync(tenantName);
                    
                    return Ok(new { 
                        Message = $"Tables created manually for tenant {tenantName}",
                        TablesCreated = tables,
                        TableCount = tables.Count,
                        Diagnostic = diagnostic
                    });
                }
                else
                {
                    return BadRequest(new { Message = $"Failed to create tables for tenant {tenantName}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error manually creating tables for tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error creating tables: {ex.Message}");
            }
        }

       
        /// Completely rebuild tenant database (drop and recreate all tables)
        
        [HttpPost("rebuild/{tenantName}")]
        public async Task<ActionResult> RebuildTenantDatabase(string tenantName)
        {
            try
            {
                var success = await _migrationService.RepairTenantDatabaseAsync(tenantName);
                
                if (success)
                {
                    var tables = await _tableCreator.GetExistingTablesAsync(tenantName);
                    return Ok(new { 
                        Message = $"Tenant database {tenantName} rebuilt successfully",
                        TablesCreated = tables,
                        TableCount = tables.Count
                    });
                }
                else
                {
                    return BadRequest(new { Message = $"Failed to rebuild tenant database {tenantName}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rebuilding tenant database {TenantName}", tenantName);
                return StatusCode(500, $"Error rebuilding database: {ex.Message}");
            }
        }

       
        /// Get existing tables in tenant database
        
        [HttpGet("tables/{tenantName}")]
        public async Task<ActionResult<List<string>>> GetTenantTables(string tenantName)
        {
            try
            {
                var tables = await _tableCreator.GetExistingTablesAsync(tenantName);
                return Ok(new {
                    TenantName = tenantName,
                    Tables = tables,
                    TableCount = tables.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tables for tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error getting tables: {ex.Message}");
            }
        }

       
        /// Completely delete tenant database (WARNING: This will delete all data!)
        
        [HttpDelete("delete-database/{tenantName}")]
        public async Task<ActionResult> DeleteTenantDatabase(string tenantName)
        {
            try
            {
                _logger.LogWarning("Request to delete tenant database: {TenantName}", tenantName);
                
                var success = await _migrationService.DeleteTenantDatabaseAsync(tenantName);
                
                if (success)
                {
                    return Ok(new { 
                        Message = $"Tenant database {tenantName} deleted successfully",
                        Warning = "All data has been permanently deleted"
                    });
                }
                else
                {
                    return BadRequest(new { Message = $"Failed to delete tenant database {tenantName}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting tenant database {TenantName}", tenantName);
                return StatusCode(500, $"Error deleting database: {ex.Message}");
            }
        }

       
        /// Completely delete tenant (database AND registry entry) - WARNING: PERMANENT!
        
        [HttpDelete("delete-completely/{tenantName}")]
        public async Task<ActionResult> DeleteTenantCompletely(string tenantName)
        {
            try
            {
                _logger.LogWarning("Request to completely delete tenant: {TenantName}", tenantName);
                
                await _tenantManagementService.DeleteTenantCompletelyAsync(tenantName);
                
                return Ok(new { 
                    Message = $"Tenant {tenantName} completely deleted",
                    Warning = "Database and registry entry permanently deleted"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completely deleting tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error deleting tenant: {ex.Message}");
            }
        }


        /// Get all tenants with their health status

        [HttpGet("health-check")]
        public async Task<ActionResult> GetTenantsHealthCheck()
        {
            try
            {
                var allTenants = await _tenantManagementService.GetAllTenantsAsync();
                var healthResults = new List<object>();

                foreach (var tenant in allTenants)
                {
                    var diagnostic = await _troubleshootingService.DiagnoseTenantAsync(tenant.TenantName);
                    healthResults.Add(new
                    {
                        TenantName = tenant.TenantName,
                        IsHealthy = diagnostic.IsHealthy,
                        DatabaseExists = diagnostic.DatabaseExists,
                        CanConnect = diagnostic.CanConnect,
                        TableCount = diagnostic.Tables.Count,
                        Error = diagnostic.Error
                    });
                }

                return Ok(healthResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing health check");
                return StatusCode(500, $"Error performing health check: {ex.Message}");
            }
        }


        /// Migrate Annotations columns for video and image materials

        [HttpPost("migrate-annotations/{tenantName}")]
        public async Task<ActionResult> MigrateAnnotationsColumns(string tenantName)
        {
            try
            {
                _logger.LogInformation("Running Annotations column migration for tenant: {TenantName}", tenantName);

                var success = await _tableCreator.MigrateAnnotationsColumnsAsync(tenantName);

                if (success)
                {
                    return Ok(new {
                        Message = $"Annotations columns migrated successfully for tenant {tenantName}",
                        Details = "Added 'startTime' and 'Annotations' columns to Materials table"
                    });
                }
                else
                {
                    return BadRequest(new { Message = $"Failed to migrate Annotations columns for tenant {tenantName}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating Annotations columns for tenant {TenantName}", tenantName);
                return StatusCode(500, $"Error migrating columns: {ex.Message}");
            }
        }
    }
}