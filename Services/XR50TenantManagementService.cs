using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
using System.Text;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using MySql.Data.MySqlClient;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services
{
    // XR50 Tenant Management Service (Repository Pattern) - MySQL
    public interface IXR50TenantManagementService
    {
        Task<IEnumerable<XR50Tenant>> GetAllTenantsAsync();
        Task<XR50Tenant> GetTenantAsync(string tenantName);
        Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant);
        Task<User> GetOwnerUserAsync(string ownerName, string tenantName);
        Task DeleteTenantAsync(string tenantName);
        Task DeleteTenantCompletelyAsync(string tenantName); // New method for complete deletion
    }

    public class XR50TenantManagementService : IXR50TenantManagementService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly IXR50TenantService _tenantService;
        private readonly XR50MigrationService _migrationService;
        private readonly ILogger<XR50TenantManagementService> _logger;

        public XR50TenantManagementService(
            IConfiguration configuration,
            HttpClient httpClient,
            IXR50TenantService tenantService,
            XR50MigrationService migrationService,
            ILogger<XR50TenantManagementService> logger)
            {
            _configuration = configuration;
            _httpClient = httpClient;
            _tenantService = tenantService;
            _migrationService = migrationService;
            _logger = logger;
            }
        public async Task<IEnumerable<XR50Tenant>> GetAllTenantsAsync()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            // FIXED: CREATE TABLE with S3 columns (same as above)
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS `XR50TenantRegistry` (
                    `TenantName` varchar(100) NOT NULL PRIMARY KEY,
                    `TenantGroup` varchar(100) NULL,
                    `Description` varchar(500) NULL,
                    `StorageType` varchar(50) NOT NULL DEFAULT 'OwnCloud',
                    `TenantDirectory` varchar(500) NULL,
                    `S3BucketName` varchar(255) NULL,
                    `S3BucketRegion` varchar(50) NULL,
                    `S3BucketArn` varchar(255) NULL,
                    `StorageEndpoint` varchar(255) NULL,
                    `OwnerName` varchar(255) NULL,
                    `DatabaseName` varchar(100) NOT NULL,
                    `CreatedAt` datetime NOT NULL,
                    `IsActive` boolean NOT NULL DEFAULT 1
                )";

            using var createCommand = new MySqlCommand(createTableSql, connection);
            await createCommand.ExecuteNonQueryAsync();

            // FIXED: SELECT with S3 fields
            var sql = @"
                SELECT TenantName, TenantGroup, Description, StorageType, TenantDirectory,
                    S3BucketName, S3BucketRegion, S3BucketArn, StorageEndpoint,
                    OwnerName, DatabaseName, CreatedAt, IsActive
                FROM XR50TenantRegistry 
                WHERE IsActive = 1 
                ORDER BY CreatedAt DESC";

            using var command = new MySqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            var results = new List<XR50Tenant>();
            while (await reader.ReadAsync())
            {
                // FIXED: Map S3 fields from database
                results.Add(new XR50Tenant
                {
                    TenantName = reader["TenantName"]?.ToString() ?? "",
                    TenantGroup = reader["TenantGroup"]?.ToString(),
                    Description = reader["Description"]?.ToString(),
                    StorageType = reader["StorageType"]?.ToString() ?? "OwnCloud",
                    TenantDirectory = reader["TenantDirectory"]?.ToString(),
                    S3BucketName = reader["S3BucketName"]?.ToString(),
                    S3BucketRegion = reader["S3BucketRegion"]?.ToString(),
                    S3BucketArn = reader["S3BucketArn"]?.ToString(),
                    StorageEndpoint = reader["StorageEndpoint"]?.ToString(),
                    OwnerName = reader["OwnerName"]?.ToString(),
                    TenantSchema = reader["DatabaseName"]?.ToString(),
                    CreatedAt = reader["CreatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedAt"]) : DateTime.UtcNow
                });
            }

            return results;
        }

        // ===== FIX 3: XR50TenantManagementService.GetTenantAsync =====

        public async Task<XR50Tenant> GetTenantAsync(string tenantName)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            
            // FIXED: SELECT with S3 fields
            var sql = @"
                SELECT TenantName, TenantGroup, Description, StorageType, TenantDirectory,
                    S3BucketName, S3BucketRegion, S3BucketArn, StorageEndpoint,
                    OwnerName, DatabaseName, CreatedAt, IsActive
                FROM XR50TenantRegistry 
                WHERE TenantName = @tenantName AND IsActive = 1";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@tenantName", tenantName);
            
            using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                // FIXED: Map S3 fields from database
                return new XR50Tenant
                {
                    TenantName = reader["TenantName"]?.ToString() ?? "",
                    TenantGroup = reader["TenantGroup"]?.ToString(),
                    Description = reader["Description"]?.ToString(),
                    StorageType = reader["StorageType"]?.ToString() ?? "OwnCloud",
                    TenantDirectory = reader["TenantDirectory"]?.ToString(),
                    S3BucketName = reader["S3BucketName"]?.ToString(),
                    S3BucketRegion = reader["S3BucketRegion"]?.ToString(),
                    S3BucketArn = reader["S3BucketArn"]?.ToString(),
                    StorageEndpoint = reader["StorageEndpoint"]?.ToString(),
                    OwnerName = reader["OwnerName"]?.ToString(),
                    TenantSchema = reader["DatabaseName"]?.ToString(),
                    CreatedAt = reader["CreatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedAt"]) : DateTime.UtcNow
                };
            }

            throw new ArgumentException($"Tenant '{tenantName}' not found");
        }

        public async Task<User> GetOwnerUserAsync(string ownerName, string tenantDatabaseName)
        {
            try
            {
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var tenantConnectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={tenantDatabaseName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogDebug("Fetching owner user {OwnerName} from tenant database: {TenantDatabase}", ownerName, tenantDatabaseName);

                using var connection = new MySqlConnection(tenantConnectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT UserName, FullName, UserEmail, Password, admin
                    FROM Users 
                    WHERE UserName = @userName";

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@userName", ownerName);

                using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync()) 
                {
                    _logger.LogWarning("Owner user {OwnerName} not found in tenant database: {TenantDatabase}", ownerName, tenantDatabaseName);
                    return null;
                }

                var owner = new User
                {
                    UserName = reader["UserName"]?.ToString(),
                    FullName = reader["FullName"]?.ToString(),
                    UserEmail = reader["UserEmail"]?.ToString(),
                    Password = reader["Password"]?.ToString(),
                    admin = Convert.ToBoolean(reader["admin"]),
                };

                _logger.LogDebug("Found owner user {OwnerName} in tenant database: {TenantDatabase}", ownerName, tenantDatabaseName);
                return owner;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch owner user {OwnerName} from tenant database: {TenantDatabase}", ownerName, tenantDatabaseName);
                return null; // Return null instead of throwing - tenant info is still valid without owner details
            }
        }
        public async Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant)
        {
            // Validate tenant doesn't already exist
            if (await _tenantService.TenantExistsAsync(tenant.TenantName))
            {
                throw new InvalidOperationException($"Tenant '{tenant.TenantName}' already exists");
            }

            // Create the tenant database and infrastructure
            var createdTenant = await _tenantService.CreateTenantAsync(tenant);

            // Set the database name for the response
            createdTenant.TenantSchema = _tenantService.GetTenantSchema(tenant.TenantName);
            await CreateTenantStorageAsync(tenant);
            return createdTenant;
        }

        public async Task<XR50Tenant> UpdateTenantAsync(string tenantName, XR50Tenant tenant)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            // FIXED: UPDATE with S3 fields
            var sql = @"
                UPDATE XR50TenantRegistry 
                SET TenantGroup = @tenantGroup,
                    Description = @description,
                    StorageType = @storageType,
                    TenantDirectory = @tenantDirectory,
                    S3BucketName = @s3BucketName,
                    S3BucketRegion = @s3BucketRegion,
                    S3BucketArn = @s3BucketArn,
                    StorageEndpoint = @storageEndpoint,
                    OwnerName = @ownerName
                WHERE TenantName = @tenantName AND IsActive = 1";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@tenantName", tenantName);
            command.Parameters.AddWithValue("@tenantGroup", tenant.TenantGroup ?? "");
            command.Parameters.AddWithValue("@description", tenant.Description ?? "");
            command.Parameters.AddWithValue("@storageType", tenant.StorageType ?? "OwnCloud");
            command.Parameters.AddWithValue("@tenantDirectory", tenant.TenantDirectory ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@s3BucketName", tenant.S3BucketName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@s3BucketRegion", tenant.S3BucketRegion ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@s3BucketArn", tenant.S3BucketArn ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@storageEndpoint", tenant.StorageEndpoint ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ownerName", tenant.Owner?.UserName ?? tenant.OwnerName ?? "");

            await command.ExecuteNonQueryAsync();

            return await GetTenantAsync(tenantName);
        }

        public async Task DeleteTenantAsync(string tenantName)
        {
            //Storage First or otherwise we will nuke db
            //await DeleteTenantStorageAsync(tenantName);
            await DeleteTenantCompletelyAsync(tenantName);
           /* var connectionString = _configuration.GetConnectionString("DefaultConnection");
             using var connection = new MySqlConnection(connectionString);
             await connection.OpenAsync();

             var sql = @"
                 UPDATE XR50TenantRegistry 
                 SET IsActive = 0 
                 WHERE TenantName = @tenantName";

             using var command = new MySqlCommand(sql, connection);
             command.Parameters.AddWithValue("@tenantName", tenantName);
             await command.ExecuteNonQueryAsync();
             */
            _logger.LogInformation("Marked tenant {TenantName} as inactive in registry (database still exists)", tenantName);
        }

        public async Task DeleteTenantCompletelyAsync(string tenantName)
        {
            try
            {
                _logger.LogWarning("Starting complete deletion of tenant: {TenantName}", tenantName);

                // 1. Delete the tenant database
                var databaseDeleted = await _migrationService.DeleteTenantDatabaseAsync(tenantName);

                if (!databaseDeleted)
                {
                    _logger.LogWarning("Failed to delete database for tenant {TenantName}, continuing with registry cleanup", tenantName);
                }

                // 2. Remove from registry completely
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                var sql = "DELETE FROM XR50TenantRegistry WHERE TenantName = @tenantName";
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@tenantName", tenantName);
                await command.ExecuteNonQueryAsync();
                //await DeleteTenantStorageAsync(tenantName);
                _logger.LogInformation("Completely deleted tenant {TenantName} (database and registry entry)", tenantName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during complete deletion of tenant {TenantName}", tenantName);
                throw;
            }
        }
        public async Task CreateTenantStorageAsync(XR50Tenant tenant) {

            // Check storage type and only do OwnCloud operations for OwnCloud tenants
            if (!tenant.IsOwnCloudStorage())
            {
                _logger.LogInformation("Tenant {TenantName} uses {StorageType} storage, skipping OwnCloud user/group creation", 
                    tenant.TenantName, tenant.StorageType);
                return; // Skip all OwnCloud operations for non-OwnCloud tenants
            }
            var values = new List<KeyValuePair<string, string>>();
            values.Add(new KeyValuePair<string, string>("groupid", tenant.TenantGroup));
            FormUrlEncodedContent messageContent = new FormUrlEncodedContent(values);
            string username = _configuration.GetValue<string>("TenantSettings:Admin");
            string password = _configuration.GetValue<string>("TenantSettings:Password");
            string uri_base = _configuration.GetValue<string>("TenantSettings:BaseAPI");
            string uri_path = _configuration.GetValue<string>("TenantSettings:GroupsPath");
            string webdav_base = _configuration.GetValue<string>("TenantSettings:BaseWebDAV");
            string authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
            var request = new HttpRequestMessage(HttpMethod.Post, uri_path)
            {
                Content = messageContent
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            _httpClient.BaseAddress = new Uri(uri_base);
        // _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            var result = await _httpClient.SendAsync(request);
            string resultContent = await result.Content.ReadAsStringAsync();
            User adminUser = tenant.Owner;

            //Console.WriteLine($"Response content: {resultContent}");
            //Create the admin User
            var valuesAdmin = new List<KeyValuePair<string, string>>();
            valuesAdmin.Add(new KeyValuePair<string, string>("userid", adminUser.UserName));
            valuesAdmin.Add(new KeyValuePair<string, string>("password", adminUser.Password));
            valuesAdmin.Add(new KeyValuePair<string, string>("email", adminUser.UserEmail));
            valuesAdmin.Add(new KeyValuePair<string, string>("display", adminUser.FullName));
            valuesAdmin.Add(new KeyValuePair<string, string>("groups[]", tenant.TenantGroup));
            //Target The User Interface
            uri_path = _configuration.GetValue<string>("TenantSettings:UsersPath");
            FormUrlEncodedContent messageContentAdmin = new FormUrlEncodedContent(valuesAdmin);
        
            var requestAdmin = new HttpRequestMessage(HttpMethod.Post, uri_path)
            {
                Content = messageContentAdmin
            };
            requestAdmin.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
        
            // _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            var resultAdmin = await _httpClient.SendAsync(requestAdmin);
            string resultAdminContent = await resultAdmin.Content.ReadAsStringAsync();
            Console.WriteLine($"Response content: {resultAdminContent}");

            // Create root dir for the Tenant, owned by Admin
            string cmd="curl";
            string dirl=System.Web.HttpUtility.UrlEncode(tenant.TenantDirectory);
            string Arg= $"-X MKCOL -u {adminUser.UserName}:{adminUser.Password} \"{webdav_base}/{dirl}/\"";
            // Create root dir for the Tenant
            Console.WriteLine("Executing command:" + cmd + " " + Arg);
            var startInfo = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = Arg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(30000); // 30 second timeout
                Console.WriteLine("Output: " + output);
                Console.WriteLine("Error: " + error);
            }
            
        }

        public async Task DeleteTenantStorageAsync(string tenantName)
        {
            var allowDestructiveDelete = _configuration.GetValue<bool>("TenantSettings:AllowDestructiveStorageDelete");
            if (!allowDestructiveDelete) 
            {
               return;
            }     
            var tenant = await GetTenantAsync(tenantName);
            if (tenant == null){
                Console.WriteLine($"Did not find Tenant with name: {tenantName}");
                return;
            }

            // Check storage type and only do OwnCloud operations for OwnCloud tenants
            if (!tenant.IsOwnCloudStorage())
            {
                _logger.LogInformation("Tenant {TenantName} uses {StorageType} storage, skipping OwnCloud deletion",
                    tenant.TenantName, tenant.StorageType);
                return; // Skip all OwnCloud operations for non-OwnCloud tenants
            }

            User adminUser = await GetOwnerUserAsync(tenant.OwnerName,tenant.TenantSchema);
            var values = new List<KeyValuePair<string, string>>();
            FormUrlEncodedContent messageContent = new FormUrlEncodedContent(values);
            string username = _configuration.GetValue<string>("TenantSettings:Admin");
            string password = _configuration.GetValue<string>("TenantSettings:Password");
            string uri_base = _configuration.GetValue<string>("TenantSettings:BaseAPI");
            string uri_group = _configuration.GetValue<string>("TenantSettings:GroupsPath");
            string uri_user = _configuration.GetValue<string>("TenantSettings:UsersPath");
            string webdav_base = _configuration.GetValue<string>("TenantSettings:BaseWebDAV");

            string authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{uri_group}/{tenant.TenantGroup}")
            {
                Content = messageContent
            };
            Console.WriteLine(tenant.TenantGroup);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            _httpClient.BaseAddress = new Uri(uri_base);
            //_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            var result = await _httpClient.SendAsync(request);
            string resultContent = await result.Content.ReadAsStringAsync();
            // Delete root dir for the Tenant
            string cmd= "curl";
            string dirl=System.Web.HttpUtility.UrlEncode(tenant.TenantDirectory);
            string Arg=  $"-X DELETE -u {adminUser.UserName}:{adminUser.Password} \"{webdav_base}/{dirl}/\"";
            Console.WriteLine("Executing command: " + cmd + " " + Arg);
            var startInfo = new ProcessStartInfo
            {                                                                                                                           FileName = cmd,
                Arguments = Arg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(30000); // 30 second timeout
                Console.WriteLine("Output: " + output);
                Console.WriteLine("Error: " + error);
            }
            request = new HttpRequestMessage(HttpMethod.Delete, $"{uri_user}/{tenant.OwnerName}")
            {
                Content = messageContent
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            // _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            result = await _httpClient.SendAsync(request);
            resultContent = await result.Content.ReadAsStringAsync();    
                    }


    }
}