using System.ComponentModel.DataAnnotations;

namespace XR50TrainingAssetRepo.Models.DTOs
{
    public class TenantResponse
    {
        public string TenantName { get; set; } = "";
        public string? TenantGroup { get; set; }
        public string? Description { get; set; }
        public string? OwnerName { get; set; }
        public string StorageType { get; set; } = "";
        public string? StorageEndpoint { get; set; }
        public string? DefaultAICollection { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Storage-specific configuration (conditionally populated)
        public S3ConfigurationResponse? S3Config { get; set; }
        public OwnCloudConfigurationResponse? OwnCloudConfig { get; set; }
        
        // User information
        public UserResponse? Owner { get; set; }
        public List<string> AdminUsers { get; set; } = new();
        
        // Factory method to create from XR50Tenant
        public static TenantResponse FromTenant(XR50Tenant tenant)
        {
            var response = new TenantResponse
            {
                TenantName = tenant.TenantName,
                TenantGroup = tenant.TenantGroup,
                Description = tenant.Description,
                OwnerName = tenant.OwnerName,
                StorageType = tenant.StorageType,
                StorageEndpoint = tenant.StorageEndpoint,
                DefaultAICollection = tenant.DefaultAICollection,
                CreatedAt = tenant.CreatedAt,
                UpdatedAt = tenant.UpdatedAt
            };
            
            // Add storage-specific configuration
            if (tenant.IsS3Storage() || tenant.IsMinIOStorage())
            {
                response.S3Config = new S3ConfigurationResponse
                {
                    BucketName = tenant.S3BucketName,
                    BucketRegion = tenant.S3BucketRegion,
                    BucketArn = tenant.S3BucketArn,
                    Endpoint = tenant.StorageEndpoint
                };
            }
            else if (tenant.IsOwnCloudStorage())
            {
                response.OwnCloudConfig = new OwnCloudConfigurationResponse
                {
                    TenantDirectory = tenant.TenantDirectory,
                    Endpoint = tenant.StorageEndpoint
                };
            }
            
            // Add owner information
            if (tenant.Owner != null)
            {
                response.Owner = new UserResponse
                {
                    UserName = tenant.Owner.UserName,
                    FullName = tenant.Owner.FullName,
                    UserEmail = tenant.Owner.UserEmail,
                    Admin = tenant.Owner.admin
                };
            }
            
            // Add admin users
            response.AdminUsers = tenant.TenantAdmins.Select(ta => ta.UserName).ToList();
            
            return response;
        }
    }
    
    public class CreateTenantRequest
    {
        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string TenantName { get; set; } = "";
        
        public string? TenantGroup { get; set; }
        public string? Description { get; set; }
        public string? OwnerName { get; set; }
        
        [Required]
        public string StorageType { get; set; } = "OwnCloud"; // "S3", "OwnCloud", "MinIO"

        // Optional. Per-tenant DataLens collection used as the fallback for AIAssistantMaterials
        // that don't define their own CollectionName. If omitted, the controller derives one
        // from the tenant name so tenants never share a collection.
        public string? DefaultAICollection { get; set; }

        // Storage-specific configuration
        public S3ConfigurationRequest? S3Config { get; set; }
        public OwnCloudConfigurationRequest? OwnCloudConfig { get; set; }
        
        // Owner user details
        public UserRequest? Owner { get; set; }
        
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TenantName))
                throw new ArgumentException("TenantName is required");
            
            if (string.IsNullOrWhiteSpace(StorageType))
                throw new ArgumentException("StorageType is required");
            
            // Validate storage-specific configuration
            if (StorageType.Equals("S3", StringComparison.OrdinalIgnoreCase) || 
                StorageType.Equals("MinIO", StringComparison.OrdinalIgnoreCase))
            {
                if (S3Config == null)
                    throw new ArgumentException("S3Config is required for S3/MinIO storage");
                
                S3Config.Validate();
            }
            else if (StorageType.Equals("OwnCloud", StringComparison.OrdinalIgnoreCase))
            {
                if (OwnCloudConfig == null)
                    throw new ArgumentException("OwnCloudConfig is required for OwnCloud storage");
                
                OwnCloudConfig.Validate();
            }
            else
            {
                throw new ArgumentException($"Unsupported storage type: {StorageType}");
            }
        }
    }
    
    public class S3ConfigurationRequest
    {
        [Required]
        public string BucketName { get; set; } = "";
        
        [Required]
        public string BucketRegion { get; set; } = "";
        
        public string? BucketArn { get; set; }
        public string? Endpoint { get; set; }
        
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(BucketName))
                throw new ArgumentException("BucketName is required");
            
            if (string.IsNullOrWhiteSpace(BucketRegion))
                throw new ArgumentException("BucketRegion is required");
        }
    }
    
    public class S3ConfigurationResponse
    {
        public string? BucketName { get; set; }
        public string? BucketRegion { get; set; }
        public string? BucketArn { get; set; }
        public string? Endpoint { get; set; }
    }
    
    public class OwnCloudConfigurationRequest
    {
        [Required]
        public string TenantDirectory { get; set; } = "";
        
        public string? Endpoint { get; set; }
        
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TenantDirectory))
                throw new ArgumentException("TenantDirectory is required");
        }
    }
    
    public class OwnCloudConfigurationResponse
    {
        public string? TenantDirectory { get; set; }
        public string? Endpoint { get; set; }
    }
    
    public class UserRequest
    {
        [Required]
        public string UserName { get; set; } = "";
        
        public string? FullName { get; set; }
        public string? UserEmail { get; set; }
        
        [Required]
        public string Password { get; set; } = "";
        
        public bool Admin { get; set; } = false;
    }
    
    public class UserResponse
    {
        public string UserName { get; set; } = "";
        public string? FullName { get; set; }
        public string? UserEmail { get; set; }
        public bool Admin { get; set; }
    }
}