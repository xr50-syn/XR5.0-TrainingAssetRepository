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

        // AI Assistant Configuration
        // Per-tenant DataLens collection for the generic Chat API and the default (material-less)
        // AI assistant endpoint, and the fallback collection for asset-level AI status sync.
        // AIAssistant *materials* do NOT use this — each gets its own collection (aiassist_{id}).
        // Per-tenant scoping keeps tenants from sharing one global collection (which would expose
        // documents across tenants through the chatbot).
        public string? DefaultAICollection { get; set; }

        // INNOV Chatbot ("LLM Engine") Configuration
        // Per-tenant connection to the partner LLM Engine. The API token is a secret: it is
        // stored in the tenant registry but must never be logged or echoed in responses.
        public string? InnovChatbotBaseUrl { get; set; }
        public string? InnovChatbotApiToken { get; set; }
        // Fallback pilot for InnovChatbotMaterials that don't define their own Pilot.
        public string? InnovChatbotDefaultPilot { get; set; }

        public bool IsInnovChatbotConfigured() => !string.IsNullOrEmpty(InnovChatbotBaseUrl);

        // XR5.0 Hub Integration
        // Tenant id used by the Hub IAM (session token "tenantId" claim). Hub-authenticated
        // requests are scoped to the tenant whose HubTenantId matches; unset means the tenant
        // is not reachable through Hub session tokens.
        public Guid? HubTenantId { get; set; }

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