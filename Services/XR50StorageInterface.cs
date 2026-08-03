using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services
{
   
    /// Storage abstraction interface that works with both S3 and OwnCloud
    
    public interface IStorageService
    {
        // Tenant Storage Management
        Task<bool> CreateTenantStorageAsync(string tenantName, XR50Tenant tenant);
        Task<bool> DeleteTenantStorageAsync(string tenantName);
        Task<bool> TenantStorageExistsAsync(string tenantName);

        // File Operations
        // fileName is the storage key within the tenant, which for hashed uploads is content-addressed
        // rather than human-readable. downloadFileName carries the name the file should be served
        // under, so backends that support it can preserve a friendly name for clients.
        Task<string> UploadFileAsync(string tenantName, string fileName, IFormFile file, string? downloadFileName = null);
        Task<Stream> DownloadFileAsync(string tenantName, string fileName);
        Task<string> GetDownloadUrlAsync(string tenantName, string fileName, TimeSpan? expiration = null);
        Task<bool> DeleteFileAsync(string tenantName, string fileName);
        Task<bool> FileExistsAsync(string tenantName, string fileName);
        Task<long> GetFileSizeAsync(string tenantName, string fileName);
        //Shares
        Task<string> CreateShareAsync(string tenantName, XR50Tenant tenant, Asset asset);
        Task<bool> DeleteShareAsync(string tenantName, string shareId);
        bool SupportsSharing();
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