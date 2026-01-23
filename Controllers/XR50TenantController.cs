using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Controllers
{
    [ApiExplorerSettings(GroupName = "tenants")]
    [Route("xr50/trainingAssetRepository/[controller]")]
    [ApiController]
    public class TenantsController : ControllerBase
    {
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IStorageService _storageService;
        private readonly ILogger<TenantsController> _logger;

        public TenantsController(
            IXR50TenantManagementService tenantManagementService,
            IStorageService storageService,
            ILogger<TenantsController> logger)
        {
            _tenantManagementService = tenantManagementService;
            _storageService = storageService;
            _logger = logger;
        }

       
        /// Get all tenants
        
        [HttpGet]
        public async Task<ActionResult<TenantResponse[]>> GetTenants()
        {
            try
            {
                var tenants = await _tenantManagementService.GetAllTenantsAsync();
                var response = tenants.Select(TenantResponse.FromTenant).ToArray();
                
                _logger.LogInformation("Retrieved {Count} tenants", response.Length);
                
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tenants");
                return StatusCode(500, new { Error = "Failed to retrieve tenants", Details = ex.Message });
            }
        }

       
        /// Create a new tenant with pre-provisioned infrastructure
        
        [HttpPost]
        public async Task<ActionResult<TenantResponse>> CreateTenant([FromBody] CreateTenantRequest request)
        {
            try
            {
                _logger.LogInformation("Creating tenant: {TenantName} with {StorageType} storage", 
                    request.TenantName, request.StorageType);

                // Validate the request
                request.Validate();

                // Validate storage type matches running implementation
                var runningStorageType = _storageService.GetStorageType();
                if (!request.StorageType.Equals(runningStorageType, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Storage type mismatch: requested {RequestedType}, but server is configured for {RunningType}",
                        request.StorageType, runningStorageType);
                    return BadRequest(new
                    {
                        Error = "Storage type mismatch",
                        Message = $"This server is configured for {runningStorageType} storage. Cannot create tenant with {request.StorageType} storage.",
                        ConfiguredStorageType = runningStorageType
                    });
                }

                // Create tenant object from request
                var tenant = new XR50Tenant
                {
                    TenantName = request.TenantName,
                    TenantGroup = request.TenantGroup,
                    Description = request.Description,
                    OwnerName = request.OwnerName,
                    StorageType = request.StorageType,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Configure storage-specific properties
                if (request.StorageType.Equals("S3", StringComparison.OrdinalIgnoreCase))
                {
                    tenant.S3BucketName = request.S3Config!.BucketName;
                    tenant.S3BucketRegion = request.S3Config.BucketRegion;
                    tenant.S3BucketArn = request.S3Config.BucketArn;
                    tenant.StorageEndpoint = request.S3Config.Endpoint;
                    
                    _logger.LogInformation("S3 Configuration - Bucket: {BucketName}, Region: {Region}", 
                        tenant.S3BucketName, tenant.S3BucketRegion);
                }
                else if (request.StorageType.Equals("OwnCloud", StringComparison.OrdinalIgnoreCase))
                {
                    tenant.TenantDirectory = request.OwnCloudConfig!.TenantDirectory;
                    tenant.StorageEndpoint = request.OwnCloudConfig.Endpoint;
                    
                    _logger.LogInformation("OwnCloud Configuration - Directory: {Directory}", 
                        tenant.TenantDirectory);
                }

                // Create owner user if provided
                if (request.Owner != null)
                {
                    tenant.Owner = new User
                    {
                        UserName = request.Owner.UserName,
                        FullName = request.Owner.FullName,
                        UserEmail = request.Owner.UserEmail,
                        Password = request.Owner.Password,
                        admin = request.Owner.Admin
                    };
                }

                // Validate storage configuration before creating tenant
                if (tenant.IsS3Storage())
                {
                    tenant.ValidateS3Configuration();
                    
                    // Validate that the S3 bucket exists and is accessible
                    _logger.LogInformation("Validating pre-provisioned S3 bucket: {BucketName}", tenant.S3BucketName);
                    
                    var storageValidated = await _storageService.CreateTenantStorageAsync(request.TenantName, tenant);
                    if (!storageValidated)
                    {
                        return BadRequest(new 
                        { 
                            Error = "Storage validation failed", 
                            Message = $"Pre-provisioned S3 bucket '{tenant.S3BucketName}' is not accessible",
                            Instructions = new[]
                            {
                                "1. Verify the bucket exists in the specified region",
                                "2. Check IAM permissions for the application",
                                "3. Ensure bucket name matches infrastructure configuration"
                            }
                        });
                    }
                }
                else
                {
                    tenant.ValidateOwnCloudConfiguration();
                }

                // Create the tenant
                var createdTenant = await _tenantManagementService.CreateTenantAsync(tenant);
                
                var response = TenantResponse.FromTenant(createdTenant);
                
                _logger.LogInformation("Successfully created tenant: {TenantName} with {StorageType} storage", 
                    request.TenantName, request.StorageType);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for tenant creation: {Error}", ex.Message);
                return BadRequest(new { Error = "Invalid request", Details = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError("Tenant creation failed: {Error}", ex.Message);
                return BadRequest(new { Error = "Tenant creation failed", Details = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating tenant: {TenantName}", request.TenantName);
                return StatusCode(500, new { Error = "Tenant creation failed", Details = ex.Message });
            }
        }

       
        /// Get a specific tenant by name
        
        [HttpGet("{tenantName}")]
        public async Task<ActionResult<TenantResponse>> GetTenant(string tenantName)
        {
            try
            {
                _logger.LogInformation("Getting tenant: {TenantName}", tenantName);
                
                var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                if (tenant == null)
                {
                    _logger.LogWarning("Tenant not found: {TenantName}", tenantName);
                    return NotFound(new { Error = $"Tenant '{tenantName}' not found" });
                }
                
                var response = TenantResponse.FromTenant(tenant);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tenant: {TenantName}", tenantName);
                return StatusCode(500, new { Error = "Failed to retrieve tenant", Details = ex.Message });
            }
        }

       
        /// Delete a tenant (soft delete - marks as inactive)
        
        [HttpDelete("{tenantName}")]
        public async Task<ActionResult> DeleteTenant(string tenantName)
        {
            try
            {
                _logger.LogWarning("Deleting tenant: {TenantName}", tenantName);
                
                await _tenantManagementService.DeleteTenantAsync(tenantName);
                
                _logger.LogInformation("Successfully deleted tenant: {TenantName}", tenantName);
                
                return Ok(new 
                { 
                    Message = $"Tenant '{tenantName}' deleted successfully",
                    Note = "Storage cleanup should be handled by infrastructure team"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting tenant: {TenantName}", tenantName);
                return StatusCode(500, new { Error = "Failed to delete tenant", Details = ex.Message });
            }
        }

       
        /// Validate tenant storage configuration
        
        [HttpGet("{tenantName}/validate-storage")]
        public async Task<ActionResult<object>> ValidateTenantStorage(string tenantName)
        {
            try
            {
                _logger.LogInformation("Validating storage for tenant: {TenantName}", tenantName);
                
                var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                if (tenant == null)
                {
                    return NotFound(new { Error = $"Tenant '{tenantName}' not found" });
                }

                var validationResult = await _storageService.TenantStorageExistsAsync(tenantName);
                
                // FIX: Create configuration object separately
                object configuration;
                if (tenant.IsS3Storage())
                {
                    configuration = new
                    {
                        BucketName = tenant.S3BucketName,
                        BucketRegion = tenant.S3BucketRegion,
                        BucketArn = tenant.S3BucketArn
                    };
                }
                else
                {
                    configuration = new
                    {
                        TenantDirectory = tenant.TenantDirectory
                    };
                }

                var response = new
                {
                    TenantName = tenantName,
                    StorageType = tenant.StorageType,
                    ValidationResult = validationResult,
                    Message = validationResult ? "Storage validation successful" : "Storage validation failed",
                    Configuration = configuration
                };

                if (validationResult)
                {
                    _logger.LogInformation("Storage validation successful for tenant: {TenantName}", tenantName);
                    return Ok(response);
                }
                else
                {
                    _logger.LogWarning("Storage validation failed for tenant: {TenantName}", tenantName);
                    return BadRequest(response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating storage for tenant: {TenantName}", tenantName);
                return StatusCode(500, new { Error = "Storage validation failed", Details = ex.Message });
            }
        }

       
        /// Get storage statistics for a tenant
        
        [HttpGet("{tenantName}/storage-stats")]
        public async Task<ActionResult<object>> GetTenantStorageStats(string tenantName)
        {
            try
            {
                _logger.LogInformation("Getting storage statistics for tenant: {TenantName}", tenantName);
                
                var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                if (tenant == null)
                {
                    return NotFound(new { Error = $"Tenant '{tenantName}' not found" });
                }

                var statistics = await _storageService.GetStorageStatisticsAsync(tenantName);
                
                // FIX: Create configuration object separately
                object configuration;
                if (tenant.IsS3Storage())
                {
                    configuration = new
                    {
                        BucketName = tenant.S3BucketName,
                        BucketRegion = tenant.S3BucketRegion
                    };
                }
                else
                {
                    configuration = new
                    {
                        TenantDirectory = tenant.TenantDirectory
                    };
                }
                
                var response = new
                {
                    TenantName = statistics.TenantName,
                    StorageType = statistics.StorageType,
                    TotalFiles = statistics.TotalFiles,
                    TotalSizeBytes = statistics.TotalSizeBytes,
                    TotalSizeGB = Math.Round(statistics.TotalSizeBytes / (1024.0 * 1024.0 * 1024.0), 2),
                    LastCalculated = statistics.LastCalculated,
                    Configuration = configuration
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting storage statistics for tenant: {TenantName}", tenantName);
                return StatusCode(500, new { Error = "Failed to get storage statistics", Details = ex.Message });
            }
        }
       
        /// Get example request for creating tenants with different storage types
        
        [HttpGet("examples/create-requests")]
        public ActionResult<object> GetCreateExamples()
        {
            var examples = new
            {
                S3Example = new
                {
                    tenantName = "company-a",
                    tenantGroup = "enterprise",
                    description = "Company A Production Environment",
                    storageType = "S3",
                    s3Config = new
                    {
                        bucketName = "xr50-tenant-company-a",
                        bucketRegion = "eu-west-1",
                        bucketArn = "arn:aws:s3:::xr50-tenant-company-a",
                        endpoint = "https://storage.lab.synelixis.com"
                    },
                    owner = new
                    {
                        userName = "admin",
                        fullName = "Company A Administrator",
                        userEmail = "admin@company-a.com",
                        password = "secure-password-123",
                        admin = true
                    }
                },
                OwnCloudExample = new
                {
                    tenantName = "test-tenant",
                    tenantGroup = "development",
                    description = "Test Environment for Development",
                    storageType = "OwnCloud",
                    ownCloudConfig = new
                    {
                        tenantDirectory = "test-tenant-files",
                        endpoint = "http://owncloud:8080"
                    },
                    owner = new
                    {
                        userName = "testadmin",
                        fullName = "Test Administrator",
                        userEmail = "test@example.com",
                        password = "test123",
                        admin = true
                    }
                },
                Instructions = new[]
                {
                    "For S3 storage: Ensure bucket is pre-provisioned by infrastructure team",
                    "For OwnCloud storage: Directory will be created automatically",
                    "S3 buckets must exist and be accessible before tenant creation",
                    "All required fields must be provided based on storage type"
                }
            };

            return Ok(examples);
        }
    }
}