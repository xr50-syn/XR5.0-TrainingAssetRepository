using System.Text.Json;
using System.Text.Json.Serialization;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for interacting with the AI Assistant API (Siemens API wrapper).
    /// Provides document upload and conversational chat with audio responses.
    /// Manages sessions automatically - first call appends filenames, subsequent calls use stored session.
    /// </summary>
    public class AIAssistantService : IAIAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly IAIAssistantMaterialService _aiAssistantMaterialService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AIAssistantService> _logger;
        private readonly string _baseUrl;

        public AIAssistantService(
            HttpClient httpClient,
            IAIAssistantMaterialService aiAssistantMaterialService,
            IConfiguration configuration,
            ILogger<AIAssistantService> logger)
        {
            _httpClient = httpClient;
            _aiAssistantMaterialService = aiAssistantMaterialService;
            _configuration = configuration;
            _logger = logger;

            // Use same base URL as ChatbotApi
            _baseUrl = configuration["ChatbotApi:BaseUrl"]
                ?? Environment.GetEnvironmentVariable("CHATBOT_API_BASE_URL")
                ?? "http://localhost:5001/docs";

            var apiKey = configuration["ChatbotApi:ApiKey"]
                ?? Environment.GetEnvironmentVariable("CHATBOT_API_KEY");

            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }

            _httpClient.BaseAddress = new Uri(_baseUrl.TrimEnd('/') + "/");
            _httpClient.Timeout = TimeSpan.FromSeconds(60); // Longer timeout for audio generation
        }

        #region Default Endpoint Operations

        public async Task<AIAssistantAskResponse> AskAsync(string query, string? sessionId = null)
        {
            _logger.LogInformation("Sending query to default AI assistant: {Query}", query);

            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("query", query),
                new KeyValuePair<string, string>("session_id", sessionId ?? string.Empty)
            });

            var askUrl = "ask";

            try
            {
                var response = await _httpClient.PostAsync(askUrl, formContent);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("Received response from AI assistant: {Response}",
                    responseContent.Length > 500 ? responseContent.Substring(0, 500) + "..." : responseContent);

                return ParseAskResponse(responseContent, query, sessionId);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to communicate with AI assistant endpoint {Endpoint}", askUrl);
                throw new InvalidOperationException($"Failed to communicate with AI assistant: {ex.Message}", ex);
            }
        }

        public async Task<AIAssistantDocumentUploadResponse> UploadDocumentAsync(Stream fileStream, string fileName, string contentType)
        {
            _logger.LogInformation("Uploading document to AI assistant: {FileName}", fileName);

            try
            {
                using var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                content.Add(streamContent, "file", fileName);

                var response = await _httpClient.PostAsync("document", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("AI assistant document upload failed: {StatusCode} - {Error}",
                        response.StatusCode, errorContent);
                    throw new InvalidOperationException($"Failed to upload document: {response.StatusCode} - {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<AIAssistantDocumentApiResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("Document uploaded successfully. Job ID: {JobId}", result?.JobId);

                return new AIAssistantDocumentUploadResponse
                {
                    JobId = result?.JobId,
                    Status = result?.Status ?? "pending",
                    Message = result?.Message ?? "Document submitted for processing",
                    DocumentId = result?.DocumentId
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error uploading document to AI assistant");
                throw new InvalidOperationException($"Network error uploading document: {ex.Message}", ex);
            }
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

            // Check for existing valid session
            var existingSession = await _aiAssistantMaterialService.GetActiveSessionAsync(aiAssistantMaterialId);

            string effectiveQuery = query;
            string? sessionIdToUse = sessionId ?? existingSession?.SessionId;

            // If no session exists (or explicit sessionId not provided and no stored session),
            // this is a first call - append filenames
            if (existingSession == null && string.IsNullOrEmpty(sessionId))
            {
                var assets = await _aiAssistantMaterialService.GetAssetsAsync(aiAssistantMaterialId);
                var filenames = assets.Select(a => a.Filename).Where(f => !string.IsNullOrEmpty(f)).ToList();

                if (filenames.Any())
                {
                    var filenameList = string.Join(", ", filenames);
                    effectiveQuery = $"{query} using filenames {filenameList}";
                    _logger.LogInformation("First call for AI Assistant material {AIAssistantMaterialId}. Appending filenames: {Filenames}",
                        aiAssistantMaterialId, filenameList);
                }

                sessionIdToUse = null; // No session for first call
            }
            else
            {
                _logger.LogInformation("Using existing session for AI Assistant material {AIAssistantMaterialId}. Session ID: {SessionId}",
                    aiAssistantMaterialId, sessionIdToUse);
            }

            // Make the API call
            var response = await AskAsync(effectiveQuery, sessionIdToUse);

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

            _logger.LogInformation("Uploading document for AI Assistant material {AIAssistantMaterialId}: {FileName}",
                aiAssistantMaterialId, fileName);

            // Upload the document
            var result = await UploadDocumentAsync(fileStream, fileName, contentType);

            // Note: The document is uploaded but not automatically associated with the AIAssistantMaterial.
            // To associate it, an Asset would need to be created first, then linked via AddAssetAsync.
            // This could be extended in the future to handle asset creation automatically.

            return result;
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
                UploadedAt = null // Asset doesn't have Created_at
            });
        }

        public async Task<bool> IsEndpointAvailableAsync(int aiAssistantMaterialId)
        {
            var aiAssistantMaterial = await _aiAssistantMaterialService.GetByIdAsync(aiAssistantMaterialId);
            if (aiAssistantMaterial == null)
            {
                return false;
            }

            // For now, use the default endpoint check
            return await IsDefaultEndpointAvailableAsync();
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
                    Markdown = apiResponse?.Response?.Markdown
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

        private class AIAssistantDocumentApiResponse
        {
            [JsonPropertyName("job_id")]
            public string? JobId { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }

            [JsonPropertyName("document_id")]
            public string? DocumentId { get; set; }
        }

        #endregion
    }
}
