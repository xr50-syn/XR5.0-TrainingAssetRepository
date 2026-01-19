using System.Text.Json;
using System.Text.Json.Serialization;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for interacting with the Voice Assistant API (Siemens API wrapper).
    /// Provides document upload and conversational chat with audio responses.
    /// </summary>
    public class VoiceAssistantService : IVoiceAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly IVoiceMaterialService _voiceMaterialService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<VoiceAssistantService> _logger;
        private readonly string _baseUrl;

        public VoiceAssistantService(
            HttpClient httpClient,
            IVoiceMaterialService voiceMaterialService,
            IConfiguration configuration,
            ILogger<VoiceAssistantService> logger)
        {
            _httpClient = httpClient;
            _voiceMaterialService = voiceMaterialService;
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

        public async Task<VoiceAskResponse> AskAsync(string query, string? sessionId = null)
        {
            _logger.LogInformation("Sending query to default voice assistant: {Query}", query);

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

                _logger.LogDebug("Received response from voice assistant: {Response}",
                    responseContent.Length > 500 ? responseContent.Substring(0, 500) + "..." : responseContent);

                return ParseAskResponse(responseContent, query, sessionId);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to communicate with voice assistant endpoint {Endpoint}", askUrl);
                throw new InvalidOperationException($"Failed to communicate with voice assistant: {ex.Message}", ex);
            }
        }

        public async Task<VoiceDocumentUploadResponse> UploadDocumentAsync(Stream fileStream, string fileName, string contentType)
        {
            _logger.LogInformation("Uploading document to voice assistant: {FileName}", fileName);

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
                    _logger.LogError("Voice assistant document upload failed: {StatusCode} - {Error}",
                        response.StatusCode, errorContent);
                    throw new InvalidOperationException($"Failed to upload document: {response.StatusCode} - {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<VoiceDocumentApiResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("Document uploaded successfully. Job ID: {JobId}", result?.JobId);

                return new VoiceDocumentUploadResponse
                {
                    JobId = result?.JobId,
                    Status = result?.Status ?? "pending",
                    Message = result?.Message ?? "Document submitted for processing",
                    DocumentId = result?.DocumentId
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error uploading document to voice assistant");
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

        #region VoiceMaterial-specific Operations

        public async Task<VoiceAskResponse> AskAsync(int voiceMaterialId, string query, string? sessionId = null)
        {
            var voiceMaterial = await _voiceMaterialService.GetByIdAsync(voiceMaterialId);
            if (voiceMaterial == null)
            {
                throw new KeyNotFoundException($"VoiceMaterial with ID {voiceMaterialId} not found");
            }

            _logger.LogInformation("Sending query to voice assistant for material {VoiceMaterialId}: {Query}",
                voiceMaterialId, query);

            // For now, use the default endpoint. In the future, VoiceMaterial could have
            // custom endpoint configuration like ChatbotMaterial.
            return await AskAsync(query, sessionId);
        }

        public async Task<VoiceDocumentUploadResponse> UploadDocumentAsync(int voiceMaterialId, Stream fileStream, string fileName, string contentType)
        {
            var voiceMaterial = await _voiceMaterialService.GetByIdAsync(voiceMaterialId);
            if (voiceMaterial == null)
            {
                throw new KeyNotFoundException($"VoiceMaterial with ID {voiceMaterialId} not found");
            }

            _logger.LogInformation("Uploading document for voice material {VoiceMaterialId}: {FileName}",
                voiceMaterialId, fileName);

            // Upload the document
            var result = await UploadDocumentAsync(fileStream, fileName, contentType);

            // Note: The document is uploaded but not automatically associated with the VoiceMaterial.
            // To associate it, an Asset would need to be created first, then linked via AddAssetAsync.
            // This could be extended in the future to handle asset creation automatically.

            return result;
        }

        public async Task<IEnumerable<VoiceDocumentInfo>> GetDocumentsAsync(int voiceMaterialId)
        {
            var voiceMaterial = await _voiceMaterialService.GetByIdAsync(voiceMaterialId);
            if (voiceMaterial == null)
            {
                throw new KeyNotFoundException($"VoiceMaterial with ID {voiceMaterialId} not found");
            }

            var assets = await _voiceMaterialService.GetAssetsAsync(voiceMaterialId);

            return assets.Select(a => new VoiceDocumentInfo
            {
                AssetId = a.Id,
                FileName = a.Filename,
                Status = a.AiAvailable ?? "notready",
                JobId = a.JobId,
                UploadedAt = null // Asset doesn't have Created_at
            });
        }

        public async Task<bool> IsEndpointAvailableAsync(int voiceMaterialId)
        {
            var voiceMaterial = await _voiceMaterialService.GetByIdAsync(voiceMaterialId);
            if (voiceMaterial == null)
            {
                return false;
            }

            // For now, use the default endpoint check
            return await IsDefaultEndpointAvailableAsync();
        }

        #endregion

        #region Response Parsing

        private VoiceAskResponse ParseAskResponse(string responseContent, string query, string? sessionId)
        {
            try
            {
                var apiResponse = JsonSerializer.Deserialize<VoiceApiResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new VoiceAskResponse
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
                _logger.LogWarning(ex, "Failed to parse voice assistant response, returning raw content");
                return new VoiceAskResponse
                {
                    SessionId = sessionId ?? string.Empty,
                    Query = query,
                    Text = responseContent
                };
            }
        }

        #endregion

        #region Internal Response Models

        private class VoiceApiResponse
        {
            [JsonPropertyName("session_id")]
            public string? SessionId { get; set; }

            [JsonPropertyName("query")]
            public string? Query { get; set; }

            [JsonPropertyName("response")]
            public VoiceApiResponseContent? Response { get; set; }

            [JsonPropertyName("reasoning")]
            public string? Reasoning { get; set; }

            [JsonPropertyName("sources")]
            public List<string>? Sources { get; set; }
        }

        private class VoiceApiResponseContent
        {
            [JsonPropertyName("speech")]
            public VoiceApiSpeechContent? Speech { get; set; }

            [JsonPropertyName("markdown")]
            public string? Markdown { get; set; }

            [JsonPropertyName("images")]
            public List<string>? Images { get; set; }
        }

        private class VoiceApiSpeechContent
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("link")]
            public string? Link { get; set; }
        }

        private class VoiceDocumentApiResponse
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
