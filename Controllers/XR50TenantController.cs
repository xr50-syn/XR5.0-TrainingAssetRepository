using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Infrastructure.Auth;
using XR50TrainingAssetRepo.Infrastructure.ErrorHandling;
using Microsoft.AspNetCore.Authorization;

namespace XR50TrainingAssetRepo.Controllers
{
    [ApiExplorerSettings(GroupName = "tenants")]
    [Route("xr50/trainingAssetRepository/[controller]")]
    [ApiController]
    public class TenantsController : ControllerBase
    {
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IStorageService _storageService;
        private readonly IamOptions _iamOptions;
        private readonly ILogger<TenantsController> _logger;

        public TenantsController(
            IXR50TenantManagementService tenantManagementService,
            IStorageService storageService,
            Microsoft.Extensions.Options.IOptions<IamOptions> iamOptions,
            ILogger<TenantsController> logger)
        {
            _tenantManagementService = tenantManagementService;
            _storageService = storageService;
            _iamOptions = iamOptions.Value;
            _logger = logger;
        }

       
        /// Get all tenants
        
        [HttpGet]
        [Authorize(Policy = "SystemAdmin")]
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
                return this.ProblemServerError("Failed to retrieve tenants.");
            }
        }


        /// Create a new tenant with pre-provisioned infrastructure.
        /// System admins create tenants freely; Hub-authenticated users self-provision the
        /// tenant for their OWN Hub tenant id (the token's tenantId claim), becoming its
        /// tenant admin. Self-service can never bind another Hub tenant's id, mint a
        /// system admin, or create a second tenant for the same Hub tenant.

        [HttpPost]
        [Authorize(Policy = "TenantCreator")]
        public async Task<ActionResult<TenantResponse>> CreateTenant([FromBody] CreateTenantRequest request)
        {
            try
            {
                _logger.LogInformation("Creating tenant: {TenantName} with {StorageType} storage",
                    request.TenantName, request.StorageType);

                // Validate the request
                request.Validate();

                // Self-service: a Hub-authenticated caller without a system-admin role binds
                // the new tenant to their own Hub tenant id; any caller-supplied hubTenantId
                // is ignored. System admins keep the request's value.
                var callerHubTenantId = User.GetHubTenantId();
                var selfService = callerHubTenantId.HasValue && !User.IsSystemAdmin(_iamOptions);
                var effectiveHubTenantId = selfService ? callerHubTenantId : request.HubTenantId;

                if (selfService && request.Owner != null && request.Owner.Admin)
                {
                    // The owner row's admin flag maps to the SYSTEM-admin role; self-service
                    // must not be able to mint one. Tenant-admin rights are granted below.
                    _logger.LogInformation("Self-service tenant creation: downgrading owner admin flag for {TenantName}",
                        request.TenantName);
                    request.Owner.Admin = false;
                }

                // One local tenant per Hub tenant id. Checked here (not left to the unique
                // index) because the registry INSERT uses ON DUPLICATE KEY UPDATE, which on a
                // HubTenantId collision would silently rewrite the OTHER tenant's row.
                if (effectiveHubTenantId.HasValue
                    && await _tenantManagementService.GetTenantByHubTenantIdAsync(effectiveHubTenantId.Value) is XR50Tenant existing)
                {
                    _logger.LogWarning("Hub tenant id {HubTenantId} is already mapped to tenant {TenantName}",
                        effectiveHubTenantId.Value, existing.TenantName);
                    return this.ProblemConflict(selfService
                        ? "Your Hub tenant already has a training tenant."
                        : $"Hub tenant id '{effectiveHubTenantId.Value:D}' is already mapped to another tenant.");
                }

                // Validate storage type matches running implementation
                var runningStorageType = _storageService.GetStorageType();
                if (!request.StorageType.Equals(runningStorageType, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Storage type mismatch: requested {RequestedType}, but server is configured for {RunningType}",
                        request.StorageType, runningStorageType);
                    return this.ProblemBadRequest($"Storage type mismatch. This server is configured for {runningStorageType} storage. Cannot create tenant with {request.StorageType} storage.");
                }

                // Derive a per-tenant default AI collection if the caller didn't supply one.
                // Sharing a collection across tenants would let chatbot queries surface another
                // tenant's documents — see the "default collection cross-tenant" security fix.
                var defaultAICollection = string.IsNullOrWhiteSpace(request.DefaultAICollection)
                    ? $"aiassist_default_{System.Text.RegularExpressions.Regex.Replace(request.TenantName, @"[^a-zA-Z0-9_]", "_")}"
                    : request.DefaultAICollection;

                // Create tenant object from request
                var tenant = new XR50Tenant
                {
                    TenantName = request.TenantName,
                    TenantGroup = request.TenantGroup,
                    Description = request.Description,
                    OwnerName = request.OwnerName,
                    StorageType = request.StorageType,
                    DefaultAICollection = defaultAICollection,
                    InnovChatbotBaseUrl = request.InnovChatbotBaseUrl,
                    InnovChatbotApiToken = request.InnovChatbotApiToken,
                    InnovChatbotDefaultPilot = request.InnovChatbotDefaultPilot,
                    HubTenantId = effectiveHubTenantId,
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
                        return this.ProblemBadRequest($"Pre-provisioned S3 bucket '{tenant.S3BucketName}' is not accessible. Verify the bucket exists in the specified region, check IAM permissions, and ensure bucket name matches infrastructure configuration.");
                    }
                }
                else
                {
                    tenant.ValidateOwnCloudConfiguration();
                }

                // Create the tenant
                var createdTenant = await _tenantManagementService.CreateTenantAsync(tenant);

                // Self-service: the creator becomes tenant admin of the tenant they just
                // provisioned (matched by the e-mail from their Hub identity).
                if (selfService && !string.IsNullOrEmpty(createdTenant.TenantSchema))
                {
                    var creatorEmail = User.FindFirst("email")?.Value;
                    var creatorUserName = User.GetUserId();
                    if (!string.IsNullOrEmpty(creatorUserName))
                    {
                        await _tenantManagementService.GrantTenantAdminAsync(
                            createdTenant.TenantName, createdTenant.TenantSchema, creatorUserName, creatorEmail);
                    }
                }

                var response = TenantResponse.FromTenant(createdTenant);
                
                _logger.LogInformation("Successfully created tenant: {TenantName} with {StorageType} storage", 
                    request.TenantName, request.StorageType);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for tenant creation: {Error}", ex.Message);
                return this.ProblemBadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError("Tenant creation failed: {Error}", ex.Message);
                return this.ProblemBadRequest($"Tenant creation failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating tenant: {TenantName}", request.TenantName);
                return this.ProblemServerError("Tenant creation failed.");
            }
        }

       
        /// Get a specific tenant by name
        
        [HttpGet("{tenantName}")]
        [Authorize(Policy = "TenantMember")]
        public async Task<ActionResult<TenantResponse>> GetTenant(string tenantName)
        {
            try
            {
                _logger.LogInformation("Getting tenant: {TenantName}", tenantName);
                
                var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                if (tenant == null)
                {
                    _logger.LogWarning("Tenant not found: {TenantName}", tenantName);
                    return this.ProblemNotFound($"Tenant '{tenantName}' not found.");
                }

                var response = TenantResponse.FromTenant(tenant);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tenant: {TenantName}", tenantName);
                return this.ProblemServerError("Failed to retrieve tenant.");
            }
        }


        /// Set or clear the XR5.0 Hub tenant id this tenant is reachable under.
        /// Hub session tokens whose tenantId claim matches are scoped to this tenant.

        [HttpPut("{tenantName}/hub-tenant")]
        [Authorize(Policy = "SystemAdmin")]
        public async Task<ActionResult<TenantResponse>> SetHubTenant(string tenantName, [FromBody] SetHubTenantRequest request)
        {
            try
            {
                _logger.LogInformation("Setting Hub tenant id for tenant {TenantName} to {HubTenantId}",
                    tenantName, request.HubTenantId?.ToString("D") ?? "(none)");

                var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                if (tenant == null)
                {
                    return this.ProblemNotFound($"Tenant '{tenantName}' not found.");
                }

                tenant.HubTenantId = request.HubTenantId;
                var updated = await _tenantManagementService.UpdateTenantAsync(tenantName, tenant);

                return Ok(TenantResponse.FromTenant(updated));
            }
            catch (ArgumentException)
            {
                return this.ProblemNotFound($"Tenant '{tenantName}' not found.");
            }
            catch (MySql.Data.MySqlClient.MySqlException ex) when (ex.Number == 1062)
            {
                // Unique index on HubTenantId: one Hub tenant maps to exactly one local tenant
                _logger.LogWarning("Hub tenant id {HubTenantId} is already mapped to another tenant",
                    request.HubTenantId?.ToString("D"));
                return this.ProblemConflict($"Hub tenant id '{request.HubTenantId:D}' is already mapped to another tenant.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting Hub tenant id for tenant: {TenantName}", tenantName);
                return this.ProblemServerError("Failed to set Hub tenant id.");
            }
        }

        /// Delete a tenant (soft delete - marks as inactive)

        [HttpDelete("{tenantName}")]
        [Authorize(Policy = "SystemAdmin")]
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
                return this.ProblemServerError("Failed to delete tenant.");
            }
        }

       
        /// Validate tenant storage configuration
        
        [HttpGet("{tenantName}/validate-storage")]
        [Authorize(Policy = "TenantMember")]
        public async Task<ActionResult<object>> ValidateTenantStorage(string tenantName)
        {
            try
            {
                _logger.LogInformation("Validating storage for tenant: {TenantName}", tenantName);
                
                var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                if (tenant == null)
                {
                    return this.ProblemNotFound($"Tenant '{tenantName}' not found.");
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
                    return this.ProblemBadRequest($"Storage validation failed for tenant '{tenantName}'.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating storage for tenant: {TenantName}", tenantName);
                return this.ProblemServerError("Storage validation failed.");
            }
        }

       
        /// Get storage statistics for a tenant
        
        [HttpGet("{tenantName}/storage-stats")]
        [Authorize(Policy = "TenantMember")]
        public async Task<ActionResult<object>> GetTenantStorageStats(string tenantName)
        {
            try
            {
                _logger.LogInformation("Getting storage statistics for tenant: {TenantName}", tenantName);
                
                var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                if (tenant == null)
                {
                    return this.ProblemNotFound($"Tenant '{tenantName}' not found.");
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
                return this.ProblemServerError("Failed to get storage statistics.");
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
                    tenantName = "tenant_a",
                    tenantGroup = "enterprise",
                    description = "Tenant A Production Environment",
                    storageType = "S3",
                    defaultAICollection = "aiassist_default_tenant_a",
                    s3Config = new
                    {
                        bucketName = "xr50-tenant-a",
                        bucketRegion = "eu-west-1",
                        bucketArn = "arn:aws:s3:::xr50-tenant-a",
                        endpoint = "https://s3.example.com"
                    },
                    owner = new
                    {
                        userName = "admin",
                        fullName = "Tenant A Administrator",
                        userEmail = "admin@tenant-a.com",
                        password = "secure-password-123",
                        admin = true
                    }
                },
                OwnCloudExample = new
                {
                    tenantName = "test_tenant",
                    tenantGroup = "development",
                    description = "Test Environment for Development",
                    storageType = "OwnCloud",
                    defaultAICollection = (string?)null,
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
                    "tenantName may contain only letters, digits and underscores: it becomes the per-tenant database name, and any other character is folded to '_', so 'foo-bar' and 'foo_bar' would resolve to the same database. Bucket names are unaffected and may keep hyphens",
                    "For S3 storage: Ensure bucket is pre-provisioned by infrastructure team",
                    "For OwnCloud storage: Directory will be created automatically",
                    "S3 buckets must exist and be accessible before tenant creation",
                    "All required fields must be provided based on storage type",
                    "defaultAICollection is optional; if omitted it is auto-derived as aiassist_default_{tenantName} so tenants don't share a chatbot collection"
                }
            };

            return Ok(examples);
        }
    }
}
