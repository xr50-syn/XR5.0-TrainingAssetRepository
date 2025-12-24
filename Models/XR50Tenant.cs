using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Models
{
    public class XR50Tenant
    {
        [Key]
        public string TenantName { get; set; } = "";
        
        public string? TenantGroup { get; set; }
        public string? TenantSchema { get; set; }
        public string? Description { get; set; }
        public string? OwnerName { get; set; }
        
        // Storage Configuration
        public string StorageType { get; set; } = "OwnCloud"; // "S3", "OwnCloud", "MinIO"
        public string? StorageEndpoint { get; set; }
        
        // S3/MinIO specific properties
        public string? S3BucketName { get; set; }
        public string? S3BucketRegion { get; set; }
        public string? S3BucketArn { get; set; }
        
        // OwnCloud specific properties
        public string? TenantDirectory { get; set; }
        
        // User Management
        public User? Owner { get; set; }
        public virtual ICollection<TenantAdmin> TenantAdmins { get; set; } = new List<TenantAdmin>();
        
        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        // Helper methods
        public bool IsS3Storage() => StorageType.Equals("S3", StringComparison.OrdinalIgnoreCase);
        public bool IsOwnCloudStorage() => StorageType.Equals("OwnCloud", StringComparison.OrdinalIgnoreCase);
        public bool IsMinIOStorage() => StorageType.Equals("MinIO", StringComparison.OrdinalIgnoreCase);

        
        public void ValidateS3Configuration()
        {
            if (IsS3Storage() || IsMinIOStorage())
            {
                if (string.IsNullOrEmpty(S3BucketName))
                    throw new InvalidOperationException("S3BucketName is required for S3/MinIO storage");
                
                if (string.IsNullOrEmpty(S3BucketRegion))
                    throw new InvalidOperationException("S3BucketRegion is required for S3/MinIO storage");
            }
        }
        
        public void ValidateOwnCloudConfiguration()
        {
            if (IsOwnCloudStorage())
            {
                if (string.IsNullOrEmpty(TenantDirectory))
                    throw new InvalidOperationException("TenantDirectory is required for OwnCloud storage");
            }
        }
        
        public XR50Tenant()
        {
            TenantName = "";
        }
    }
    
    public class TenantAdmin
    {
        public string TenantName { get; set; } = "";
        public string UserName { get; set; } = "";
        
        // Navigation properties  
        public virtual XR50Tenant Tenant { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}