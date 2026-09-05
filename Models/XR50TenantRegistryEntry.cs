namespace XR50TrainingAssetRepo.Models
{
    /// <summary>
    /// A row of the central tenant registry (<c>XR50TenantRegistry</c> in the base database).
    /// This entity exists so that EF Core migrations own the table's schema; the services that
    /// read and write the registry still use their existing SQL. Shape mirrors the table
    /// column-for-column, so changing it means adding a migration for
    /// <see cref="Data.XR50RegistryContext"/>.
    /// </summary>
    public class XR50TenantRegistryEntry
    {
        public string TenantName { get; set; } = "";
        public string? TenantGroup { get; set; }
        public string? Description { get; set; }
        public string StorageType { get; set; } = "OwnCloud";
        public string? TenantDirectory { get; set; }
        public string? S3BucketName { get; set; }
        public string? S3BucketRegion { get; set; }
        public string? S3BucketArn { get; set; }
        public string? StorageEndpoint { get; set; }
        public string? OwnerName { get; set; }
        public string? DefaultAICollection { get; set; }
        public string? InnovChatbotBaseUrl { get; set; }
        public string? InnovChatbotApiToken { get; set; }
        public string? InnovChatbotDefaultPilot { get; set; }
        public Guid? HubTenantId { get; set; }
        public string DatabaseName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
