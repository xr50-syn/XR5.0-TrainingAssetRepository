using Amazon.S3;
using Amazon.S3.Model;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services
{
    public class S3StorageServiceImplementation : IStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IConfiguration _configuration;
        private readonly ILogger<S3StorageServiceImplementation> _logger;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly string _baseBucketPrefix;

        public S3StorageServiceImplementation(
            IAmazonS3 s3Client,
            IConfiguration configuration,
            ILogger<S3StorageServiceImplementation> logger,
            IXR50TenantManagementService tenantManagementService)
        {
            _s3Client = s3Client;
            _configuration = configuration;
            _logger = logger;
            _tenantManagementService = tenantManagementService;
            _baseBucketPrefix = _configuration.GetValue<string>("S3Settings:BaseBucketPrefix") ?? "xr50";
        }

        public string GetStorageType() => "S3";

        private async Task<string> GetTenantBucketName(string tenantName, XR50Tenant? tenant = null){
            if (tenant == null)
            {
                _logger.LogInformation("DEBUG: Fetching tenant info for: {TenantName}", tenantName);
                tenant = await _tenantManagementService.GetTenantAsync(tenantName);

                // Comprehensive debug logging
                if (tenant == null)
                {
                    _logger.LogError(" DEBUG: GetTenantAsync returned NULL for tenant: {TenantName}", tenantName);
                }
                else
                {
                    _logger.LogInformation("DEBUG: Retrieved tenant successfully");
                    _logger.LogInformation("   - TenantName: '{TenantName}'", tenant.TenantName ?? "NULL");
                    _logger.LogInformation("   - StorageType: '{StorageType}'", tenant.StorageType ?? "NULL");
                    _logger.LogInformation("   - S3BucketName: '{S3BucketName}'", tenant.S3BucketName ?? "NULL");
                    _logger.LogInformation("   - S3BucketRegion: '{S3BucketRegion}'", tenant.S3BucketRegion ?? "NULL");
                    _logger.LogInformation("   - TenantDirectory: '{TenantDirectory}'", tenant.TenantDirectory ?? "NULL");
                    _logger.LogInformation("   - StorageEndpoint: '{StorageEndpoint}'", tenant.StorageEndpoint ?? "NULL");

                    // Check the boolean helper methods
                    _logger.LogInformation("   - IsS3Storage(): {IsS3}", tenant.IsS3Storage());
                    _logger.LogInformation("   - IsOwnCloudStorage(): {IsOwnCloud}", tenant.IsOwnCloudStorage());
                }
            }
            // If tenant has a specific S3 bucket configured, use that
            if (tenant != null && !string.IsNullOrEmpty(tenant.S3BucketName))
            {
                return tenant.S3BucketName;
            }

            // Otherwise, use the auto-generated naming convention
            var sanitized = Regex.Replace(tenantName.ToLowerInvariant(), @"[^a-z0-9\-\.]", "-");
            return $"{_baseBucketPrefix}-tenant-{sanitized}";
        }

        public async Task<bool> CreateTenantStorageAsync(string tenantName, XR50Tenant tenant)
        {
            try
            {
                _logger.LogInformation("Verifying S3 storage for tenant: {TenantName}", tenantName);

                var bucketName = await GetTenantBucketName(tenantName, tenant);

                // Check if bucket exists (pre-provisioning required)
                var bucketExists = await DoesBucketExistAsync(bucketName);
                if (bucketExists)
                {
                    _logger.LogInformation("S3 bucket exists and is ready: {BucketName}", bucketName);
                    return true;
                }

                // Bucket does not exist - this is an error since we require pre-provisioning
                _logger.LogError(
                    "S3 bucket does NOT exist: {BucketName}. " +
                    "Buckets must be pre-provisioned. The application does not create buckets automatically. " +
                    "Please create the bucket manually and ensure the application has access to it.",
                    bucketName);

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify S3 storage for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        public async Task<bool> DeleteTenantStorageAsync(string tenantName)
        {
            try
            {
                _logger.LogWarning("Deleting S3 storage for tenant: {TenantName}", tenantName);

                var bucketName = await GetTenantBucketName(tenantName);

                // Check if bucket exists before trying to delete
                var bucketExists = await DoesBucketExistAsync(bucketName);
                if (!bucketExists)
                {
                    _logger.LogInformation("S3 bucket does not exist: {BucketName}", bucketName);
                    return true; // Consider it successful if bucket doesn't exist
                }

                // First, delete all objects in the bucket
                await DeleteAllObjectsInBucketAsync(bucketName);

                // Then delete the bucket
                var deleteRequest = new DeleteBucketRequest
                {
                    BucketName = bucketName
                };

                var response = await _s3Client.DeleteBucketAsync(deleteRequest);

                _logger.LogInformation("Deleted S3 bucket: {BucketName}", bucketName);
                return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete S3 storage for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        public async Task<bool> TenantStorageExistsAsync(string tenantName)
        {
            try
            {
                var bucketName = await GetTenantBucketName(tenantName);
                return await DoesBucketExistAsync(bucketName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if tenant storage exists: {TenantName}", tenantName);
                return false;
            }
        }
        public async Task<string> UploadFileAsync(string tenantName, string fileName, IFormFile file)
        {
            try
            {
                var bucketName = await GetTenantBucketName(tenantName);
                var key = fileName;
                
                // Create clean byte array directly from IFormFile
                byte[] fileBytes;
                using (var sourceStream = file.OpenReadStream())
                {
                    using var ms = new MemoryStream();
                    await sourceStream.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                }
                
                // Create fresh MemoryStream for upload
                using var uploadStream = new MemoryStream(fileBytes);
                
                _logger.LogInformation("Uploading file to S3: {BucketName}/{Key}, Size: {Size} bytes", 
                    bucketName, key, fileBytes.Length);
                
                var request = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    InputStream = uploadStream,
                    ContentType = file.ContentType ?? "application/octet-stream"
                };
                  
                var response = await _s3Client.PutObjectAsync(request);
                
                var url = $"s3://{bucketName}/{key}";
                _logger.LogInformation("Successfully uploaded file to S3: {Url}", url);
                return url;  
            }
            catch (AmazonS3Exception s3Ex)
            {
                _logger.LogError("S3 Exception Details:");
                _logger.LogError("  Error Code: {ErrorCode}", s3Ex.ErrorCode);
                _logger.LogError("  Error Message: {ErrorMessage}", s3Ex.Message);
                _logger.LogError("  Status Code: {StatusCode}", s3Ex.StatusCode);
                _logger.LogError("  Request ID: {RequestId}", s3Ex.RequestId);
                _logger.LogError("  Error Type: {ErrorType}", s3Ex.ErrorType);
                _logger.LogError("  Response Body: {ResponseBody}", s3Ex.ResponseBody);
                throw; 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file to S3: {FileName}", fileName);
                throw; 
            }
        } 
        public async Task<Stream> DownloadFileAsync(string tenantName, string fileName)
        {
            try
            {
                var bucketName = await GetTenantBucketName(tenantName);
                var key = fileName;

                var request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = key
                };

                var response = await _s3Client.GetObjectAsync(request);
                return response.ResponseStream;
            }
            catch (AmazonS3Exception s3Ex)
            {
                // Capture detailed S3 error information
                _logger.LogError("S3 Exception Details:");
                _logger.LogError("  Error Code: {ErrorCode}", s3Ex.ErrorCode);
                _logger.LogError("  Error Message: {ErrorMessage}", s3Ex.Message);
                _logger.LogError("  Status Code: {StatusCode}", s3Ex.StatusCode);
                _logger.LogError("  Request ID: {RequestId}", s3Ex.RequestId);
                _logger.LogError("  Error Type: {ErrorType}", s3Ex.ErrorType);
                _logger.LogError("  Response Body: {ResponseBody}", s3Ex.ResponseBody);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download file from S3: {TenantName}/{FileName}", tenantName, fileName);
                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner Exception: {Message} - {StackTrace}", ex.InnerException.Message, ex.InnerException.StackTrace);

                }
                throw;

            }
        }

        public async Task<string> GetDownloadUrlAsync(string tenantName, string fileName, TimeSpan? expiration = null)
        {
            try
            {
                var bucketName = await  GetTenantBucketName(tenantName);
                var key = fileName;
                var expires = expiration ?? TimeSpan.FromHours(1);

                var request = new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.Add(expires)
                };

                var url = await _s3Client.GetPreSignedURLAsync(request);
                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate download URL for S3: {TenantName}/{FileName}", tenantName, fileName);
                throw;
            }
        }

        public async Task<bool> DeleteFileAsync(string tenantName, string fileName)
        {
            try
            {
                var bucketName = await  GetTenantBucketName(tenantName);
                var key = fileName;

                var request = new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = key
                };

                var response = await _s3Client.DeleteObjectAsync(request);
                return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file from S3: {TenantName}/{FileName}", tenantName, fileName);
                return false;
            }
        }

        public async Task<bool> FileExistsAsync(string tenantName, string fileName)
        {
            try
            {
                var bucketName = await  GetTenantBucketName(tenantName);
                var key = fileName;

                var request = new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = key
                };

                await _s3Client.GetObjectMetadataAsync(request);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if file exists in S3: {TenantName}/{FileName}", tenantName, fileName);
                return false;
            }
        }

        public async Task<long> GetFileSizeAsync(string tenantName, string fileName)
        {
            try
            {
                var bucketName = await  GetTenantBucketName(tenantName);
                var key = fileName;

                var request = new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = key
                };

                var response = await _s3Client.GetObjectMetadataAsync(request);
                return response.ContentLength;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get file size from S3: {TenantName}/{FileName}", tenantName, fileName);
                return 0;
            }
        }

        public async Task<StorageStatistics> GetStorageStatisticsAsync(string tenantName)
        {
            try
            {
                var bucketName = await  GetTenantBucketName(tenantName);
                var request = new ListObjectsV2Request { BucketName = bucketName };

                long totalFiles = 0;
                long totalSize = 0;

                ListObjectsV2Response response;
                do
                {
                    response = await _s3Client.ListObjectsV2Async(request);
                    totalFiles += response.S3Objects?.Count ?? 0;
                    totalSize += response.S3Objects?.Sum(obj => obj.Size) ?? 0;
                    request.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated == true);

                return new StorageStatistics
                {
                    TenantName = tenantName,
                    StorageType = "S3",
                    TotalFiles = totalFiles,
                    TotalSizeBytes = totalSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get storage statistics for tenant: {TenantName}", tenantName);
                return new StorageStatistics { TenantName = tenantName, StorageType = "S3" };
            }
        }

        #region Private Helper Methods

        private async Task<bool> DoesBucketExistAsync(string bucketName)
        {
            try
            {
                await _s3Client.GetBucketLocationAsync(bucketName);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if bucket exists: {BucketName}", bucketName);
                return false;
            }
        }

        private async Task DeleteAllObjectsInBucketAsync(string bucketName)
        {
            try
            {
                var request = new ListObjectsV2Request { BucketName = bucketName };
                ListObjectsV2Response response;

                do
                {
                    response = await _s3Client.ListObjectsV2Async(request);
                    if (response.S3Objects.Count > 0)
                    {
                        var deleteRequest = new DeleteObjectsRequest
                        {
                            BucketName = bucketName,
                            Objects = response.S3Objects.Select(obj => new KeyVersion { Key = obj.Key }).ToList()
                        };

                        await _s3Client.DeleteObjectsAsync(deleteRequest);
                    }
                    request.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated == true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete all objects in bucket: {BucketName}", bucketName);
                throw;
            }
        }

        #endregion
        
        public bool SupportsSharing() => false;

        public async Task<string> CreateShareAsync(string tenantName, XR50Tenant tenant, Asset asset)
        {
            throw new NotSupportedException("S3 storage does not support OwnCloud-style sharing");
        }

        public async Task<bool> DeleteShareAsync(string tenantName, string shareId)
        {
            throw new NotSupportedException("S3 storage does not support OwnCloud-style sharing");
        }
    }

}