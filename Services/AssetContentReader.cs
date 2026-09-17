using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Reads the bytes of an asset the repository stores itself, for server-side consumers such
    /// as AI document ingestion.
    ///
    /// Those consumers used to download the asset's URL. That URL is built for clients from
    /// S3Settings:PublicEndpoint, so from inside the API container it can point at an unreachable
    /// host (localhost), and it is unsigned, so a private bucket answers 403 even when the host
    /// resolves. Reading through the storage service uses the same authenticated, internal
    /// connection as uploads and works however the public endpoint is configured.
    /// </summary>
    public interface IAssetContentReader
    {
        /// <summary>
        /// Opens the stored file of <paramref name="asset"/> in the current tenant's storage.
        /// Returns null for a reference-only asset, which stores no file and whose URL is the only
        /// source of its content.
        /// </summary>
        Task<Stream?> OpenStoredContentAsync(Asset asset);
    }

    public sealed class AssetContentReader : IAssetContentReader
    {
        private readonly IStorageService _storageService;
        private readonly IXR50TenantService _tenantService;
        private readonly ILogger<AssetContentReader> _logger;

        public AssetContentReader(
            IStorageService storageService,
            IXR50TenantService tenantService,
            ILogger<AssetContentReader> logger)
        {
            _storageService = storageService;
            _tenantService = tenantService;
            _logger = logger;
        }

        public async Task<Stream?> OpenStoredContentAsync(Asset asset)
        {
            if (string.IsNullOrEmpty(asset.ResolvedStorageKey))
            {
                return null;
            }

            var tenantName = _tenantService.GetCurrentTenant();

            // Uploads record a storage key. Rows written before storage keys existed have none but
            // still keep their file under the filename, so only an asset with no key AND no file
            // under its filename is reference-only.
            if (string.IsNullOrEmpty(asset.StorageKey)
                && !await _storageService.FileExistsAsync(tenantName, asset.ResolvedStorageKey))
            {
                _logger.LogDebug("Asset {AssetId} has no stored file in tenant {TenantName}; treating it as reference-only",
                    asset.Id, tenantName);
                return null;
            }

            _logger.LogDebug("Reading stored file of asset {AssetId} from tenant {TenantName} storage",
                asset.Id, tenantName);
            return await _storageService.DownloadFileAsync(tenantName, asset.ResolvedStorageKey);
        }
    }
}
