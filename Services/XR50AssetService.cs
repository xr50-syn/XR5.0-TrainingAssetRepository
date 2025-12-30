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

        // AI Processing Operations
        Task<Asset> SubmitAssetForAiProcessingAsync(int assetId);
        Task<int> SyncAssetAiStatusesAsync();
        Task<IEnumerable<Asset>> GetAssetsWithAiStatusAsync(string status);
        Task<IEnumerable<Asset>> GetAssetsPendingAiProcessingAsync();
    }

    public class AssetService : IAssetService
    {
        private readonly IConfiguration _configuration;
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly IMaterialService _materialService;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IStorageService _storageService; // Unified storage interface
        private readonly ISiemensApiService _siemensApiService;
        private readonly ILogger<AssetService> _logger;

        public AssetService(
            IConfiguration configuration,
            IXR50TenantDbContextFactory dbContextFactory,
            IMaterialService materialService,
            IXR50TenantManagementService tenantManagementService,
            IStorageService storageService,
            ISiemensApiService siemensApiService,
            ILogger<AssetService> logger)
        {
            _configuration = configuration;
            _dbContextFactory = dbContextFactory;
            _materialService = materialService;
            _tenantManagementService = tenantManagementService;
            _storageService = storageService;
            _siemensApiService = siemensApiService;
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

            var filetype = assetRefData.Filetype ?? GetFiletypeFromFilename(assetRefData.Filename ?? assetRefData.Src ?? assetRefData.URL);

            var asset = new Asset
            {
                Filename = assetRefData.Filename ?? GenerateFilenameFromUrl(assetRefData.Src ?? assetRefData.URL),
                Description = assetRefData.Description,
                Filetype = filetype,
                Type = InferAssetTypeFromFiletype(filetype),
                Src = assetRefData.Src ?? assetRefData.URL,
                URL = assetRefData.URL ?? assetRefData.Src
            };

            context.Assets.Add(asset);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created asset reference {AssetId} (Type: {AssetType}, Filetype: {Filetype}) pointing to {Src}",
                asset.Id, asset.Type, asset.Filetype, asset.Src);
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

        // Helper to detect file type from binary stream using magic bytes
        private async Task<(string filetype, AssetType assetType)> DetectFileTypeFromStream(Stream stream)
        {
            _logger.LogDebug("=== DetectFileTypeFromStream: Starting binary file detection ===");
            _logger.LogDebug("Stream position before detection: {Position}, Stream length: {Length}",
                stream.Position, stream.CanSeek ? stream.Length : -1);

            // Read first 12 bytes to check file signature (magic bytes)
            var buffer = new byte[12];
            var originalPosition = stream.Position;

            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                _logger.LogDebug("Read {BytesRead} bytes from stream. Magic bytes: {MagicBytes}",
                    bytesRead,
                    string.Join(" ", buffer.Take(Math.Min(12, bytesRead)).Select(b => $"0x{b:X2}")));

                if (bytesRead < 4)
                {
                    // Not enough data to detect, fallback to unknown
                    _logger.LogDebug("Insufficient bytes read ({BytesRead}), returning unknown type", bytesRead);
                    return ("unknown", AssetType.PDF);
                }

                // Check for common file signatures (magic bytes)

                // PDF: %PDF (0x25 0x50 0x44 0x46)
                if (buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46)
                {
                    _logger.LogDebug("Detected PDF file by magic bytes");
                    return ("pdf", AssetType.PDF);
                }

                // PNG: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A
                if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
                {
                    _logger.LogDebug("Detected PNG file by magic bytes");
                    return ("png", AssetType.Image);
                }

                // JPEG: 0xFF 0xD8 0xFF
                if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
                {
                    _logger.LogDebug("Detected JPEG file by magic bytes");
                    return ("jpg", AssetType.Image);
                }

                // GIF: GIF87a or GIF89a
                if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46)
                {
                    _logger.LogDebug("Detected GIF file by magic bytes");
                    return ("gif", AssetType.Image);
                }

                // BMP: BM (0x42 0x4D)
                if (buffer[0] == 0x42 && buffer[1] == 0x4D)
                {
                    _logger.LogDebug("Detected BMP file by magic bytes");
                    return ("bmp", AssetType.Image);
                }

                // WebP: RIFF....WEBP
                if (buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
                    bytesRead >= 12 && buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50)
                    return ("webp", AssetType.Image);

                // MP4/MOV: Check for ftyp box signature
                if (bytesRead >= 8 && buffer[4] == 0x66 && buffer[5] == 0x74 && buffer[6] == 0x79 && buffer[7] == 0x70)
                {
                    // Check specific brand codes
                    if (bytesRead >= 12)
                    {
                        // mp4 brands: isom, mp41, mp42
                        if ((buffer[8] == 0x69 && buffer[9] == 0x73 && buffer[10] == 0x6F && buffer[11] == 0x6D) || // isom
                            (buffer[8] == 0x6D && buffer[9] == 0x70 && buffer[10] == 0x34))  // mp4*
                            return ("mp4", AssetType.Video);

                        // QuickTime: qt
                        if (buffer[8] == 0x71 && buffer[9] == 0x74)
                            return ("mov", AssetType.Video);
                    }
                    // Default to mp4 for ftyp
                    return ("mp4", AssetType.Video);
                }

                // AVI: RIFF....AVI
                if (buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
                    bytesRead >= 12 && buffer[8] == 0x41 && buffer[9] == 0x56 && buffer[10] == 0x49)
                    return ("avi", AssetType.Video);

                // WebM/MKV: 0x1A 0x45 0xDF 0xA3 (EBML signature)
                if (buffer[0] == 0x1A && buffer[1] == 0x45 && buffer[2] == 0xDF && buffer[3] == 0xA3)
                    return ("webm", AssetType.Video);

                // Unity bundle files typically have "UnityFS" header
                if (bytesRead >= 7 &&
                    buffer[0] == 0x55 && buffer[1] == 0x6E && buffer[2] == 0x69 && buffer[3] == 0x74 &&
                    buffer[4] == 0x79 && buffer[5] == 0x46 && buffer[6] == 0x53)
                    return ("unity", AssetType.Unity);

                // Unity asset bundle (older format)
                if (bytesRead >= 11 &&
                    buffer[0] == 0x55 && buffer[1] == 0x6E && buffer[2] == 0x69 && buffer[3] == 0x74 &&
                    buffer[4] == 0x79 && buffer[5] == 0x57 && buffer[6] == 0x65 && buffer[7] == 0x62)
                    return ("unity", AssetType.Unity);

                // GLB (binary glTF): "glTF" (0x67 0x6C 0x54 0x46)
                if (buffer[0] == 0x67 && buffer[1] == 0x6C && buffer[2] == 0x54 && buffer[3] == 0x46)
                {
                    _logger.LogDebug("Detected GLB file by magic bytes");
                    return ("glb", AssetType.Unity);
                }

                // FBX: Kaydara FBX Binary
                if (bytesRead >= 11 &&
                    buffer[0] == 0x4B && buffer[1] == 0x61 && buffer[2] == 0x79 && buffer[3] == 0x64 &&
                    buffer[4] == 0x61 && buffer[5] == 0x72 && buffer[6] == 0x61)
                {
                    _logger.LogDebug("Detected FBX file by magic bytes");
                    return ("fbx", AssetType.Unity);
                }

                // Unknown file type - fallback to PDF as default
                _logger.LogWarning("Unknown file signature: {Signature}",
                    string.Join(" ", buffer.Take(Math.Min(8, bytesRead)).Select(b => $"0x{b:X2}")));
                return ("unknown", AssetType.PDF);
            }
            finally
            {
                // Restore original stream position
                stream.Seek(originalPosition, SeekOrigin.Begin);
            }
        }

        // Helper to infer AssetType from Filetype (for reference assets without MIME)
        private AssetType InferAssetTypeFromFiletype(string? filetype)
        {
            if (string.IsNullOrEmpty(filetype))
                return AssetType.PDF; // Default when not specified

            var lower = filetype.ToLower();

            // Video types
            if (lower == "mp4" || lower == "avi" || lower == "mov" || lower == "wmv" ||
                lower == "flv" || lower == "webm" || lower == "mkv")
                return AssetType.Video;

            // PDF
            if (lower == "pdf")
                return AssetType.PDF;

            // Unity and 3D models
            if (lower == "unity" || lower == "unitypackage" || lower == "bundle" ||
                lower == "glb" || lower == "gltf" || lower == "fbx" || lower == "obj")
                return AssetType.Unity;

            // Image - png, jpg, jpeg, gif, bmp, svg, webp
            if (lower == "png" || lower == "jpg" || lower == "jpeg" || lower == "gif" ||
                lower == "bmp" || lower == "svg" || lower == "webp")
                return AssetType.Image;

            // Default to PDF for unknown types
            return AssetType.PDF;
        }

        public async Task<Asset> CreateAssetAsync(Asset asset, string tenantName, IFormFile file)
        {
            using var context = _dbContextFactory.CreateDbContext();

            try
            {
                // Detect file type from binary stream (magic bytes)
                using var stream = file.OpenReadStream();
                var (detectedFiletype, detectedType) = await DetectFileTypeFromStream(stream);

                // If binary detection failed (unknown), try to infer from file extension
                if (detectedFiletype == "unknown")
                {
                    var extensionFiletype = GetFiletypeFromFilename(asset.Filename ?? file.FileName);
                    var extensionType = InferAssetTypeFromFiletype(extensionFiletype);
                    _logger.LogInformation("Binary detection failed, using extension-based detection for {Filename}: {Filetype} -> {Type}",
                        asset.Filename ?? file.FileName, extensionFiletype, extensionType);
                    detectedFiletype = extensionFiletype;
                    detectedType = extensionType;
                }

                // Use detected values if asset properties not explicitly set
                if (string.IsNullOrEmpty(asset.Filetype))
                {
                    asset.Filetype = detectedFiletype;
                }
                asset.Type = detectedType;

                _logger.LogInformation("Detected file type from binary stream for asset {Filename}: Type={Type}, Filetype={Filetype}",
                    asset.Filename, asset.Type, asset.Filetype);

                // Upload file to storage
                stream.Seek(0, SeekOrigin.Begin); // Reset stream position after detection
                var uploadUrl = await _storageService.UploadFileAsync(tenantName, asset.Filename, file);

                // Update asset with storage URL
                asset.URL = uploadUrl;

                // Set Src if not already provided (for consistency with UploadAssetAsync)
                if (string.IsNullOrEmpty(asset.Src))
                {
                    asset.Src = uploadUrl;
                }

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

                // Detect file type from binary stream
                using var stream = file.OpenReadStream();
                var (filetype, assetType) = await DetectFileTypeFromStream(stream);

                _logger.LogInformation("Detected file type from binary stream for {Filename}: Type={Type}, Filetype={Filetype}",
                    filename, assetType, filetype);

                // Upload file to storage
                stream.Seek(0, SeekOrigin.Begin);
                var uploadResult = await _storageService.UploadFileAsync(tenantName, filename, file);

                // Create asset record
                var asset = new Asset
                {
                    Filename = filename,
                    Description = description,
                    Filetype = filetype,
                    Type = assetType,
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
                ".glb" or ".gltf" => "glb",
                ".fbx" => "fbx",
                ".obj" or ".3ds" => "3d_model",
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

        #region AI Processing Operations

        public async Task<Asset> SubmitAssetForAiProcessingAsync(int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var asset = await context.Assets.FindAsync(assetId);
            if (asset == null)
            {
                throw new KeyNotFoundException($"Asset {assetId} not found");
            }

            if (string.IsNullOrEmpty(asset.URL))
            {
                throw new InvalidOperationException($"Asset {assetId} has no URL for processing");
            }

            if (asset.AiAvailable == "process")
            {
                _logger.LogInformation("Asset {AssetId} is already being processed", assetId);
                return asset;
            }

            if (asset.AiAvailable == "ready")
            {
                _logger.LogInformation("Asset {AssetId} is already processed", assetId);
                return asset;
            }

            try
            {
                var jobId = await _siemensApiService.SubmitDocumentAsync(assetId, asset.URL);
                asset.JobId = jobId;
                asset.AiAvailable = "process";

                await context.SaveChangesAsync();

                _logger.LogInformation("Submitted asset {AssetId} for AI processing. Job ID: {JobId}", assetId, jobId);

                return asset;
            }
            catch (SiemensApiException ex)
            {
                _logger.LogError(ex, "Failed to submit asset {AssetId} for AI processing", assetId);
                throw;
            }
        }

        public async Task<int> SyncAssetAiStatusesAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();

            var processingAssets = await context.Assets
                .Where(a => a.AiAvailable == "process" && !string.IsNullOrEmpty(a.JobId))
                .ToListAsync();

            if (!processingAssets.Any())
            {
                _logger.LogDebug("No assets currently processing");
                return 0;
            }

            var updatedCount = 0;

            foreach (var asset in processingAssets)
            {
                try
                {
                    var status = await _siemensApiService.GetJobStatusAsync(asset.JobId!);

                    if (status.Status == "success")
                    {
                        asset.AiAvailable = "ready";
                        updatedCount++;
                        _logger.LogInformation("Asset {AssetId} AI processing completed", asset.Id);
                    }
                    else if (status.Status == "failed")
                    {
                        asset.AiAvailable = "notready";
                        asset.JobId = null;
                        updatedCount++;
                        _logger.LogWarning("Asset {AssetId} AI processing failed: {Error}", asset.Id, status.Error);
                    }
                    // If still processing, leave as is
                }
                catch (SiemensApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to check status for asset {AssetId}", asset.Id);
                }
            }

            if (updatedCount > 0)
            {
                await context.SaveChangesAsync();
            }

            return updatedCount;
        }

        public async Task<IEnumerable<Asset>> GetAssetsWithAiStatusAsync(string status)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.Assets
                .Where(a => a.AiAvailable == status)
                .OrderBy(a => a.Filename)
                .ToListAsync();
        }

        public async Task<IEnumerable<Asset>> GetAssetsPendingAiProcessingAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.Assets
                .Where(a => a.AiAvailable == "process" && !string.IsNullOrEmpty(a.JobId))
                .OrderBy(a => a.Filename)
                .ToListAsync();
        }

        #endregion
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