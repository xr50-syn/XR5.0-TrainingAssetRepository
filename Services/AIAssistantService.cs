using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for interacting with the DataLens AI API (v1).
    /// Provides inference queries with session management and document uploads.
    /// All operations are scoped to a DataLens collection.
    /// </summary>
    public class AIAssistantService : IAIAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly IAIAssistantMaterialService _aiAssistantMaterialService;
        private readonly IXR50TenantService _tenantService;
        private readonly IXR50TenantManagementService _tenantManagementService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AIAssistantService> _logger;
        private readonly string _baseUrl;

        public AIAssistantService(
            HttpClient httpClient,
            IAIAssistantMaterialService aiAssistantMaterialService,
            IXR50TenantService tenantService,
            IXR50TenantManagementService tenantManagementService,
            IConfiguration configuration,
            ILogger<AIAssistantService> logger)
        {
            _httpClient = httpClient;
            _aiAssistantMaterialService = aiAssistantMaterialService;
            _tenantService = tenantService;
            _tenantManagementService = tenantManagementService;
            _configuration = configuration;
            _logger = logger;

            _baseUrl = configuration["ChatbotApi:BaseUrl"]
                ?? Environment.GetEnvironmentVariable("CHATBOT_API_BASE_URL")
                ?? "http://localhost:5001";

            // Bearer token authentication (v1 API)
            var bearerToken = configuration["ChatbotApi:BearerToken"]
                ?? Environment.GetEnvironmentVariable("CHATBOT_API_BEARER_TOKEN");

            if (!string.IsNullOrEmpty(bearerToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            _httpClient.BaseAddress = new Uri(_baseUrl.TrimEnd('/') + "/");
            _httpClient.Timeout = TimeSpan.FromSeconds(60); // Longer timeout for audio generation
        }

        // Resolve the current tenant's per-tenant default DataLens collection. Used as the
        // fallback for AIAssistantMaterials that don't define their own CollectionName.
        // Sharing one global collection across tenants would let a chatbot query in tenant A
        // surface documents another tenant uploaded — see XR50Tenant.DefaultAICollection.
        private async Task<string> GetTenantDefaultCollectionAsync()
        {
            var tenantName = _tenantService.GetCurrentTenant();
            var tenant = await _tenantManagementService.GetTenantAsync(tenantName);
            if (string.IsNullOrEmpty(tenant?.DefaultAICollection))
            {
                throw new InvalidOperationException(
                    $"Tenant '{tenantName}' has no DefaultAICollection configured; cannot route AI assistant request to a default collection");
            }
            return tenant.DefaultAICollection;
        }

        #region Default Endpoint Operations

        public async Task<AIAssistantAskResponse> AskAsync(string query, string? sessionId = null)
        {
            var collectionName = await GetTenantDefaultCollectionAsync();
            return await AskCollectionAsync(collectionName, query, sessionId, sourceFiles: null);
        }

        public async Task<AIAssistantDocumentUploadResponse> UploadDocumentAsync(Stream fileStream, string fileName, string contentType)
        {
            var collectionName = await GetTenantDefaultCollectionAsync();
            return await UploadDocumentToCollectionAsync(collectionName, fileStream, fileName, contentType);
        }

        public async Task<bool> IsDefaultEndpointAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region AIAssistantMaterial-specific Operations

        public async Task<AIAssistantAskResponse> AskAsync(int aiAssistantMaterialId, string query, string? sessionId = null)
        {
            var aiAssistantMaterial = await _aiAssistantMaterialService.GetByIdAsync(aiAssistantMaterialId);
            if (aiAssistantMaterial == null)
            {
                throw new KeyNotFoundException($"AIAssistantMaterial with ID {aiAssistantMaterialId} not found");
            }

            var collectionName = aiAssistantMaterial.CollectionName ?? await GetTenantDefaultCollectionAsync();

            // Check for existing valid session
            var existingSession = await _aiAssistantMaterialService.GetActiveSessionAsync(aiAssistantMaterialId);

            string? sessionIdToUse = sessionId ?? existingSession?.SessionId;
            string? sourceFiles = null;

            // If no session exists (or explicit sessionId not provided and no stored session),
            // this is a first call - use source_files to establish document context
            if (existingSession == null && string.IsNullOrEmpty(sessionId))
            {
                var assets = await _aiAssistantMaterialService.GetAssetsAsync(aiAssistantMaterialId);
                var filenames = assets.Select(a => a.Filename).Where(f => !string.IsNullOrEmpty(f)).ToList();

                if (filenames.Any())
                {
                    sourceFiles = string.Join(",", filenames);
                    _logger.LogInformation("First call for AI Assistant material {AIAssistantMaterialId}. Using source_files: {SourceFiles}",
                        aiAssistantMaterialId, sourceFiles);
                }

                sessionIdToUse = null; // No session for first call
            }
            else
            {
                _logger.LogInformation("Using existing session for AI Assistant material {AIAssistantMaterialId}. Session ID: {SessionId}",
                    aiAssistantMaterialId, sessionIdToUse);
            }

            // Make the API call
            var response = await AskCollectionAsync(collectionName, query, sessionIdToUse, sourceFiles);

            // If this was a first call and we got a session_id back, store it
            if (existingSession == null && string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(response.SessionId))
            {
                await _aiAssistantMaterialService.CreateSessionAsync(aiAssistantMaterialId, response.SessionId);
                _logger.LogInformation("Stored new session for AI Assistant material {AIAssistantMaterialId}. Session ID: {SessionId}",
                    aiAssistantMaterialId, response.SessionId);
            }

            return response;
        }

        public async Task<AIAssistantDocumentUploadResponse> UploadDocumentAsync(int aiAssistantMaterialId, Stream fileStream, string fileName, string contentType)
        {
            var aiAssistantMaterial = await _aiAssistantMaterialService.GetByIdAsync(aiAssistantMaterialId);
            if (aiAssistantMaterial == null)
            {
                throw new KeyNotFoundException($"AIAssistantMaterial with ID {aiAssistantMaterialId} not found");
            }

            var collectionName = aiAssistantMaterial.CollectionName ?? await GetTenantDefaultCollectionAsync();

            _logger.LogInformation("Uploading document for AI Assistant material {AIAssistantMaterialId} to collection {CollectionName}: {FileName}",
                aiAssistantMaterialId, collectionName, fileName);

            return await UploadDocumentToCollectionAsync(collectionName, fileStream, fileName, contentType);
        }

        public async Task<IEnumerable<AIAssistantDocumentInfo>> GetDocumentsAsync(int aiAssistantMaterialId)
        {
            var aiAssistantMaterial = await _aiAssistantMaterialService.GetByIdAsync(aiAssistantMaterialId);
            if (aiAssistantMaterial == null)
            {
                throw new KeyNotFoundException($"AIAssistantMaterial with ID {aiAssistantMaterialId} not found");
            }

            var assets = await _aiAssistantMaterialService.GetAssetsAsync(aiAssistantMaterialId);

            return assets.Select(a => new AIAssistantDocumentInfo
            {
                AssetId = a.Id,
                FileName = a.Filename,
                Status = a.AiAvailable ?? "notready",
                JobId = a.JobId,
                UploadedAt = null
            });
        }

        public async Task<bool> IsEndpointAvailableAsync(int aiAssistantMaterialId)
        {
            var aiAssistantMaterial = await _aiAssistantMaterialService.GetByIdAsync(aiAssistantMaterialId);
            if (aiAssistantMaterial == null)
            {
                return false;
            }

            return await IsDefaultEndpointAvailableAsync();
        }

        #endregion

        #region Internal Collection Operations

        private async Task<AIAssistantAskResponse> AskCollectionAsync(
            string collectionName, string query, string? sessionId, string? sourceFiles)
        {
            _logger.LogInformation("Sending query to collection {CollectionName}: {Query}", collectionName, query);

            // Build the inference URL with query parameters
            var queryParams = new List<string>
            {
                $"query={Uri.EscapeDataString(query)}"
            };

            if (!string.IsNullOrEmpty(sessionId))
            {
                queryParams.Add($"session_id={Uri.EscapeDataString(sessionId)}");
            }

            if (!string.IsNullOrEmpty(sourceFiles))
            {
                queryParams.Add($"source_files={Uri.EscapeDataString(sourceFiles)}");
            }

            var inferenceUrl = $"api/v1/collections/{Uri.EscapeDataString(collectionName)}/inferences?{string.Join("&", queryParams)}";

            try
            {
                var response = await _httpClient.PostAsync(inferenceUrl, null);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("Received response from collection {CollectionName}: {Response}",
                    collectionName,
                    responseContent.Length > 500 ? responseContent.Substring(0, 500) + "..." : responseContent);

                return ParseAskResponse(responseContent, query, sessionId);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to communicate with DataLens inference endpoint for collection {CollectionName}",
                    collectionName);
                throw new InvalidOperationException($"Failed to communicate with AI assistant: {ex.Message}", ex);
            }
        }

        private async Task<AIAssistantDocumentUploadResponse> UploadDocumentToCollectionAsync(
            string collectionName, Stream fileStream, string fileName, string contentType)
        {
            _logger.LogInformation("Uploading document to collection {CollectionName}: {FileName}",
                collectionName, fileName);

            try
            {
                using var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                content.Add(streamContent, "file", fileName);

                var uploadUrl = $"api/v1/collections/{Uri.EscapeDataString(collectionName)}/documents";
                var response = await _httpClient.PostAsync(uploadUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Document upload to collection {CollectionName} failed: {StatusCode} - {Error}",
                        collectionName, response.StatusCode, errorContent);
                    throw new InvalidOperationException($"Failed to upload document: {response.StatusCode} - {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<DataLensDocumentUploadResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("Document uploaded to collection {CollectionName}. Job ID: {JobId}",
                    collectionName, result?.JobId);

                return new AIAssistantDocumentUploadResponse
                {
                    JobId = result?.JobId,
                    Status = result?.Status ?? "pending",
                    Message = "Document submitted for processing",
                    DocumentId = result?.Document,
                    CollectionName = result?.CollectionName ?? collectionName
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error uploading document to collection {CollectionName}", collectionName);
                throw new InvalidOperationException($"Network error uploading document: {ex.Message}", ex);
            }
        }

        #endregion

        #region Response Parsing

        private AIAssistantAskResponse ParseAskResponse(string responseContent, string query, string? sessionId)
        {
            try
            {
                var apiResponse = JsonSerializer.Deserialize<AIAssistantApiResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new AIAssistantAskResponse
                {
                    SessionId = apiResponse?.SessionId ?? sessionId ?? string.Empty,
                    Query = query,
                    Text = apiResponse?.Response?.Speech?.Text,
                    AudioUrl = apiResponse?.Response?.Speech?.Link,
                    Sources = apiResponse?.Sources,
                    Markdown = apiResponse?.Response?.Markdown,
                    CollectionName = apiResponse?.CollectionName
                };
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse AI assistant response, returning raw content");
                return new AIAssistantAskResponse
                {
                    SessionId = sessionId ?? string.Empty,
                    Query = query,
                    Text = responseContent
                };
            }
        }

        #endregion

        #region Internal Response Models

        private class AIAssistantApiResponse
        {
            [JsonPropertyName("session_id")]
            public string? SessionId { get; set; }

            [JsonPropertyName("query")]
            public string? Query { get; set; }

            [JsonPropertyName("collection_name")]
            public string? CollectionName { get; set; }

            [JsonPropertyName("response")]
            public AIAssistantApiResponseContent? Response { get; set; }

            [JsonPropertyName("reasoning")]
            public string? Reasoning { get; set; }

            [JsonPropertyName("sources")]
            public List<string>? Sources { get; set; }
        }

        private class AIAssistantApiResponseContent
        {
            [JsonPropertyName("speech")]
            public AIAssistantApiSpeechContent? Speech { get; set; }

            [JsonPropertyName("markdown")]
            public string? Markdown { get; set; }

            [JsonPropertyName("images")]
            public List<string>? Images { get; set; }
        }

        private class AIAssistantApiSpeechContent
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("link")]
            public string? Link { get; set; }
        }

        #endregion
    }
}
