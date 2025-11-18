using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services;
using System.Diagnostics;

namespace XR50TrainingAssetRepo.Services
{
    public interface IAssetService
    {
        // Basic Asset Operations
        Task<IEnumerable<Asset>> GetAllAssetsAsync();
        Task<Asset?> GetAssetAsync(int id);
        Task<Asset> CreateAssetReference(string tenantName, AssetReferenceData assetRefData);
        Task<Asset> CreateAssetAsync(Asset asset, string tenantName, IFormFile file);
        Task<Asset> UpdateAssetAsync(Asset asset);
        Task<bool> DeleteAssetAsync(string tenantName, int id);
        Task<bool> AssetExistsAsync(int id);
        
        // Asset Search and Filtering
        Task<IEnumerable<Asset>> GetAssetsByFiletypeAsync(string filetype);
        Task<IEnumerable<Asset>> SearchAssetsByFilenameAsync(string searchTerm);
        Task<IEnumerable<Asset>> GetAssetsByDescriptionAsync(string searchTerm);
        
        // Asset Relationships
        Task<IEnumerable<Material>> GetMaterialsUsingAssetAsync(int assetId);
        Task<int> GetAssetUsageCountAsync(int assetId);
        
        // File Management with Storage Service
        Task<string> GetAssetDownloadUrlAsync(int assetId);
        Task<Asset> UploadAssetAsync(IFormFile file, string tenantName, string filename, string? description = null);
        Task<bool> DeleteAssetFileAsync(int assetId);
        Task<long> GetAssetFileSizeAsync(int assetId);
        Task<bool> AssetFileExistsAsync(int assetId);
        // Share Management
        Task<Share> CreateShareAsync(string tenantName, string assetId);
        Task<bool> DeleteShareAsync(string tenantName, string shareId);
        Task<IEnumerable<Share>> GetAssetSharesAsync(string tenantName, string assetId);
        Task<IEnumerable<Share>> GetTenantSharesAsync(string tenantName);
        Task<string> GetAssetShareUrlAsync(string tenantName, string assetId);
    }

    public class AssetService : IAssetService
    {
        private readonly IConfiguration _configuration;
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly IMaterialService _materialService;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IStorageService _storageService; // Unified storage interface
        private readonly ILogger<AssetService> _logger;

        public AssetService(
            IConfiguration configuration,
            IXR50TenantDbContextFactory dbContextFactory,
            IMaterialService materialService,
            IXR50TenantManagementService tenantManagementService,
            IStorageService storageService,
            ILogger<AssetService> logger)
        {
            _configuration = configuration;
            _dbContextFactory = dbContextFactory;
            _materialService = materialService;
            _tenantManagementService = tenantManagementService;
            _storageService = storageService;
            _logger = logger;
        }

        public async Task<IEnumerable<Asset>> GetAllAssetsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Assets.OrderBy(a => a.Filename).ToListAsync();
        }

        public async Task<Asset?> GetAssetAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Assets.FindAsync(id);
        }
        public async Task<Asset> CreateAssetReference(string tenantName, AssetReferenceData assetRefData)
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            var asset = new Asset
            {
                Filename = assetRefData.Filename ?? GenerateFilenameFromUrl(assetRefData.Src ?? assetRefData.URL),
                Description = assetRefData.Description,
                Filetype = assetRefData.Filetype ?? GetFiletypeFromFilename(assetRefData.Filename ?? assetRefData.Src ?? assetRefData.URL),
                Src = assetRefData.Src ?? assetRefData.URL,
                URL = assetRefData.URL ?? assetRefData.Src
            };

            context.Assets.Add(asset);
            await context.SaveChangesAsync();
            
            _logger.LogInformation("Created asset reference {AssetId} pointing to {Src}", asset.Id, asset.Src);
            return asset;
        }
        // NEW: Helper to generate filename from URL
        private string GenerateFilenameFromUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return Guid.NewGuid().ToString();
            
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var filename = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(filename))
                    return filename;
            }
            
            return Guid.NewGuid().ToString();
        }

        public async Task<Asset> CreateAssetAsync(Asset asset, string tenantName, IFormFile file)
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            try
            {
                // Upload file to storage first
                using var stream = file.OpenReadStream();
                var uploadUrl = await _storageService.UploadFileAsync(tenantName, asset.Filename, file);
                
                // Update asset with storage URL
                asset.URL = uploadUrl;
                
                // Save to database
                context.Assets.Add(asset);
                await context.SaveChangesAsync();
                
                _logger.LogInformation("Created asset {AssetId} ({Filename}) in {StorageType} storage", 
                    asset.Id, asset.Filename, _storageService.GetStorageType());

                // Auto-share if storage supports it
                if (_storageService.SupportsSharing())
                {
                    try
                    {
                        var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                        if (tenant != null)
                        {
                            var shareUrl = await _storageService.CreateShareAsync(tenantName, tenant, asset);
                            
                            if (!string.IsNullOrEmpty(shareUrl))
                            {
                                // Update asset URL with share URL and create share record
                                asset.URL = shareUrl;
                                await CreateShareRecord(context, asset.Id.ToString(), tenant.TenantGroup ?? "");
                                await context.SaveChangesAsync();
                                
                                _logger.LogInformation("Automatically shared asset {AssetId} with tenant group", asset.Id);
                            }
                        }
                    }
                    catch (Exception shareEx)
                    {
                        // Don't fail asset creation if sharing fails
                        _logger.LogWarning(shareEx, "Failed to auto-share asset {AssetId}, but asset creation succeeded", asset.Id);
                    }
                }
                
                return asset;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create asset {Filename} for tenant {TenantName}", 
                    asset.Filename, tenantName);
                throw;
            }
        }
        public async Task<Asset> UpdateAssetAsync(Asset asset)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Find existing asset
                var existing = await context.Assets.FindAsync(asset.Id);
                if (existing == null)
                {
                    throw new KeyNotFoundException($"Asset {asset.Id} not found");
                }

                // Delete old asset
                context.Assets.Remove(existing);
                await context.SaveChangesAsync();

                // Add new asset with same ID (full replacement)
                context.Assets.Add(asset);
                await context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation("Updated asset {AssetId} ({Filename}) via delete-recreate",
                    asset.Id, asset.Filename);

                return asset;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> DeleteAssetAsync(string tenantName, int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var asset = await context.Assets.FindAsync(id);
            if (asset == null)
            {
                return false;
            }

            try
            {
                // Delete file from storage
                var storageDeleted = await _storageService.DeleteFileAsync(tenantName, asset.Filename);
                if (!storageDeleted)
                {
                    _logger.LogWarning("Failed to delete file {Filename} from storage, but continuing with database deletion",
                        asset.Filename);
                }

                // Delete from database
                context.Assets.Remove(asset);
                await context.SaveChangesAsync();

                _logger.LogInformation("Deleted asset {AssetId} ({Filename}) from {StorageType} storage",
                    id, asset.Filename, _storageService.GetStorageType());

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete asset {AssetId} ({Filename})", id, asset.Filename);
                throw;
            }
        }

        public async Task<bool> AssetExistsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Assets.AnyAsync(a => a.Id == id);
        }

        public async Task<IEnumerable<Asset>> GetAssetsByFiletypeAsync(string filetype)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Assets
                .Where(a => a.Filetype == filetype)
                .OrderBy(a => a.Filename)
                .ToListAsync();
        }

        public async Task<IEnumerable<Asset>> SearchAssetsByFilenameAsync(string searchTerm)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Assets
                .Where(a => a.Filename.Contains(searchTerm))
                .OrderBy(a => a.Filename)
                .ToListAsync();
        }

        public async Task<IEnumerable<Asset>> GetAssetsByDescriptionAsync(string searchTerm)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Assets
                .Where(a => a.Description != null && a.Description.Contains(searchTerm))
                .OrderBy(a => a.Filename)
                .ToListAsync();
        }

        public async Task<IEnumerable<Material>> GetMaterialsUsingAssetAsync(int assetId)
        {
            var asset = await GetAssetAsync(assetId);
            if (asset == null)
            {
                return new List<Material>();
            }

            return await _materialService.GetMaterialsByAssetIdAsync(assetId);
        }

        public async Task<int> GetAssetUsageCountAsync(int assetId)
        {
            var materials = await GetMaterialsUsingAssetAsync(assetId);
            return materials.Count();
        }

        public async Task<string> GetAssetDownloadUrlAsync(int assetId)
        {
            var asset = await GetAssetAsync(assetId);
            if (asset == null)
            {
                throw new ArgumentException($"Asset with ID {assetId} not found");
            }

            try
            {
                // Extract tenant name from context or determine from asset
                var tenantName = ExtractTenantNameFromContext();

                var downloadUrl = await _storageService.GetDownloadUrlAsync(tenantName, asset.Filename);

                _logger.LogInformation("Generated download URL for asset {AssetId} ({Filename}) from {StorageType}",
                    assetId, asset.Filename, _storageService.GetStorageType());

                return downloadUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate download URL for asset {AssetId}", assetId);
                throw;
            }
        }

        public async Task<Asset> UploadAssetAsync(IFormFile file, string tenantName, string filename, string? description = null)
        {
            try
            {
                _logger.LogInformation("Uploading asset {Filename} to {StorageType} storage", filename, _storageService.GetStorageType());

                // Upload file to storage
                using var stream = file.OpenReadStream();
                var uploadResult = await _storageService.UploadFileAsync(tenantName, filename, file);

                // Create asset record
                var asset = new Asset
                {
                    Filename = filename,
                    Description = description,
                    Filetype = GetFiletypeFromFilename(filename),
                    Src = uploadResult
                };

                return await CreateAssetAsync(asset, tenantName, file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload asset {Filename} to tenant {TenantName}", filename, tenantName);
                throw;
            }
        }

        public async Task<bool> DeleteAssetFileAsync(int assetId)
        {
            var asset = await GetAssetAsync(assetId);
            if (asset == null)
            {
                return false;
            }

            try
            {
                var tenantName = ExtractTenantNameFromContext();
                var deleted = await _storageService.DeleteFileAsync(tenantName, asset.Filename);

                _logger.LogInformation("Deleted file for asset {AssetId} ({Filename}) from {StorageType} storage",
                    assetId, asset.Filename, _storageService.GetStorageType());

                return deleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file for asset {AssetId} ({Filename})", assetId, asset.Filename);
                return false;
            }
        }

        public async Task<long> GetAssetFileSizeAsync(int assetId)
        {
            var asset = await GetAssetAsync(assetId);
            if (asset == null)
            {
                return 0;
            }

            try
            {
                var tenantName = ExtractTenantNameFromContext();
                var size = await _storageService.GetFileSizeAsync(tenantName, asset.Filename);

                _logger.LogInformation("Retrieved file size for asset {AssetId} ({Filename}): {Size} bytes",
                    assetId, asset.Filename, size);

                return size;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get file size for asset {AssetId} ({Filename})", assetId, asset.Filename);
                return 0;
            }
        }

        public async Task<bool> AssetFileExistsAsync(int assetId)
        {
            var asset = await GetAssetAsync(assetId);
            if (asset == null)
            {
                return false;
            }

            try
            {
                var tenantName = ExtractTenantNameFromContext();
                var exists = await _storageService.FileExistsAsync(tenantName, asset.Filename);

                _logger.LogInformation("File existence check for asset {AssetId} ({Filename}): {Exists}",
                    assetId, asset.Filename, exists);

                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check file existence for asset {AssetId} ({Filename})", assetId, asset.Filename);
                return false;
            }
        }


       /* public async Task<AssetStatistics> GetAssetStatisticsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();

            var totalAssets = await context.Assets.CountAsync();
            var filetypeGroups = await context.Assets
                .GroupBy(a => a.Filetype)
                .Select(g => new { Filetype = g.Key, Count = g.Count() })
                .ToListAsync();

            // Calculate total storage used by querying storage service
            long totalStorageUsed = 0;
            try
            {
                var tenantName = ExtractTenantNameFromContext();
                var storageStats = await _storageService.GetStorageStatisticsAsync(tenantName);
                totalStorageUsed = storageStats.TotalSizeBytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get storage statistics for asset statistics calculation");
            }

            var statistics = new AssetStatistics
            {
                TotalAssets = totalAssets,
                FiletypeBreakdown = filetypeGroups.ToDictionary(g => g.Filetype ?? "unknown", g => g.Count),
                TotalStorageUsed = totalStorageUsed,
                AverageFileSize = totalAssets > 0 ? totalStorageUsed / totalAssets : 0
            };

            return statistics;
        }
*/
        public async Task<Share> CreateShareAsync(string tenantName, string assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            try
            {
                if (!_storageService.SupportsSharing())
                {
                    throw new NotSupportedException($"{_storageService.GetStorageType()} storage does not support sharing");
                }

                var asset = await context.Assets.FindAsync(int.Parse(assetId));
                if (asset == null)
                {
                    throw new ArgumentException($"Asset with ID {assetId} not found");
                }

                var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                if (tenant == null)
                {
                    throw new ArgumentException($"Tenant {tenantName} not found");
                }

                // Create share via storage service
                var shareUrl = await _storageService.CreateShareAsync(tenantName, tenant, asset);
                
                if (string.IsNullOrEmpty(shareUrl))
                {
                    throw new InvalidOperationException("Failed to create share in storage service");
                }

                // Create share record in database
                var share = await CreateShareRecord(context, assetId, tenant.TenantGroup ?? "");
                
                // Update asset URL
                asset.URL = shareUrl;
                await context.SaveChangesAsync();

                _logger.LogInformation("Created share {ShareId} for asset {AssetId}", share.ShareId, assetId);
                return share;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create share for asset {AssetId}", assetId);
                throw;
            }
        }

       
        /// Delete a share from both database and storage
        
        public async Task<bool> DeleteShareAsync(string tenantName, string shareId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            try
            {
                var share = await context.Shares.FindAsync(shareId);
                if (share == null)
                {
                    return false;
                }

                // Delete from storage service if supported
                if (_storageService.SupportsSharing())
                {
                    await _storageService.DeleteShareAsync(tenantName, shareId);
                }

                // Delete from database
                context.Shares.Remove(share);
                await context.SaveChangesAsync();

                _logger.LogInformation("Deleted share {ShareId}", shareId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete share {ShareId}", shareId);
                return false;
            }
        }

       
        /// Get shares for an asset
        
        public async Task<IEnumerable<Share>> GetAssetSharesAsync(string tenantName, string assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            return await context.Shares
                .Where(s => s.FileId == assetId)
                .ToListAsync();
        }

       
        /// Get all shares for a tenant
        
        public async Task<IEnumerable<Share>> GetTenantSharesAsync(string tenantName)
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            return await context.Shares.ToListAsync();
        }

       
        /// Get the share URL for an asset
        
        public async Task<string> GetAssetShareUrlAsync(string tenantName, string assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            var asset = await context.Assets.FindAsync(int.Parse(assetId));
            return asset?.URL ?? string.Empty;
        }

        private async Task<Share> CreateShareRecord(XR50TrainingContext context, string assetId, string target)
        {
            var share = new Share
            {
                FileId = assetId,
                Type = ShareType.Group,
                Target = target
            };

            context.Shares.Add(share);
            await context.SaveChangesAsync();
            
            return share;
        }

        private string GetFiletypeFromFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return "unknown";

            var extension = Path.GetExtension(filename).ToLowerInvariant();

            return extension switch
            {
                ".mp4" or ".avi" or ".mov" or ".wmv" or ".webm" => "video",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" => "image",
                ".pdf" => "document",
                ".doc" or ".docx" => "document",
                ".xls" or ".xlsx" => "spreadsheet",
                ".ppt" or ".pptx" => "presentation",
                ".txt" or ".md" => "text",
                ".json" => "data",
                ".zip" or ".rar" or ".7z" => "archive",
                ".unity" or ".unitypackage" => "unity",
                ".fbx" or ".obj" or ".3ds" => "3d_model",
                ".wav" or ".mp3" or ".ogg" or ".flac" => "audio",
                _ => "unknown"
            };
        }

        private string ExtractTenantNameFromContext()
        {
            // This is a simplified implementation
            // In a real scenario, you might extract tenant from:
            // - HTTP context (URL path, headers, claims)
            // - Database lookup
            // - Configuration

            // For now, return a default tenant name
            // TODO: Implement proper tenant resolution
            return "default-tenant";
        }    
    }


    public class AssetStatistics
    {
        public int TotalAssets { get; set; }
        public Dictionary<string, int> FiletypeBreakdown { get; set; } = new();
        public long TotalStorageUsed { get; set; } // In bytes
        public long AverageFileSize { get; set; } // In bytes
    }

    public class AssetUploadRequest
    {
        public string Filename { get; set; } = "";
        public string? Description { get; set; }
        public string? Filetype { get; set; }
    }

    public class AssetSearchRequest
    {
        public string? SearchTerm { get; set; }
        public string? Filetype { get; set; }
        public int? Skip { get; set; }
        public int? Take { get; set; }
    }
    
}