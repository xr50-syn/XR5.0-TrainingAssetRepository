using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<UsersController> _logger;
        private readonly IStorageService _storageService;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public UsersController(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<UsersController> logger,
            IStorageService storageService,
            IXR50TenantManagementService tenantManagementService,
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _storageService = storageService;
            _tenantManagementService = tenantManagementService;
            _configuration = configuration;
            _httpClient = httpClient;
        }
        // GET: api/{tenantName}/users
        [HttpGet]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers(string tenantName)
        {
            _logger.LogInformation("Getting users for tenant: {TenantName}", tenantName);

            using var context = _dbContextFactory.CreateDbContext();

            var users = await context.Users.ToListAsync();

            _logger.LogInformation("Found {UserCount} users for tenant: {TenantName}", users.Count, tenantName);

            return users;
        }

        // GET: api/{tenantName}/users/5
        [HttpGet("{userName}")]
        public async Task<ActionResult<User>> GetUser(string tenantName, string userName)
        {
            _logger.LogInformation("Getting user {UserName} for tenant: {TenantName}", userName, tenantName);

            using var context = _dbContextFactory.CreateDbContext();

            var user = await context.Users.FindAsync(userName);

            if (user == null)
            {
                _logger.LogWarning("User {UserName} not found in tenant: {TenantName}", userName, tenantName);
                return NotFound();
            }

            return user;
        }

        // FIXED: Create user in both MySQL and OwnCloud
        [HttpPost]
        public async Task<ActionResult<User>> PostUser(string tenantName, User user)
        {
            _logger.LogInformation("Creating user {UserName} for tenant: {TenantName}", user.UserName, tenantName);

            // Validate required fields
            if (string.IsNullOrWhiteSpace(user.UserName))
            {
                _logger.LogWarning("User creation failed: UserName is required for tenant: {TenantName}", tenantName);
                return BadRequest(new { Error = "UserName is required" });
            }

            if (string.IsNullOrWhiteSpace(user.Password))
            {
                _logger.LogWarning("User creation failed: Password is required for tenant: {TenantName}", tenantName);
                return BadRequest(new { Error = "Password is required" });
            }

            try
            {
                // 1. Create user in tenant database (MySQL)
                using var context = _dbContextFactory.CreateDbContext();

                // Check for duplicate username
                if (await context.Users.AnyAsync(u => u.UserName == user.UserName))
                {
                    _logger.LogWarning("User creation failed: UserName '{UserName}' already exists for tenant: {TenantName}",
                        user.UserName, tenantName);
                    return Conflict(new { Error = $"User '{user.UserName}' already exists" });
                }

                context.Users.Add(user);
                await context.SaveChangesAsync();

                _logger.LogInformation("Created user {UserName} in database for tenant: {TenantName}",
                    user.UserName, tenantName);

                // 2. Create user in OwnCloud (if using OwnCloud storage)
                if (_storageService.GetStorageType() == "OwnCloud")
                {
                    var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                    if (tenant != null)
                    {
                        var owncloudCreated = await CreateUserInOwnCloudAsync(user, tenant.TenantGroup);

                        if (owncloudCreated)
                        {
                            _logger.LogInformation("Created user {UserName} in OwnCloud for tenant: {TenantName}",
                                user.UserName, tenantName);
                        }
                        else
                        {
                            _logger.LogWarning(" Failed to create user {UserName} in OwnCloud for tenant: {TenantName}",
                                user.UserName, tenantName);
                            // For research prototype, don't fail the entire operation
                        }
                    }
                }

                return CreatedAtAction(nameof(GetUser),
                    new { tenantName, userName = user.UserName },
                    user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user {UserName} for tenant: {TenantName}", user.UserName, tenantName);
                return StatusCode(500, new { Error = "Failed to create user", Details = ex.Message });
            }
        }

        // FIXED: Update user in both MySQL and OwnCloud
        [HttpPut("{userName}")]
        public async Task<IActionResult> PutUser(string tenantName, string userName, User user)
        {
            _logger.LogInformation("Updating user {UserName} for tenant: {TenantName}", userName, tenantName);

            try
            {
                // 1. Fetch existing user from database
                using var context = _dbContextFactory.CreateDbContext();
                var existingUser = await context.Users.FindAsync(userName);

                if (existingUser == null)
                {
                    _logger.LogWarning("User {UserName} not found for update in tenant: {TenantName}", userName, tenantName);
                    return NotFound(new { Error = $"User '{userName}' not found" });
                }

                // 2. Apply partial updates (only update non-null/non-empty fields from request)
                if (!string.IsNullOrEmpty(user.FullName))
                {
                    existingUser.FullName = user.FullName;
                }
                if (!string.IsNullOrEmpty(user.UserEmail))
                {
                    existingUser.UserEmail = user.UserEmail;
                }
                if (!string.IsNullOrEmpty(user.Password))
                {
                    existingUser.Password = user.Password;
                }
                // Allow explicit setting of admin flag
                existingUser.admin = user.admin;

                await context.SaveChangesAsync();

                _logger.LogInformation("Updated user {UserName} in database for tenant: {TenantName}", userName, tenantName);

                // Use the merged user for OwnCloud updates
                user = existingUser;

                // 2. Update in OwnCloud (if using OwnCloud storage)
                if (_storageService.GetStorageType() == "OwnCloud")
                {
                    var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
                    if (tenant != null)
                    {
                        var owncloudUpdated = await UpdateUserInOwnCloudAsync(user, tenant.TenantGroup);

                        if (owncloudUpdated)
                        {
                            _logger.LogInformation("Updated user {UserName} in OwnCloud for tenant: {TenantName}",
                                userName, tenantName);
                        }
                        else
                        {
                            _logger.LogWarning(" Failed to update user {UserName} in OwnCloud for tenant: {TenantName}",
                                userName, tenantName);
                        }
                    }
                }

                return NoContent();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Concurrency conflict updating user {UserName} for tenant: {TenantName}", userName, tenantName);
                return Conflict(new { Error = "The user was modified by another request. Please retry." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user {UserName} for tenant: {TenantName}", userName, tenantName);
                return StatusCode(500, new { Error = "Failed to update user", Details = ex.Message });
            }
        }

        // FIXED: Delete user from both MySQL and OwnCloud
        [HttpDelete("{userName}")]
        public async Task<IActionResult> DeleteUser(string tenantName, string userName)
        {
            _logger.LogInformation("Deleting user {UserName} for tenant: {TenantName}", userName, tenantName);

            try
            {
                // 1. Delete from OwnCloud first (before removing from database)
                if (_storageService.GetStorageType() == "OwnCloud")
                {
                    var owncloudDeleted = await DeleteUserFromOwnCloudAsync(userName);

                    if (owncloudDeleted)
                    {
                        _logger.LogInformation("Deleted user {UserName} from OwnCloud for tenant: {TenantName}",
                            userName, tenantName);
                    }
                    else
                    {
                        _logger.LogWarning(" Failed to delete user {UserName} from OwnCloud for tenant: {TenantName}",
                            userName, tenantName);
                        // Continue with database deletion anyway
                    }
                }

                // 2. Delete from database
                using var context = _dbContextFactory.CreateDbContext();
                var user = await context.Users.FindAsync(userName);
                if (user == null)
                {
                    return NotFound();
                }

                context.Users.Remove(user);
                await context.SaveChangesAsync();

                _logger.LogInformation("Deleted user {UserName} from database for tenant: {TenantName}", userName, tenantName);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user {UserName} for tenant: {TenantName}", userName, tenantName);
                return StatusCode(500, new { Error = "Failed to delete user", Details = ex.Message });
            }
        }

        /// Create user in OwnCloud using the same logic as tenant creation
        
        private async Task<bool> CreateUserInOwnCloudAsync(User user, string groupName)
        {
            try
            {
                _logger.LogInformation("Creating user {UserName} in OwnCloud group: {GroupName}", user.UserName, groupName);

                var values = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("userid", user.UserName),
                new KeyValuePair<string, string>("password", user.Password),
                new KeyValuePair<string, string>("email", user.UserEmail ?? ""),
                new KeyValuePair<string, string>("display", user.FullName ?? ""),
                new KeyValuePair<string, string>("groups[]", groupName ?? "")
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

                if (result.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully created OwnCloud user: {UserName}", user.UserName);
                    return true;
                }
                else
                {
                    var errorContent = await result.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to create OwnCloud user {UserName}: {StatusCode}, Response: {ErrorContent}",
                        user.UserName, result.StatusCode, errorContent);

                    // Handle "user already exists" as success
                    if (errorContent.Contains("user already exists") || result.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        _logger.LogWarning("OwnCloud user {UserName} already exists, treating as success", user.UserName);
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception creating OwnCloud user: {UserName}", user.UserName);
                return false;
            }
        }

       
        /// Update user in OwnCloud (OwnCloud API supports user updates)
        
        private async Task<bool> UpdateUserInOwnCloudAsync(User user, string groupName)
        {
            try
            {
                _logger.LogInformation("Updating user {UserName} in OwnCloud", user.UserName);

                // OwnCloud user update API - PUT /users/{userid}
                var values = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("email", user.UserEmail ?? ""),
                new KeyValuePair<string, string>("display", user.FullName ?? "")
            };

                // Update password if provided
                if (!string.IsNullOrEmpty(user.Password))
                {
                    values.Add(new KeyValuePair<string, string>("password", user.Password));
                }

                var messageContent = new FormUrlEncodedContent(values);

                var uri_base = _configuration.GetValue<string>("TenantSettings:BaseAPI");
                var uri_path = _configuration.GetValue<string>("TenantSettings:UsersPath");

                var request = new HttpRequestMessage(HttpMethod.Put, $"{uri_path}/{user.UserName}")
                {
                    Content = messageContent
                };

                AddBasicAuthHeader(request);

                _httpClient.BaseAddress = new Uri(uri_base);
                var result = await _httpClient.SendAsync(request);

                if (result.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully updated OwnCloud user: {UserName}", user.UserName);
                    return true;
                }
                else
                {
                    var errorContent = await result.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to update OwnCloud user {UserName}: {StatusCode}, Response: {ErrorContent}",
                        user.UserName, result.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception updating OwnCloud user: {UserName}", user.UserName);
                return false;
            }
        }

       
        /// Delete user from OwnCloud
        
        private async Task<bool> DeleteUserFromOwnCloudAsync(string userName)
        {
            try
            {
                _logger.LogInformation("Deleting user {UserName} from OwnCloud", userName);

                var uri_base = _configuration.GetValue<string>("TenantSettings:BaseAPI");
                var uri_path = _configuration.GetValue<string>("TenantSettings:UsersPath");

                var request = new HttpRequestMessage(HttpMethod.Delete, $"{uri_path}/{userName}");

                AddBasicAuthHeader(request);

                _httpClient.BaseAddress = new Uri(uri_base);
                var result = await _httpClient.SendAsync(request);

                if (result.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully deleted OwnCloud user: {UserName}", userName);
                    return true;
                }
                else
                {
                    var errorContent = await result.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to delete OwnCloud user {UserName}: {StatusCode}, Response: {ErrorContent}",
                        userName, result.StatusCode, errorContent);

                    // Handle "user not found" as success (already deleted)
                    if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("OwnCloud user {UserName} not found, treating as success", userName);
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception deleting OwnCloud user: {UserName}", userName);
                return false;
            }
        }

       
        /// Add basic authentication header for OwnCloud API requests
        
        private void AddBasicAuthHeader(HttpRequestMessage request)
        {
            var username = _configuration.GetValue<string>("TenantSettings:Admin");
            var password = _configuration.GetValue<string>("TenantSettings:Password");
            var authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes(authenticationString));

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", base64EncodedAuthenticationString);
        }

        private async Task<bool> UserExistsAsync(string userName)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Users.AnyAsync(e => e.UserName == userName);
        }

    }
}