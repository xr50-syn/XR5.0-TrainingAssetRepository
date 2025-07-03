namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Storage abstraction interface that works with both S3 and OwnCloud
    /// </summary>
    public interface IStorageService
    {
        // Tenant Storage Management
        Task<bool> CreateTenantStorageAsync(string tenantName, XR50Tenant tenant);
        Task<bool> DeleteTenantStorageAsync(string tenantName);
        Task<bool> TenantStorageExistsAsync(string tenantName);
        
        // File Operations
        Task<string> UploadFileAsync(string tenantName, string fileName, Stream fileStream, string contentType = "application/octet-stream");
        Task<Stream> DownloadFileAsync(string tenantName, string fileName);
        Task<string> GetDownloadUrlAsync(string tenantName, string fileName, TimeSpan? expiration = null);
        Task<bool> DeleteFileAsync(string tenantName, string fileName);
        Task<bool> FileExistsAsync(string tenantName, string fileName);
        Task<long> GetFileSizeAsync(string tenantName, string fileName);
        
        // Storage Info
        Task<StorageStatistics> GetStorageStatisticsAsync(string tenantName);
        string GetStorageType(); // Returns "S3" or "OwnCloud"
    }
    
    public class StorageStatistics
    {
        public string TenantName { get; set; } = "";
        public string StorageType { get; set; } = "";
        public long TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public DateTime LastCalculated { get; set; } = DateTime.UtcNow;
    }
}