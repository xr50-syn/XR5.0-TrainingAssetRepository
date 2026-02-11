using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services
{
    public class OwnCloudStorageServiceImplementation : IStorageService
    {
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<OwnCloudStorageServiceImplementation> _logger;

        public OwnCloudStorageServiceImplementation(
            IXR50TenantManagementService tenantManagementService,
            IConfiguration configuration,
            HttpClient httpClient,
            ILogger<OwnCloudStorageServiceImplementation> logger)
        {
            _tenantManagementService = tenantManagementService;
            _configuration = configuration;
            _httpClient = httpClient;
            _logger = logger;
        }

        public string GetStorageType() => "OwnCloud";
        public bool SupportsSharing() => true;
        public async Task<bool> CreateTenantStorageAsync(string tenantName, XR50Tenant tenant)
        {
            try
            {
                _logger.LogInformation("Creating OwnCloud storage for tenant: {TenantName}", tenantName);

                // Create group
                var groupCreated = await CreateGroupAsync(tenant.TenantGroup);
                if (!groupCreated)
                {
                    _logger.LogError("Failed to create group for tenant: {TenantName}", tenantName);
                    return false;
                }

                // Create admin user
                if (tenant.Owner != null)
                {
                    var userCreated = await CreateUserAsync(tenant.Owner, tenant.TenantGroup);
                    if (!userCreated)
                    {
                        _logger.LogError("Failed to create user for tenant: {TenantName}", tenantName);
                        return false;
                    }

                    // Create directory
                    var dirCreated = await CreateDirectoryAsync(tenant.TenantDirectory, tenant.Owner);
                    if (!dirCreated)
                    {
                        _logger.LogError("Failed to create directory for tenant: {TenantName}", tenantName);
                        return false;
                    }
                }

                _logger.LogInformation("Successfully created OwnCloud storage for tenant: {TenantName}", tenantName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating OwnCloud storage for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        public async Task<bool> DeleteTenantStorageAsync(string tenantName)
        {
            var allowDestructiveDelete = _configuration.GetValue<bool>("TenantSettings:AllowDestructiveStorageDelete");
            if (!allowDestructiveDelete) 
            {
               return false;
            }    
            try
            {
                _logger.LogWarning("Deleting OwnCloud storage for tenant: {TenantName}", tenantName);

                // Note: This is a simplified implementation
                // In a real scenario, you'd need to get tenant details first

                // Delete directory (using curl command for simplicity)
                var tenant = await GetTenantWithOwner(tenantName);
                var tenantDirectory = tenant.TenantDirectory;
                var success = await ExecuteWebDAVAsAdmin("DELETE", $"{tenantDirectory}");

                _logger.LogInformation("Deleted OwnCloud storage for tenant: {TenantName}", tenantName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting OwnCloud storage for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        public async Task<bool> TenantStorageExistsAsync(string tenantName)
        {
            try
            {
                // Check if tenant directory exists by trying to access it
                var tenant = await GetTenantWithOwner(tenantName);
                var dirl = System.Web.HttpUtility.UrlEncode(tenant.TenantDirectory);
                return await ExecuteWebDAVAsUser("HEAD", $"{dirl}/", null, tenant.Owner);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if tenant storage exists: {TenantName}", tenantName);
                return false;
            }
        }

        // In OwnCloudStorageServiceImplementation.cs

        public async Task<string> UploadFileAsync(string tenantName, string fileName, IFormFile file)
        {
            try
            {
                _logger.LogInformation("Uploading file to OwnCloud: {TenantName}/{FileName}, Size: {Size} bytes", 
                    tenantName, fileName, file.Length);

                // Get tenant info to determine directory and credentials
                var tenant = await GetTenantWithOwner(tenantName);

                if (tenant == null || !tenant.IsOwnCloudStorage())
                {
                    throw new InvalidOperationException($"Tenant '{tenantName}' not found or not configured for OwnCloud storage");
                }

                // Construct WebDAV URL
                var webdavBase = _configuration.GetValue<string>("TenantSettings:BaseWebDAV");
                var tenantDirectory = tenant.TenantDirectory;
                var fileUrl = $"{webdavBase}/{tenantDirectory}/{fileName}";
                var ownerUser = tenant.Owner;
                // Get credentials (could be tenant owner or admin)
                var username = tenant.Owner?.UserName ?? _configuration.GetValue<string>("TenantSettings:Admin");
                var password = tenant.Owner?.Password ?? _configuration.GetValue<string>("TenantSettings:Password");

                // Create clean byte array from IFormFile
                byte[] fileBytes;
                using (var sourceStream = file.OpenReadStream())
                {
                    using var ms = new MemoryStream();
                    await sourceStream.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                }

                // Create WebDAV PUT request
                var request = new HttpRequestMessage(HttpMethod.Put, fileUrl);
                request.Content = new ByteArrayContent(fileBytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
                
                // Add basic authentication
                var authBytes = Encoding.UTF8.GetBytes($"{username}:{password}");
                var base64Auth = Convert.ToBase64String(authBytes);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);

                // Send the request
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var owncloudUrl = fileUrl;
                    _logger.LogInformation("Successfully uploaded file to OwnCloud: {Url}", owncloudUrl);
                    return owncloudUrl;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException($"OwnCloud upload failed with status {response.StatusCode}: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file to OwnCloud: {TenantName}/{FileName}", tenantName, fileName);
                throw;
            }
        }
        public async Task<Stream> DownloadFileAsync(string tenantName, string fileName)
        {
            try
            {
                _logger.LogInformation("Downloading file from OwnCloud: {TenantName}/{FileName}", tenantName, fileName);

                var tenant = await GetTenantWithOwner(tenantName);
                var webdavBase = _configuration.GetValue<string>("TenantSettings:BaseWebDAV");
                var dirl = System.Web.HttpUtility.UrlEncode(tenant.TenantDirectory);
                var url = $"{webdavBase}/{dirl}/{fileName}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddBasicAuthHeader(request, tenant.Owner);
                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStreamAsync();
                }

                throw new InvalidOperationException($"Download failed with status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download file from OwnCloud: {TenantName}/{FileName}", tenantName, fileName);
                throw;
            }
        }

        public async Task<string> GetDownloadUrlAsync(string tenantName, string fileName, TimeSpan? expiration = null)
        {
            try
            {
                // For OwnCloud, generate a direct WebDAV URL
                // Note: This doesn't implement actual expiration - that would require OwnCloud share API
                var tenant = await GetTenantWithOwner(tenantName);
                var webdavBase = _configuration.GetValue<string>("TenantSettings:BaseWebDAV");
                var dirl = System.Web.HttpUtility.UrlEncode(tenant.TenantDirectory);
                var url = $"{webdavBase}/{dirl}/{fileName}";

                _logger.LogInformation("Generated OwnCloud download URL: {TenantName}/{FileName}", tenantName, fileName);
                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate download URL: {TenantName}/{FileName}", tenantName, fileName);
                throw;
            }
        }

        public async Task<bool> DeleteFileAsync(string tenantName, string fileName)
        {
            try
            {
                _logger.LogInformation("Deleting file from OwnCloud: {TenantName}/{FileName}", tenantName, fileName);

                // Get tenant information for owner credentials
                var tenant = await GetTenantWithOwner(tenantName);

                // FIXED: Pass tenant owner credentials
                string dirl = System.Web.HttpUtility.UrlEncode(tenant.TenantDirectory); // Only encode directory
                var success = await ExecuteWebDAVAsUser("DELETE", $"{dirl}/{fileName}", null, tenant?.Owner);

                if (success)
                {
                    _logger.LogInformation("Successfully deleted file from OwnCloud: {TenantName}/{FileName}", tenantName, fileName);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file from OwnCloud: {TenantName}/{FileName}", tenantName, fileName);
                return false;
            }
        }
        public async Task<bool> FileExistsAsync(string tenantName, string fileName)
        {
            try
            {
                var tenant = await GetTenantWithOwner(tenantName);
                // Check if file exists using HEAD request
                string dirl = System.Web.HttpUtility.UrlEncode(tenant.TenantDirectory); // Only encode directory
                return await ExecuteWebDAVAsUser("HEAD", $"{dirl}/{fileName}", null, tenant.Owner);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if file exists in OwnCloud: {TenantName}/{FileName}", tenantName, fileName);
                return false;
            }
        }

        public async Task<long> GetFileSizeAsync(string tenantName, string fileName)
        {
            try
            {
                // This would require implementing WebDAV PROPFIND
                // For now, return 0 as placeholder
                _logger.LogInformation("Getting file size from OwnCloud: {TenantName}/{FileName}", tenantName, fileName);
                return 0; // Placeholder
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get file size from OwnCloud: {TenantName}/{FileName}", tenantName, fileName);
                return 0;
            }
        }

        public async Task<StorageStatistics> GetStorageStatisticsAsync(string tenantName)
        {
            try
            {
                // This would require implementing WebDAV directory listing
                // For now, return placeholder statistics
                return new StorageStatistics
                {
                    TenantName = tenantName,
                    StorageType = "OwnCloud",
                    TotalFiles = 0, // Placeholder
                    TotalSizeBytes = 0 // Placeholder
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get storage statistics for tenant: {TenantName}", tenantName);
                return new StorageStatistics { TenantName = tenantName, StorageType = "OwnCloud" };
            }
        }

        #region OwnCloud Helper Methods

        private async Task<bool> CreateGroupAsync(string groupName)
        {
            try
            {
                var values = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("groupid", groupName)
                };

                var messageContent = new FormUrlEncodedContent(values);
                var uri_path = _configuration.GetValue<string>("TenantSettings:GroupsPath");

                var request = new HttpRequestMessage(HttpMethod.Post, uri_path)
                {
                    Content = messageContent
                };

                AddBasicAuthHeader(request);

                var result = await _httpClient.SendAsync(request);
                return result.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create group: {GroupName}", groupName);
                return false;
            }
        }

        public async Task<bool> CreateUserAsync(User user, string groupName)
        {
            try
            {
                var values = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("userid", user.UserName),
                    new KeyValuePair<string, string>("password", user.Password),
                    new KeyValuePair<string, string>("email", user.UserEmail),
                    new KeyValuePair<string, string>("display", user.FullName),
                    new KeyValuePair<string, string>("groups[]", groupName)
                };

                var messageContent = new FormUrlEncodedContent(values);
                var uri_base = _configuration.GetValue<string>("TenantSettings:BaseAPI");
                var uri_path = _configuration.GetValue<string>("TenantSettings:UsersPath");
                
                var request = new HttpRequestMessage(HttpMethod.Post, uri_path)
                {
                    Content = messageContent
                };

                AddBasicAuthHeader(request);
                _httpClient.BaseAddress = new Uri(uri_base);
                var result = await _httpClient.SendAsync(request);
                return result.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user: {UserName}", user.UserName);
                return false;
            }
        }

        private async Task<bool> CreateDirectoryAsync(string directoryPath, User user)
        {
            try
            {
                return await ExecuteWebDAVAsUser("MKCOL", directoryPath, null, user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory: {DirectoryPath}", directoryPath);
                return false;
            }
        }

        #region WebDAV Command Execution - Explicit User vs Admin

        private async Task<bool> ExecuteWebDAVAsUser(string method, string path, string filePath, User user)
        {
            if (user == null)
            {
                throw new ArgumentException("User credentials are required for user WebDAV operations");
            }

            if (string.IsNullOrEmpty(user.UserName) || string.IsNullOrEmpty(user.Password))
            {
                throw new ArgumentException("User must have valid username and password for WebDAV operations");
            }

            try
            {
                var webdavBase = _configuration.GetValue<string>("TenantSettings:BaseWebDAV");
                var username = user.UserName;
                var password = user.Password;

                //var encodedPath = System.Web.HttpUtility.UrlEncode(path);
                var args = method switch
                {
                    "PUT" when !string.IsNullOrEmpty(filePath) =>
                        $"-X PUT -u {username}:{password} --data-binary @\"{filePath}\" \"{webdavBase}/{path}\"",
                    "DELETE" =>
                        $"-X DELETE -u {username}:{password} \"{webdavBase}/{path}\"",
                    "HEAD" =>
                        $"-X HEAD -u {username}:{password} \"{webdavBase}/{path}\"",
                    "GET" =>
                        $"-X GET -u {username}:{password} \"{webdavBase}/{path}\"",
                    _ => throw new ArgumentException($"Unsupported user WebDAV method: {method}")
                };

                _logger.LogDebug("Executing WebDAV command as USER {UserName}: {Method} {Path}", 
                    username, method, path);

                return await ExecuteCurlCommand(args, password);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute user WebDAV command: {Method} {Path} as user {UserName}", 
                    method, path, user.UserName);
                return false;
            }
        }

       
        /// Execute WebDAV command using admin credentials
        /// Used for: Directory creation, user setup, administrative operations
        
        private async Task<bool> ExecuteWebDAVAsAdmin(string method, string path, string filePath = null)
        {
            try
            {
                var webdavBase = _configuration.GetValue<string>("TenantSettings:BaseWebDAV");
                var username = _configuration.GetValue<string>("TenantSettings:Admin");
                var password = _configuration.GetValue<string>("TenantSettings:Password");

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("Admin credentials not configured");
                }

                
                var args = method switch
                {
                    "MKCOL" =>
                        $"-X MKCOL -u {username}:{password} \"{webdavBase}/{path}/\"",
                    "PUT" when !string.IsNullOrEmpty(filePath) =>
                        $"-X PUT -u {username}:{password} --data-binary @\"{filePath}\" \"{webdavBase}/{path}\"",
                    "DELETE" =>
                        $"-X DELETE -u {username}:{password} \"{webdavBase}/{path}\"",
                    _ => throw new ArgumentException($"Unsupported admin WebDAV method: {method}")
                };

                _logger.LogDebug("Executing WebDAV command as ADMIN: {Method} {Path}", method, path);

                return await ExecuteCurlCommand(args, password);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute admin WebDAV command: {Method} {Path}", method, path);
                return false;
            }
        }

        private async Task<XR50Tenant> GetTenantWithOwner(string tenantName)
        {
            var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
            
            // Try to fetch owner explicitly if not populated
            if (tenant?.Owner == null && !string.IsNullOrEmpty(tenant?.OwnerName) && !string.IsNullOrEmpty(tenant?.TenantSchema))
            {
                _logger.LogWarning("Tenant owner not populated, fetching explicitly for tenant: {TenantName}", tenantName);
                tenant.Owner = await _tenantManagementService.GetOwnerUserAsync(tenant.OwnerName, tenant.TenantSchema);
            }

            if (tenant?.Owner == null)
            {
                throw new InvalidOperationException($"Tenant owner credentials not available for tenant '{tenantName}'. Cannot perform file operations that require proper ownership.");
            }

            if (string.IsNullOrEmpty(tenant.Owner.UserName) || string.IsNullOrEmpty(tenant.Owner.Password))
            {
                throw new InvalidOperationException($"Tenant owner credentials incomplete for tenant '{tenantName}' (missing username or password).");
            }

            return tenant;
        }
        private async Task<bool> ExecuteCurlCommand(string args, string password)
        {
           
            var startInfo = new ProcessStartInfo
            {
                FileName = "curl",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var maskedArgs = args.Replace(password, "***PASSWORD***");
            _logger.LogInformation(" EXECUTING CURL COMMAND: curl {Args}", maskedArgs);
    
            // Also log it in a format you can copy-paste and test manually
            _logger.LogInformation("COPY-PASTE COMMAND: curl {Args}", maskedArgs);
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start curl process");
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.LogDebug("WebDAV command completed. Exit code: {ExitCode}", process.ExitCode);

            if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
            {
                _logger.LogWarning("WebDAV command stderr: {Error}", error);
            }

            return process.ExitCode == 0;
        }

        #endregion


        private void AddBasicAuthHeader(HttpRequestMessage request)
        {
            var username = _configuration.GetValue<string>("TenantSettings:Admin");
            var password = _configuration.GetValue<string>("TenantSettings:Password");
            var authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
        }
        #endregion

        #region Share Management

       
        /// Create a share in OwnCloud and return the share URL
        
        public async Task<string> CreateShareAsync(string tenantName, XR50Tenant tenant, Asset asset)
        {
            try
            {
                _logger.LogInformation("Creating OwnCloud share for asset {AssetId} in tenant {TenantName}",
                    asset.Id, tenantName);

                var values = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("shareType", "1"), // Group share
                    new KeyValuePair<string, string>("shareWith", tenant.TenantGroup ?? ""),
                    new KeyValuePair<string, string>("permissions", "1"), // Read permission
                    new KeyValuePair<string, string>("path", $"{tenant.TenantDirectory}/{asset.Filename}")
                };

                var messageContent = new FormUrlEncodedContent(values);
                var uri_base = _configuration.GetValue<string>("TenantSettings:BaseAPI");
                var uri_share = _configuration.GetValue<string>("TenantSettings:SharesPath");

                var request = new HttpRequestMessage(HttpMethod.Post, uri_share)
                {
                    Content = messageContent
                };

                AddBasicAuthHeader(request, tenant.Owner.UserName, tenant.Owner.Password);

                _httpClient.BaseAddress = new Uri(uri_base);
                var result = await _httpClient.SendAsync(request);

                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadAsStringAsync();
                    var baseUrl = _configuration.GetValue<string>("TenantSettings:BaseAPI") ?? "http://owncloud:8080";
                    var dirl = System.Web.HttpUtility.UrlEncode(tenant.TenantDirectory);
                    var shareUrl = $"{baseUrl}/remote.php/webdav/{dirl}/{asset.Filename}";
                    return shareUrl;
                }
                else
                {
                    _logger.LogError("Failed to create OwnCloud share for asset {AssetId}. Status: {StatusCode}",
                        asset.Id, result.StatusCode);
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating OwnCloud share for asset {AssetId}", asset.Id);
                return string.Empty;
            }
        }

       
        /// Delete a share from OwnCloud
        
        public async Task<bool> DeleteShareAsync(string tenantName, string shareId)
        {
            try
            {
                _logger.LogInformation("Deleting OwnCloud share {ShareId} for tenant {TenantName}",
                    shareId, tenantName);

                var uri_base = _configuration.GetValue<string>("TenantSettings:BaseAPI");
                var uri_share = _configuration.GetValue<string>("TenantSettings:SharesPath");

                var request = new HttpRequestMessage(HttpMethod.Delete, $"{uri_share}/{shareId}");

                // For deletion, we'd need tenant info to get the owner credentials
                // This is a simplification - in practice you'd need to get the tenant
                var username = _configuration.GetValue<string>("TenantSettings:Admin");
                var password = _configuration.GetValue<string>("TenantSettings:Password");
                AddBasicAuthHeader(request, username, password);

                _httpClient.BaseAddress = new Uri(uri_base);
                var result = await _httpClient.SendAsync(request);

                if (result.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully deleted OwnCloud share {ShareId}", shareId);
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to delete OwnCloud share {ShareId}. Status: {StatusCode}",
                        shareId, result.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting OwnCloud share {ShareId}", shareId);
                return false;
            }
        }

        #endregion

        #region Helper Methods

       
        /// Parse the share URL from OwnCloud XML response
        
        private string ParseShareUrlFromResponse(string xmlResponse)
        {
            try
            {
                _logger.LogInformation("DEBUG: Parsing XML response: {XmlResponse}", xmlResponse);
                
                var doc = System.Xml.Linq.XDocument.Parse(xmlResponse);
                
                _logger.LogInformation("DEBUG: XML root element: {RootName}", doc.Root?.Name);
                _logger.LogInformation("DEBUG: Looking for data element...");
                
                var dataElement = doc.Root?.Element("data");
                if (dataElement == null)
                {
                    _logger.LogWarning("DEBUG: No 'data' element found in response");
                    // Try different possible structures
                    _logger.LogInformation("DEBUG: Available root child elements: {Elements}", 
                        string.Join(", ", doc.Root?.Elements().Select(e => e.Name.LocalName) ?? new string[0]));
                }
                else
                {
                    _logger.LogInformation("DEBUG: Found data element, looking for url...");
                    var urlElement = dataElement.Element("url");
                    if (urlElement == null)
                    {
                        _logger.LogWarning("DEBUG: No 'url' element found in data");
                        _logger.LogInformation("DEBUG: Available data child elements: {Elements}", 
                            string.Join(", ", dataElement.Elements().Select(e => e.Name.LocalName)));
                    }
                }
                
                var url = doc.Root?.Element("data")?.Element("url")?.Value;
                _logger.LogInformation("DEBUG: Extracted URL: '{Url}'", url ?? "NULL");
                
                return url ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing share URL from OwnCloud response: {Response}", xmlResponse);
                return string.Empty;
            }
        }

       
        /// Add basic authentication header for OwnCloud requests
        
        private void AddBasicAuthHeader(HttpRequestMessage request, User user)
        {
            if (user != null)
            {
                var authenticationString = $"{user.UserName}:{user.Password}";
                var base64EncodedAuthenticationString = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes(authenticationString));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", base64EncodedAuthenticationString);
            }
        }

       
        /// Add basic authentication header with username/password
        
        private void AddBasicAuthHeader(HttpRequestMessage request, string username, string password)
        {
            var authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes(authenticationString));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", base64EncodedAuthenticationString);
        }

        #endregion

        // ... rest of existing methods ...
    }
}
        
