using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Migrations;
using XR50TrainingAssetRepo.Infrastructure;
using XR50TrainingAssetRepo.Infrastructure.ErrorHandling;
using Microsoft.AspNetCore.Authorization;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/troubleshooting")]
    [Authorize(Policy = "SystemAdmin")]
    [ApiController]
    public class TenantTroubleshootingController : ControllerBase
    {
        private readonly IXR50TenantTroubleshootingService _troubleshootingService;
        private readonly XR50MigrationService _migrationService;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IXR50SchemaMigrator _schemaMigrator;
        private readonly ILogger<TenantTroubleshootingController> _logger;

        public TenantTroubleshootingController(
            IXR50TenantTroubleshootingService troubleshootingService,
            XR50MigrationService migrationService,
            IXR50TenantManagementService tenantManagementService,
            IXR50SchemaMigrator schemaMigrator,
            ILogger<TenantTroubleshootingController> logger)
        {
            _troubleshootingService = troubleshootingService;
            _migrationService = migrationService;
            _tenantManagementService = tenantManagementService;
            _schemaMigrator = schemaMigrator;
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
                return this.ProblemServerError($"Error diagnosing tenant '{tenantName}'.");
            }
        }

        /// Repair a tenant's database: create it if missing and bring it to the committed schema
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

                return this.ProblemBadRequest($"Failed to repair tenant '{tenantName}'.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error repairing tenant {TenantName}", tenantName);
                return this.ProblemServerError($"Error repairing tenant '{tenantName}'.");
            }
        }

        // ----- Schema migrations -----

        /// Migration state of the base database and every registered tenant. Read-only.
        [HttpGet("migration-status")]
        public async Task<ActionResult<IReadOnlyList<MigrationTargetStatus>>> GetMigrationStatus()
        {
            try
            {
                return Ok(await _schemaMigrator.GetStatusAsync(null, HttpContext.RequestAborted));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading migration status");
                return this.ProblemServerError("Error reading migration status.");
            }
        }

        /// Migration state of one registered tenant's database. Read-only.
        [HttpGet("migration-status/{tenantName}")]
        public async Task<ActionResult<IReadOnlyList<MigrationTargetStatus>>> GetMigrationStatus(string tenantName)
        {
            try
            {
                if (!await TenantIsRegisteredAsync(tenantName))
                {
                    return this.ProblemNotFound($"Tenant '{tenantName}' is not registered.");
                }

                return Ok(await _schemaMigrator.GetStatusAsync(tenantName, HttpContext.RequestAborted));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading migration status of tenant {TenantName}", tenantName);
                return this.ProblemServerError($"Error reading migration status of tenant '{tenantName}'.");
            }
        }

        /// Apply pending migrations to one registered tenant's database (adopting a legacy
        /// database if needed). Idempotent: a current database reports nothing applied.
        [HttpPost("migrate/{tenantName}")]
        public async Task<ActionResult<MigrationRunResult>> MigrateTenant(string tenantName)
        {
            try
            {
                if (!await TenantIsRegisteredAsync(tenantName))
                {
                    return this.ProblemNotFound($"Tenant '{tenantName}' is not registered.");
                }

                var result = await _schemaMigrator.MigrateTenantAsync(tenantName, null, HttpContext.RequestAborted);
                if (result.Succeeded)
                {
                    return Ok(result);
                }

                if (result.StateBefore == SchemaState.Missing)
                {
                    return this.ProblemNotFound(result.Error ?? $"Database of tenant '{tenantName}' does not exist.");
                }

                if (result.ManualInterventionRequired)
                {
                    return this.ProblemConflict(result.Error ?? $"Tenant '{tenantName}' needs manual intervention before it can be migrated.");
                }

                return this.ProblemServerError(result.Error ?? $"Migration of tenant '{tenantName}' failed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating tenant {TenantName}", tenantName);
                return this.ProblemServerError($"Error migrating tenant '{tenantName}'.");
            }
        }

        /// Apply pending migrations to the base database and every registered tenant. One
        /// tenant's failure does not stop the others; the report says which succeeded.
        [HttpPost("migrate-all")]
        public async Task<ActionResult<MigrationRunReport>> MigrateAll()
        {
            try
            {
                var report = await _schemaMigrator.MigrateAllAsync(
                    new MigrateOptions { TolerateTenantFailures = true }, HttpContext.RequestAborted);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating all databases");
                return this.ProblemServerError("Error migrating all databases.");
            }
        }

        // GetTenantAsync reports an unknown tenant by throwing; the tenant controller maps that
        // to 404 and so do the migration endpoints.
        private async Task<bool> TenantIsRegisteredAsync(string tenantName)
        {
            try
            {
                return await _tenantManagementService.GetTenantAsync(tenantName) is not null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // ----- Diagnostics -----

        /// Test tenant database connection
        [HttpGet("test-connection/{tenantName}")]
        public async Task<ActionResult> TestConnection(string tenantName)
        {
            try
            {
                var canConnect = await _troubleshootingService.TestTenantConnectionAsync(tenantName);

                return Ok(new
                {
                    TenantName = tenantName,
                    CanConnect = canConnect,
                    Message = canConnect ? "Connection successful" : "Connection failed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection for tenant {TenantName}", tenantName);
                return this.ProblemServerError($"Error testing connection for tenant '{tenantName}'.");
            }
        }

        /// Get all tenant databases
        [HttpGet("databases")]
        [DevelopmentOnly]
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
                return this.ProblemServerError("Error getting tenant databases.");
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
                var diagnostic = await _troubleshootingService.DiagnoseTenantAsync(tenantName);

                return Ok(new
                {
                    Message = $"Test tenant {tenantName} created",
                    Tenant = createdTenant,
                    Diagnostic = diagnostic
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test tenant {TenantName}", tenantName);
                return this.ProblemServerError($"Error creating test tenant '{tenantName}'.");
            }
        }

        /// Force recreate a tenant database
        [HttpPost("force-recreate/{tenantName}")]
        [DevelopmentOnly]
        public async Task<ActionResult> ForceRecreateTenant(string tenantName)
        {
            try
            {
                _logger.LogInformation("Force recreating tenant {TenantName}", tenantName);

                var tenant = new XR50Tenant
                {
                    TenantName = tenantName,
                    TenantGroup = "recreated",
                    Description = $"Force recreated tenant - {DateTime.UtcNow}",
                    OwnerName = "System"
                };

                await _migrationService.CreateTenantDatabaseAsync(tenant);
                var diagnostic = await _troubleshootingService.DiagnoseTenantAsync(tenantName);

                return Ok(new
                {
                    Message = $"Tenant {tenantName} force recreated",
                    Diagnostic = diagnostic
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error force recreating tenant {TenantName}", tenantName);
                return this.ProblemServerError($"Error force recreating tenant '{tenantName}'.");
            }
        }

        /// Completely rebuild tenant database (drop the database and recreate it from the migrations)
        [HttpPost("rebuild/{tenantName}")]
        [DevelopmentOnly]
        public async Task<ActionResult> RebuildTenantDatabase(string tenantName)
        {
            try
            {
                var success = await _migrationService.RepairTenantDatabaseAsync(tenantName);

                if (success)
                {
                    var tables = await _troubleshootingService.GetTablesInTenantDatabaseAsync(tenantName);
                    return Ok(new
                    {
                        Message = $"Tenant database {tenantName} rebuilt successfully",
                        TablesCreated = tables,
                        TableCount = tables.Count
                    });
                }

                return this.ProblemBadRequest($"Failed to rebuild tenant database '{tenantName}'.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rebuilding tenant database {TenantName}", tenantName);
                return this.ProblemServerError($"Error rebuilding database for tenant '{tenantName}'.");
            }
        }

        /// Get existing tables in tenant database
        [HttpGet("tables/{tenantName}")]
        public async Task<ActionResult<List<string>>> GetTenantTables(string tenantName)
        {
            try
            {
                var tables = await _troubleshootingService.GetTablesInTenantDatabaseAsync(tenantName);
                return Ok(new
                {
                    TenantName = tenantName,
                    Tables = tables,
                    TableCount = tables.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tables for tenant {TenantName}", tenantName);
                return this.ProblemServerError($"Error getting tables for tenant '{tenantName}'.");
            }
        }

        /// Completely delete tenant database (WARNING: This will delete all data!)
        [HttpDelete("delete-database/{tenantName}")]
        [DevelopmentOnly]
        public async Task<ActionResult> DeleteTenantDatabase(string tenantName)
        {
            try
            {
                _logger.LogWarning("Request to delete tenant database: {TenantName}", tenantName);

                var success = await _migrationService.DeleteTenantDatabaseAsync(tenantName);

                if (success)
                {
                    return Ok(new
                    {
                        Message = $"Tenant database {tenantName} deleted successfully",
                        Warning = "All data has been permanently deleted"
                    });
                }

                return this.ProblemBadRequest($"Failed to delete tenant database '{tenantName}'.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting tenant database {TenantName}", tenantName);
                return this.ProblemServerError($"Error deleting database for tenant '{tenantName}'.");
            }
        }

        /// Completely delete tenant (database AND registry entry) - WARNING: PERMANENT!
        [HttpDelete("delete-completely/{tenantName}")]
        [DevelopmentOnly]
        public async Task<ActionResult> DeleteTenantCompletely(string tenantName)
        {
            try
            {
                _logger.LogWarning("Request to completely delete tenant: {TenantName}", tenantName);

                await _tenantManagementService.DeleteTenantCompletelyAsync(tenantName);

                return Ok(new
                {
                    Message = $"Tenant {tenantName} completely deleted",
                    Warning = "Database and registry entry permanently deleted"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completely deleting tenant {TenantName}", tenantName);
                return this.ProblemServerError($"Error completely deleting tenant '{tenantName}'.");
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
                return this.ProblemServerError("Error performing health check.");
            }
        }
    }
}
