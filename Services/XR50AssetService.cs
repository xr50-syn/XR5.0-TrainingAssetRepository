using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Materials;
using System.Diagnostics;
using System.Security.Cryptography;

namespace XR50TrainingAssetRepo.Services
{
    public interface IAssetService
    {
        // Basic Asset Operations
        Task<IEnumerable<Asset>> GetAllAssetsAsync();
        Task<Asset?> GetAssetAsync(int id);
        Task<Asset> CreateAssetReference(string tenantName, AssetReferenceData assetRefData);
        Task<Asset> CreateAssetAsync(Asset asset, string tenantName, IFormFile file);
        Task<AssetCreationResult> CreateAssetWithResultAsync(Asset asset, string tenantName, IFormFile file);
        Task<Asset> UpdateAssetAsync(Asset asset);

        /// <summary>
        /// Deletes an asset with dependency awareness. When the asset is referenced by any
        /// Training Material and <paramref name="force"/> is false, nothing is deleted and the
        /// returned result is <see cref="AssetDeletionResult.Blocked"/> with the dependency list.
        /// When forced (or when there are no dependents), the asset is removed together with its
        /// single-asset materials, detached from any AI Assistant / Innov chatbot (deleting those
        /// only when it was their last asset), and its DataLens documents/collections are cleaned up.
        /// </summary>
        Task<AssetDeletionResult> DeleteAssetAsync(string tenantName, int id, bool force = false);

        /// <summary>
        /// Deletes just this asset's storage file and DB row, with no dependency check or cascade.
        /// Used by internal rollback/replace flows that manage material references themselves.
        /// </summary>
        Task<bool> DeleteAssetRecordAsync(string tenantName, int id);

        /// <summary>
        /// Lists every Training Material that references the asset (single-asset column types plus
        /// AI Assistant / Innov chatbot JSON arrays), with whether each would be deleted on a forced delete.
        /// </summary>
        Task<List<AssetDependencyDto>> GetAssetDependenciesAsync(int assetId);

        Task<bool> AssetExistsAsync(int id);
        
        // Asset Search and Filtering
        Task<IEnumerable<Asset>> GetAssetsByFiletypeAsync(string filetype);
        Task<IEnumerable<Asset>> SearchAssetsByFilenameAsync(string searchTerm);
        Task<IEnumerable<Asset>> GetAssetsByDescriptionAsync(string searchTerm);
        
        // Asset Relationships
        Task<IEnumerable<Material>> GetMaterialsUsingAssetAsync(int assetId);
        Task<int> GetAssetUsageCountAsync(int assetId);
        
        // File Management with Storage Service
        Task<string> GetAssetDownloadUrlAsync(string tenantName, int assetId);
        Task<Asset> UploadAssetAsync(IFormFile file, string tenantName, string filename, string? description = null);
        Task<Asset> UploadAssetToExistingAsync(int assetId, IFormFile file, string tenantName, string? filename = null, string? description = null);
        Task<long> GetAssetFileSizeAsync(string tenantName, int assetId);
        Task<bool> AssetFileExistsAsync(string tenantName, int assetId);
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
        private readonly IMaterialServiceBase _materialServiceBase;
        private readonly IAIAssistantMaterialService _aiAssistantMaterialService;
        private readonly IInnovChatbotMaterialService _innovChatbotMaterialService;
        private readonly IXR50TenantService _tenantService;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IStorageService _storageService; // Unified storage interface
        private readonly IChatbotApiService _chatbotApiService;
        private readonly ILogger<AssetService> _logger;

        public AssetService(
            IConfiguration configuration,
            IXR50TenantDbContextFactory dbContextFactory,
            IMaterialServiceBase materialServiceBase,
            IAIAssistantMaterialService aiAssistantMaterialService,
            IInnovChatbotMaterialService innovChatbotMaterialService,
            IXR50TenantService tenantService,
            IXR50TenantManagementService tenantManagementService,
            IStorageService storageService,
            IChatbotApiService chatbotApiService,
            ILogger<AssetService> logger)
        {
            _configuration = configuration;
            _dbContextFactory = dbContextFactory;
            _materialServiceBase = materialServiceBase;
            _aiAssistantMaterialService = aiAssistantMaterialService;
            _innovChatbotMaterialService = innovChatbotMaterialService;
            _tenantService = tenantService;
            _tenantManagementService = tenantManagementService;
            _storageService = storageService;
            _chatbotApiService = chatbotApiService;
            _logger = logger;
        }

        // Materials referencing an asset, split by deletion semantics.
        private sealed class AssetDependencies
        {
            public List<Material> SingleAsset { get; } = new();
            public List<AIAssistantMaterial> AiAssistants { get; } = new();
            public List<InnovChatbotMaterial> InnovChatbots { get; } = new();
            public bool Any => SingleAsset.Count > 0 || AiAssistants.Count > 0 || InnovChatbots.Count > 0;
        }

        // Resolve the current tenant's per-tenant default DataLens collection. Used as the
        // fallback when an asset isn't owned by an AIAssistantMaterial. See
        // XR50Tenant.DefaultAICollection for why this must not be a global value.
        private async Task<string> GetTenantDefaultCollectionAsync()
        {
            var tenantName = _tenantService.GetCurrentTenant();
            var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
            if (string.IsNullOrEmpty(tenant?.DefaultAICollection))
            {
                throw new InvalidOperationException(
                    $"Tenant '{tenantName}' has no DefaultAICollection configured; cannot resolve a default collection for asset AI status sync");
            }
            return tenant.DefaultAICollection;
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
            var result = await CreateAssetWithResultAsync(asset, tenantName, file);
            return result.Asset;
        }

        public async Task<AssetCreationResult> CreateAssetWithResultAsync(Asset asset, string tenantName, IFormFile file)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            string? uploadedFilePath = null; // Track for cleanup on failure

            try
            {
                // Detect file type from binary stream (magic bytes)
                using var stream = file.OpenReadStream();
                var (detectedFiletype, detectedType) = await DetectFileTypeFromStream(stream);
                asset.ContentHash = await ComputeContentHashAsync(stream);

                var existingAsset = await context.Assets
                    .AsNoTracking()
                    .SingleOrDefaultAsync(existing => existing.ContentHash == asset.ContentHash);
                if (existingAsset != null)
                {
                    await transaction.RollbackAsync();
                    _logger.LogInformation(
                        "Reusing asset {AssetId} for duplicate upload {Filename} in tenant {TenantName}",
                        existingAsset.Id, asset.Filename, tenantName);
                    return new AssetCreationResult(existingAsset, false);
                }

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

                // Store under the content hash so this row owns the only reference to the object and
                // a same-named upload cannot overwrite another asset. Recording the key rather than
                // deriving it keeps the file addressable even if the asset is renamed later.
                asset.StorageKey = asset.ContentHash;

                stream.Seek(0, SeekOrigin.Begin); // Reset stream position after detection
                var uploadUrl = await _storageService.UploadFileAsync(tenantName, asset.ResolvedStorageKey, file, asset.Filename);
                uploadedFilePath = asset.ResolvedStorageKey; // Mark for potential cleanup

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

                await transaction.CommitAsync();
                uploadedFilePath = null; // Success - don't cleanup

                return new AssetCreationResult(asset, true);
            }
            catch (DbUpdateException ex) when (!string.IsNullOrEmpty(asset.ContentHash))
            {
                await transaction.RollbackAsync();

                // A concurrent request may have inserted the same hash after our initial lookup.
                // Resolve that race through the tenant-local unique index and return its winner.
                using var duplicateContext = _dbContextFactory.CreateDbContext();
                var existingAsset = await duplicateContext.Assets
                    .AsNoTracking()
                    .SingleOrDefaultAsync(existing => existing.ContentHash == asset.ContentHash);

                if (existingAsset == null)
                {
                    await CleanupUploadedFileAsync(tenantName, uploadedFilePath);
                    _logger.LogError(ex,
                        "Failed to persist asset {Filename} for tenant {TenantName}",
                        asset.Filename, tenantName);
                    throw;
                }

                // The race winner has the same content, so under content addressing it resolves to
                // the same storage key: the loser's upload wrote the winner's object, byte for byte.
                // Cleaning it up would delete the file the winner now depends on.
                if (!string.Equals(uploadedFilePath, existingAsset.ResolvedStorageKey, StringComparison.Ordinal))
                {
                    await CleanupUploadedFileAsync(tenantName, uploadedFilePath);
                }

                _logger.LogInformation(
                    "Reusing concurrently created asset {AssetId} for duplicate upload {Filename} in tenant {TenantName}",
                    existingAsset.Id, asset.Filename, tenantName);
                return new AssetCreationResult(existingAsset, false);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // Compensating action: cleanup orphaned storage file
                await CleanupUploadedFileAsync(tenantName, uploadedFilePath);

                _logger.LogError(ex, "Failed to create asset {Filename} for tenant {TenantName} - Transaction rolled back",
                    asset.Filename, tenantName);
                throw;
            }
        }

        private static async Task<string> ComputeContentHashAsync(Stream stream)
        {
            var originalPosition = stream.Position;
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                var hash = await SHA256.HashDataAsync(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            finally
            {
                stream.Seek(originalPosition, SeekOrigin.Begin);
            }
        }

        private async Task CleanupUploadedFileAsync(string tenantName, string? uploadedFilePath)
        {
            if (uploadedFilePath == null)
            {
                return;
            }

            try
            {
                await _storageService.DeleteFileAsync(tenantName, uploadedFilePath);
                _logger.LogInformation("Cleaned up orphaned file {Filename} after failed asset creation", uploadedFilePath);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to cleanup orphaned file {Filename} after failed asset creation", uploadedFilePath);
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

                // ContentHash and StorageKey are internal and ignored by JSON model binding. Preserve
                // them across metadata PUT requests so updating a description cannot disable
                // deduplication, and renaming an asset cannot move it off the file it already owns.
                asset.ContentHash = existing.ContentHash;
                asset.StorageKey = existing.StorageKey;

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

        public async Task<List<AssetDependencyDto>> GetAssetDependenciesAsync(int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var deps = await ResolveDependenciesAsync(context, assetId);
            return ToDependencyDtos(deps);
        }

        public async Task<AssetDeletionResult> DeleteAssetAsync(string tenantName, int id, bool force = false)
        {
            var result = new AssetDeletionResult();

            Asset asset;
            AssetDependencies deps;
            var documentTargets = new List<(string Collection, string Document)>();
            var collectionTargets = new List<string>();

            // Resolve everything (asset, dependents, DataLens cleanup targets) up front while we
            // hold a context and the per-(material,asset) job rows are still present.
            using (var context = _dbContextFactory.CreateDbContext())
            {
                var loaded = await context.Assets.FindAsync(id);
                if (loaded == null)
                {
                    result.NotFound = true;
                    return result;
                }
                asset = loaded;

                deps = await ResolveDependenciesAsync(context, id);

                if (deps.Any && !force)
                {
                    // Asset is in use and the caller has not confirmed a forced delete.
                    result.Blocked = true;
                    result.Dependencies = ToDependencyDtos(deps);
                    _logger.LogInformation("Delete of asset {AssetId} blocked: {Count} dependent material(s)",
                        id, result.Dependencies.Count);
                    return result;
                }

                // AI Assistant owners: drop the whole collection if this was the last asset,
                // otherwise drop just this asset's document from the assistant's collection.
                foreach (var owner in deps.AiAssistants)
                {
                    var job = await context.AIAssistantMaterialAssetJobs
                        .FirstOrDefaultAsync(j => j.AIAssistantMaterialId == owner.id && j.AssetId == id);
                    var collection = !string.IsNullOrEmpty(job?.CollectionName) ? job!.CollectionName : owner.CollectionName;
                    if (string.IsNullOrEmpty(collection))
                    {
                        continue;
                    }

                    if (owner.GetAssetIdsList().Count <= 1)
                    {
                        collectionTargets.Add(collection);
                    }
                    else
                    {
                        var docName = !string.IsNullOrEmpty(job?.DocumentName) ? job!.DocumentName! : SafeDocumentName(asset);
                        if (!string.IsNullOrEmpty(docName))
                        {
                            documentTargets.Add((collection, docName));
                        }
                    }
                }

                // Standalone AI-processed PDF (not owned by any AI Assistant): remove it from the
                // tenant's default DataLens bucket.
                if (deps.AiAssistants.Count == 0 && !string.IsNullOrEmpty(asset.AiAvailable) && asset.AiAvailable != "notready")
                {
                    try
                    {
                        var defaultCollection = await GetTenantDefaultCollectionAsync();
                        var docName = SafeDocumentName(asset);
                        if (!string.IsNullOrEmpty(docName))
                        {
                            documentTargets.Add((defaultCollection, docName));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not resolve tenant default collection for standalone asset {AssetId}; skipping DataLens document cleanup", id);
                    }
                }
            }

            // --- Cascade material deletions (each service manages its own context/transaction) ---

            // Single-asset materials (Image/Video/PDF/Unity/Default) are deleted outright.
            foreach (var material in deps.SingleAsset)
            {
                try
                {
                    if (await _materialServiceBase.DeleteAsync(material.id))
                    {
                        result.DeletedMaterialIds.Add(material.id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete dependent material {MaterialId} while deleting asset {AssetId}", material.id, id);
                }
            }

            // AI Assistant materials: detach the asset; delete the material only when it was its last asset.
            foreach (var owner in deps.AiAssistants)
            {
                try
                {
                    if (owner.GetAssetIdsList().Count <= 1)
                    {
                        if (await _aiAssistantMaterialService.DeleteAsync(owner.id))
                        {
                            result.DeletedMaterialIds.Add(owner.id);
                        }
                    }
                    else
                    {
                        await _aiAssistantMaterialService.RemoveAssetAsync(owner.id, id);
                        // Drop the stale per-(material, asset) job row so the material's aggregate
                        // status is not computed from a job whose asset no longer belongs to it.
                        await RemoveAssetJobRowsAsync(owner.id, id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update AI Assistant material {MaterialId} while deleting asset {AssetId}", owner.id, id);
                }
            }

            // Innov chatbot materials mirror AI Assistant lifecycle (INNOV backend has no DataLens bucket).
            foreach (var owner in deps.InnovChatbots)
            {
                try
                {
                    if (owner.GetAssetIdsList().Count <= 1)
                    {
                        if (await _innovChatbotMaterialService.DeleteAsync(owner.id))
                        {
                            result.DeletedMaterialIds.Add(owner.id);
                        }
                    }
                    else
                    {
                        await _innovChatbotMaterialService.RemoveAssetAsync(owner.id, id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update Innov chatbot material {MaterialId} while deleting asset {AssetId}", owner.id, id);
                }
            }

            // --- DataLens cleanup (best-effort; failures are logged, never thrown) ---
            foreach (var (collection, document) in documentTargets)
            {
                if (await _chatbotApiService.DeleteDocumentAsync(collection, document))
                {
                    result.DataLensDocumentsRemoved.Add($"{collection}/{document}");
                }
            }
            foreach (var collection in collectionTargets.Distinct())
            {
                if (await _chatbotApiService.DeleteCollectionAsync(collection, force: true))
                {
                    result.DataLensCollectionsRemoved.Add(collection);
                }
            }

            // --- Finally remove the asset's storage file and DB row ---
            result.Deleted = await RemoveAssetStorageAndRowAsync(tenantName, id);
            return result;
        }

        public Task<bool> DeleteAssetRecordAsync(string tenantName, int id)
        {
            return RemoveAssetStorageAndRowAsync(tenantName, id);
        }

        // Deletes an asset's storage file (best-effort) and its DB row. No dependency handling.
        private async Task<bool> RemoveAssetStorageAndRowAsync(string tenantName, int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var asset = await context.Assets.FindAsync(id);
            if (asset == null)
            {
                return false;
            }

            try
            {
                // Delete file from storage. The key is content-addressed, and the unique index on
                // ContentHash means no other row shares it, so this cannot remove a file another
                // asset still points at.
                var storageDeleted = await _storageService.DeleteFileAsync(tenantName, asset.ResolvedStorageKey);
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

        // Resolves every material that references an asset, split by how it is deleted:
        // single-asset column types (deleted outright) vs. AI Assistant / Innov JSON arrays
        // (asset detached; material deleted only when it was the last asset).
        private async Task<AssetDependencies> ResolveDependenciesAsync(XR50TrainingContext context, int assetId)
        {
            var deps = new AssetDependencies();

            deps.SingleAsset.AddRange(await _materialServiceBase.GetByAssetIdAsync(assetId));

            deps.AiAssistants.AddRange(
                (await context.Materials.OfType<AIAssistantMaterial>().ToListAsync())
                .Where(m => m.GetAssetIdsList().Contains(assetId)));

            deps.InnovChatbots.AddRange(
                (await context.Materials.OfType<InnovChatbotMaterial>().ToListAsync())
                .Where(m => m.GetAssetIdsList().Contains(assetId)));

            return deps;
        }

        private static List<AssetDependencyDto> ToDependencyDtos(AssetDependencies deps)
        {
            var list = new List<AssetDependencyDto>();

            foreach (var m in deps.SingleAsset)
            {
                list.Add(new AssetDependencyDto { Id = m.id, Name = m.Name, Type = m.Type.ToString(), WillBeDeleted = true });
            }
            foreach (var m in deps.AiAssistants)
            {
                list.Add(new AssetDependencyDto { Id = m.id, Name = m.Name, Type = m.Type.ToString(), WillBeDeleted = m.GetAssetIdsList().Count <= 1 });
            }
            foreach (var m in deps.InnovChatbots)
            {
                list.Add(new AssetDependencyDto { Id = m.id, Name = m.Name, Type = m.Type.ToString(), WillBeDeleted = m.GetAssetIdsList().Count <= 1 });
            }

            return list;
        }

        // Removes the per-(material, asset) job rows for an asset detached from an AI Assistant.
        // (When the material itself is deleted, its job rows are removed by DB cascade instead.)
        private async Task RemoveAssetJobRowsAsync(int aiAssistantMaterialId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var jobs = await context.AIAssistantMaterialAssetJobs
                .Where(j => j.AIAssistantMaterialId == aiAssistantMaterialId && j.AssetId == assetId)
                .ToListAsync();
            if (jobs.Count > 0)
            {
                context.AIAssistantMaterialAssetJobs.RemoveRange(jobs);
                await context.SaveChangesAsync();
            }
        }

        // Derives the DataLens document name for an asset, matching what was used at submit time.
        // Submission files documents under the asset's filename, so cleanup resolves the same way -
        // deriving from the URL would look for the content hash instead. Job rows carry the name
        // actually used and take precedence over this wherever one exists.
        private string? SafeDocumentName(Asset asset)
        {
            if (string.IsNullOrEmpty(asset.Filename))
            {
                return null;
            }
            try
            {
                return _chatbotApiService.GetDocumentName(asset.Filename, asset.Filetype ?? "pdf");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not derive DataLens document name for asset {AssetId} from filename {Filename}; using it verbatim",
                    asset.Id, asset.Filename);
                return asset.Filename;
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

            return await _materialServiceBase.GetByAssetIdAsync(assetId);
        }

        public async Task<int> GetAssetUsageCountAsync(int assetId)
        {
            var materials = await GetMaterialsUsingAssetAsync(assetId);
            return materials.Count();
        }

        public async Task<string> GetAssetDownloadUrlAsync(string tenantName, int assetId)
        {
            var asset = await GetAssetAsync(assetId);
            if (asset == null)
            {
                throw new ArgumentException($"Asset with ID {assetId} not found");
            }

            try
            {
                var downloadUrl = await _storageService.GetDownloadUrlAsync(tenantName, asset.ResolvedStorageKey);

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

                var asset = new Asset
                {
                    Filename = filename,
                    Description = description
                };

                // Delegate to CreateAssetAsync to handle detection, upload, and persistence once.
                return await CreateAssetAsync(asset, tenantName, file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload asset {Filename} to tenant {TenantName}", filename, tenantName);
                throw;
            }
        }

        public async Task<Asset> UploadAssetToExistingAsync(int assetId, IFormFile file, string tenantName, string? filename = null, string? description = null)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            string? uploadedFilePath = null; // Track for cleanup on failure

            try
            {
                var existing = await context.Assets.FindAsync(assetId);
                if (existing == null)
                {
                    throw new KeyNotFoundException($"Asset {assetId} not found");
                }

                if (!string.IsNullOrEmpty(existing.URL) || !string.IsNullOrEmpty(existing.Src))
                {
                    throw new InvalidOperationException($"Asset {assetId} already has a file attached");
                }

                var resolvedFilename = !string.IsNullOrEmpty(filename)
                    ? filename
                    : (!string.IsNullOrEmpty(existing.Filename) ? existing.Filename : file.FileName);

                // Detect file type from binary stream (magic bytes)
                using var stream = file.OpenReadStream();
                var (detectedFiletype, detectedType) = await DetectFileTypeFromStream(stream);

                // Attaching a file is a content-producing operation just like creating an asset, so
                // it has to hash too. Without this the row keeps a null hash, stays invisible to
                // deduplication, and lands on a filename-keyed storage path.
                existing.ContentHash = await ComputeContentHashAsync(stream);

                var duplicateAsset = await context.Assets
                    .AsNoTracking()
                    .SingleOrDefaultAsync(other => other.ContentHash == existing.ContentHash && other.Id != assetId);
                if (duplicateAsset != null)
                {
                    throw new DuplicateAssetContentException(
                        $"This file is already stored as asset {duplicateAsset.Id} ('{duplicateAsset.Filename}'). " +
                        "Attach that asset instead of uploading another copy.");
                }

                if (detectedFiletype == "unknown")
                {
                    var extensionFiletype = GetFiletypeFromFilename(resolvedFilename);
                    var extensionType = InferAssetTypeFromFiletype(extensionFiletype);
                    _logger.LogInformation("Binary detection failed, using extension-based detection for {Filename}: {Filetype} -> {Type}",
                        resolvedFilename, extensionFiletype, extensionType);
                    detectedFiletype = extensionFiletype;
                    detectedType = extensionType;
                }

                existing.StorageKey = existing.ContentHash;

                stream.Seek(0, SeekOrigin.Begin);
                var uploadUrl = await _storageService.UploadFileAsync(tenantName, existing.ResolvedStorageKey, file, resolvedFilename);
                uploadedFilePath = existing.ResolvedStorageKey; // Mark for potential cleanup

                existing.Filename = resolvedFilename;
                existing.Description = description ?? existing.Description;
                existing.Filetype = detectedFiletype;
                existing.Type = detectedType;
                existing.Src = uploadUrl;
                existing.URL = uploadUrl;

                if (_storageService.SupportsSharing())
                {
                    try
                    {
                        var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                        if (tenant != null)
                        {
                            var shareUrl = await _storageService.CreateShareAsync(tenantName, tenant, existing);
                            if (!string.IsNullOrEmpty(shareUrl))
                            {
                                existing.URL = shareUrl;
                                await CreateShareRecord(context, existing.Id.ToString(), tenant.TenantGroup ?? "");
                            }
                        }
                    }
                    catch (Exception shareEx)
                    {
                        _logger.LogWarning(shareEx, "Failed to auto-share asset {AssetId} after upload", existing.Id);
                    }
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                uploadedFilePath = null; // Success - don't cleanup

                _logger.LogInformation("Uploaded file for existing asset {AssetId} ({Filename}) to {StorageType} storage",
                    existing.Id, existing.Filename, _storageService.GetStorageType());

                return existing;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // Compensating action: cleanup orphaned storage file
                if (uploadedFilePath != null)
                {
                    try
                    {
                        await _storageService.DeleteFileAsync(tenantName, uploadedFilePath);
                        _logger.LogInformation("Cleaned up orphaned file {Filename} after failed upload to existing asset", uploadedFilePath);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to cleanup orphaned file {Filename} after failed upload to existing asset", uploadedFilePath);
                    }
                }

                _logger.LogError(ex, "Failed to upload file to existing asset {AssetId} - Transaction rolled back", assetId);
                throw;
            }
        }

        public async Task<long> GetAssetFileSizeAsync(string tenantName, int assetId)
        {
            var asset = await GetAssetAsync(assetId);
            if (asset == null)
            {
                return 0;
            }

            try
            {
                var size = await _storageService.GetFileSizeAsync(tenantName, asset.ResolvedStorageKey);

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

        public async Task<bool> AssetFileExistsAsync(string tenantName, int assetId)
        {
            var asset = await GetAssetAsync(assetId);
            if (asset == null)
            {
                return false;
            }

            try
            {
                var exists = await _storageService.FileExistsAsync(tenantName, asset.ResolvedStorageKey);

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
                // Resolve collection name: check if asset belongs to an AIAssistantMaterial
                var collectionName = await ResolveCollectionNameForAssetAsync(context, assetId);

                // The normal Chat path submits straight to the tenant's DefaultAICollection, which
                // is created lazily — the AIAssistantMaterial flow owns provisioning for its own
                // collections, but a tenant that has never created one has no DataLens collection
                // yet, so this submit would 502 on a cold gateway. Ensure it exists first.
                await _chatbotApiService.EnsureCollectionExistsAsync(collectionName);

                var jobId = await _chatbotApiService.SubmitDocumentAsync(
                    assetId, asset.URL, asset.Filetype ?? "pdf", collectionName, asset.Filename);
                asset.JobId = jobId;
                asset.AiAvailable = "process";

                _logger.LogInformation("Submitted asset {AssetId} to collection {CollectionName} for AI processing. Job ID: {JobId}",
                    assetId, collectionName, jobId);

                await context.SaveChangesAsync();

                return asset;
            }
            catch (ChatbotApiException ex)
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

            _logger.LogInformation("Found {Count} assets with AiAvailable='process' and JobId set",
                processingAssets.Count);

            if (!processingAssets.Any())
            {
                _logger.LogInformation("No assets currently processing");
                return 0;
            }

            // Build asset-to-collection mapping
            var assetCollectionMap = await BuildAssetCollectionMapAsync(context, processingAssets.Select(a => a.Id).ToList());

            // Tenant-scoped fallback for assets not owned by any AIAssistantMaterial.
            var tenantDefaultCollection = await GetTenantDefaultCollectionAsync();

            var updatedCount = 0;

            foreach (var asset in processingAssets)
            {
                _logger.LogInformation("Checking status for Asset {AssetId} with JobId: {JobId}",
                    asset.Id, asset.JobId);

                try
                {
                    var collectionName = assetCollectionMap.GetValueOrDefault(asset.Id, tenantDefaultCollection);
                    var status = await _chatbotApiService.GetJobStatusAsync(asset.JobId!, collectionName);

                    _logger.LogInformation("Asset {AssetId} status check returned: Status='{Status}'",
                        asset.Id, status.Status);

                    if (status.Status == "completed")
                    {
                        asset.AiAvailable = "ready";
                        updatedCount++;
                        _logger.LogInformation("Asset {AssetId} AI processing completed - updating to 'ready'", asset.Id);
                    }
                    else if (status.Status == "failed")
                    {
                        asset.AiAvailable = "notready";
                        asset.JobId = null;
                        updatedCount++;
                        _logger.LogWarning("Asset {AssetId} AI processing failed: {Error}", asset.Id, status.Error);
                    }
                    else
                    {
                        _logger.LogInformation("Asset {AssetId} still processing (status: {Status})",
                            asset.Id, status.Status);
                    }
                }
                catch (ChatbotApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to check status for asset {AssetId}: {Message}",
                        asset.Id, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error checking status for asset {AssetId}", asset.Id);
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

        /// <summary>
        /// Resolves the DataLens collection name for an asset by finding its owning AIAssistantMaterial.
        /// Falls back to the current tenant's default collection if the asset is not associated with any material.
        /// </summary>
        private async Task<string> ResolveCollectionNameForAssetAsync(XR50TrainingContext context, int assetId)
        {
            var aiAssistantMaterial = await context.Materials
                .OfType<AIAssistantMaterial>()
                .Where(m => m.AIAssistantAssetIds != null && m.AIAssistantAssetIds.Contains(assetId.ToString()))
                .FirstOrDefaultAsync();

            if (aiAssistantMaterial?.CollectionName != null)
            {
                return aiAssistantMaterial.CollectionName;
            }

            return await GetTenantDefaultCollectionAsync();
        }

        /// <summary>
        /// Builds a mapping of asset IDs to their DataLens collection names.
        /// </summary>
        private async Task<Dictionary<int, string>> BuildAssetCollectionMapAsync(XR50TrainingContext context, List<int> assetIds)
        {
            var map = new Dictionary<int, string>();

            var aiAssistantMaterials = await context.Materials
                .OfType<AIAssistantMaterial>()
                .Where(m => m.AIAssistantAssetIds != null && m.CollectionName != null)
                .ToListAsync();

            foreach (var material in aiAssistantMaterials)
            {
                var materialAssetIds = material.GetAssetIdsList();
                foreach (var assetId in materialAssetIds.Where(id => assetIds.Contains(id)))
                {
                    map[assetId] = material.CollectionName!;
                }
            }

            return map;
        }

        #endregion
    }


    /// <summary>Result of resolving an upload to either a new or existing asset.</summary>
    public sealed record AssetCreationResult(Asset Asset, bool Created);

    /// <summary>
    /// Raised when a file would attach content that another asset already stores. Uploading to a new
    /// asset deduplicates instead, but attaching targets one specific asset, so there is no correct
    /// row to return and the caller has to pick the existing asset explicitly.
    /// </summary>
    public class DuplicateAssetContentException : InvalidOperationException
    {
        public DuplicateAssetContentException(string message) : base(message) { }
    }

    /// <summary>
    /// A Training Material that references an asset, surfaced to the client so it can show which
    /// materials a forced delete would affect.
    /// </summary>
    public class AssetDependencyDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// True if the material itself is deleted when the asset is deleted (single-asset types, or a
        /// multi-asset assistant whose last asset this is). False when the asset is merely detached.
        /// </summary>
        public bool WillBeDeleted { get; set; }
    }

    /// <summary>
    /// Outcome of an asset deletion attempt.
    /// </summary>
    public class AssetDeletionResult
    {
        /// <summary>The asset did not exist.</summary>
        public bool NotFound { get; set; }

        /// <summary>The asset row was deleted.</summary>
        public bool Deleted { get; set; }

        /// <summary>Deletion was refused because the asset is in use and force was not set.</summary>
        public bool Blocked { get; set; }

        /// <summary>Dependent materials (populated when Blocked).</summary>
        public List<AssetDependencyDto> Dependencies { get; set; } = new();

        /// <summary>Ids of materials deleted as part of the cascade.</summary>
        public List<int> DeletedMaterialIds { get; set; } = new();

        /// <summary>DataLens documents removed, formatted as "collection/document".</summary>
        public List<string> DataLensDocumentsRemoved { get; set; } = new();

        /// <summary>DataLens collections removed.</summary>
        public List<string> DataLensCollectionsRemoved { get; set; } = new();
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
