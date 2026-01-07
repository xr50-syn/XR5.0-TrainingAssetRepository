using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Implementation of Chatbot AI document processing API integration.
    /// </summary>
    public class ChatbotApiService : IChatbotApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ChatbotApiService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _baseUrl;

        public ChatbotApiService(
            HttpClient httpClient,
            ILogger<ChatbotApiService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;

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
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> SubmitDocumentAsync(int assetId, string assetUrl, string filetype)
        {
            try
            {
                _logger.LogInformation("Submitting asset {AssetId} to Chatbot API for processing", assetId);

                // Download the file from the asset URL
                using var downloadClient = new HttpClient();
                var fileBytes = await downloadClient.GetByteArrayAsync(assetUrl);

                // Use filetype to determine content type and ensure filename has correct extension
                var contentType = GetContentTypeFromFiletype(filetype);
                var fileName = GetFileNameWithExtension(assetUrl, filetype);

                // Create multipart form-data content with the file
                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                content.Add(fileContent, "file", fileName);

                // Optionally include asset_id as form field
                content.Add(new StringContent(assetId.ToString()), "asset_id");

                var response = await _httpClient.PostAsync("document", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Chatbot API submission failed for asset {AssetId}: {StatusCode} - {Error}",
                        assetId, response.StatusCode, errorContent);
                    throw new ChatbotApiException($"Failed to submit document: {response.StatusCode} - {errorContent}");
                }

                var result = await response.Content.ReadFromJsonAsync<ChatbotSubmitResponse>();

                if (result == null || string.IsNullOrEmpty(result.JobId))
                {
                    throw new ChatbotApiException("Invalid response from Chatbot API: missing job ID");
                }

                _logger.LogInformation("Asset {AssetId} submitted successfully. Job ID: {JobId}", assetId, result.JobId);

                return result.JobId;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error submitting asset {AssetId} to Chatbot API", assetId);
                throw new ChatbotApiException($"Network error communicating with Chatbot API: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout submitting asset {AssetId} to Chatbot API", assetId);
                throw new ChatbotApiException("Chatbot API request timed out", ex);
            }
        }

        public async Task<ChatbotJobStatus> GetJobStatusAsync(string jobId)
        {
            try
            {
                _logger.LogDebug("Checking status for Chatbot job {JobId}", jobId);

                var response = await _httpClient.GetAsync($"document/jobs/{jobId}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("Chatbot job {JobId} not found", jobId);
                        return new ChatbotJobStatus
                        {
                            JobId = jobId,
                            Status = "failed",
                            Error = "Job not found"
                        };
                    }

                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get status for job {JobId}: {StatusCode} - {Error}",
                        jobId, response.StatusCode, errorContent);
                    throw new ChatbotApiException($"Failed to get job status: {response.StatusCode}");
                }

                var result = await response.Content.ReadFromJsonAsync<ChatbotStatusResponse>();

                if (result == null)
                {
                    throw new ChatbotApiException("Invalid response from Chatbot API: null status");
                }

                return new ChatbotJobStatus
                {
                    JobId = result.JobId ?? jobId,
                    Status = MapChatbotStatus(result.Status),
                    Error = result.Error,
                    Progress = result.Progress,
                    CreatedAt = result.CreatedAt,
                    UpdatedAt = result.UpdatedAt
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error getting status for job {JobId}", jobId);
                throw new ChatbotApiException($"Network error communicating with Chatbot API: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout getting status for job {JobId}", jobId);
                throw new ChatbotApiException("Chatbot API request timed out", ex);
            }
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Chatbot API health check failed");
                return false;
            }
        }

        private static string MapChatbotStatus(string? chatbotStatus)
        {
            return chatbotStatus?.ToLowerInvariant() switch
            {
                "pending" => "pending",
                "queued" => "pending",
                "processing" => "processing",
                "in_progress" => "processing",
                "success" => "success",
                "completed" => "success",
                "done" => "success",
                "failed" => "failed",
                "error" => "failed",
                _ => "pending"
            };
        }

        private static string GetFileNameWithExtension(string url, string filetype)
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "document";
            }

            // Ensure filename has the correct extension based on filetype
            var currentExtension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var expectedExtension = filetype.TrimStart('.').ToLowerInvariant();

            if (currentExtension != expectedExtension)
            {
                // Add or replace extension
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                fileName = $"{nameWithoutExt}.{expectedExtension}";
            }

            return fileName;
        }

        private static string GetContentTypeFromFiletype(string filetype)
        {
            var ext = filetype.TrimStart('.').ToLowerInvariant();
            return ext switch
            {
                "pdf" => "application/pdf",
                "doc" => "application/msword",
                "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "txt" => "text/plain",
                "html" => "text/html",
                "htm" => "text/html",
                "json" => "application/json",
                "xml" => "application/xml",
                _ => "application/octet-stream"
            };
        }
    }

    #region Request/Response Models

    internal class ChatbotSubmitRequest
    {
        [JsonPropertyName("asset_id")]
        public int AssetId { get; set; }

        [JsonPropertyName("document_url")]
        public string DocumentUrl { get; set; } = string.Empty;
    }

    internal class ChatbotSubmitResponse
    {
        [JsonPropertyName("job_id")]
        public string? JobId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    internal class ChatbotStatusResponse
    {
        [JsonPropertyName("job_id")]
        public string? JobId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("progress")]
        public int? Progress { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }

    #endregion

    #region Exception

    public class ChatbotApiException : Exception
    {
        public ChatbotApiException(string message) : base(message) { }
        public ChatbotApiException(string message, Exception innerException) : base(message, innerException) { }
    }

    #endregion
}
