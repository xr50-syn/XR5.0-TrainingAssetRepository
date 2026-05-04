using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Implementation of DataLens AI document processing API integration (v1).
    /// All document and job operations are scoped to a collection.
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
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> SubmitDocumentAsync(int assetId, string assetUrl, string filetype, string collectionName)
        {
            try
            {
                _logger.LogInformation("Submitting asset {AssetId} to collection {CollectionName} for processing",
                    assetId, collectionName);

                // Download the file from the asset URL
                using var downloadClient = new HttpClient();
                var fileBytes = await downloadClient.GetByteArrayAsync(assetUrl);

                var contentType = GetContentTypeFromFiletype(filetype);
                var fileName = GetFileNameWithExtension(assetUrl, filetype);

                // Check if document already exists to decide POST vs PUT
                var documentExists = await DocumentExistsAsync(collectionName, fileName);

                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                content.Add(fileContent, "file", fileName);

                HttpResponseMessage response;
                if (documentExists)
                {
                    // Update existing document with PUT
                    var updateUrl = $"api/v1/collections/{Uri.EscapeDataString(collectionName)}/documents/{Uri.EscapeDataString(fileName)}";
                    _logger.LogInformation("Document {FileName} already exists in collection {CollectionName}, updating via PUT",
                        fileName, collectionName);
                    response = await _httpClient.PutAsync(updateUrl, content);
                }
                else
                {
                    // Create new document with POST
                    var uploadUrl = $"api/v1/collections/{Uri.EscapeDataString(collectionName)}/documents";
                    response = await _httpClient.PostAsync(uploadUrl, content);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Document submission failed for asset {AssetId} in collection {CollectionName}: {StatusCode} - {Error}",
                        assetId, collectionName, response.StatusCode, errorContent);
                    throw new ChatbotApiException($"Failed to submit document: {response.StatusCode} - {errorContent}");
                }

                var result = await response.Content.ReadFromJsonAsync<DataLensDocumentUploadResponse>();

                if (result == null || string.IsNullOrEmpty(result.JobId))
                {
                    throw new ChatbotApiException("Invalid response from DataLens API: missing job ID");
                }

                _logger.LogInformation("Asset {AssetId} submitted successfully to collection {CollectionName}. Job ID: {JobId}",
                    assetId, collectionName, result.JobId);

                return result.JobId;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error submitting asset {AssetId} to collection {CollectionName}",
                    assetId, collectionName);
                throw new ChatbotApiException($"Network error communicating with DataLens API: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout submitting asset {AssetId} to collection {CollectionName}",
                    assetId, collectionName);
                throw new ChatbotApiException("DataLens API request timed out", ex);
            }
        }

        public async Task<ChatbotJobStatus> GetJobStatusAsync(string jobId, string collectionName)
        {
            try
            {
                var statusUrl = $"api/v1/collections/{Uri.EscapeDataString(collectionName)}/jobs/{Uri.EscapeDataString(jobId)}";
                _logger.LogInformation("Checking status for job {JobId} in collection {CollectionName}",
                    jobId, collectionName);

                var response = await _httpClient.GetAsync(statusUrl);
                var rawContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Job {JobId} status response: {StatusCode} - {RawContent}",
                    jobId, response.StatusCode, rawContent);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("Job {JobId} not found in collection {CollectionName}",
                            jobId, collectionName);
                        return new ChatbotJobStatus
                        {
                            JobId = jobId,
                            Status = "failed",
                            Error = "Job not found",
                            CollectionName = collectionName
                        };
                    }

                    _logger.LogError("Failed to get status for job {JobId} in collection {CollectionName}: {StatusCode} - {Error}",
                        jobId, collectionName, response.StatusCode, rawContent);
                    throw new ChatbotApiException($"Failed to get job status: {response.StatusCode}");
                }

                var result = JsonSerializer.Deserialize<DataLensJobStatusResponse>(rawContent);

                if (result == null)
                {
                    _logger.LogError("Failed to deserialize job status response for {JobId}: {RawContent}",
                        jobId, rawContent);
                    throw new ChatbotApiException("Invalid response from DataLens API: null status");
                }

                var mappedStatus = MapStatus(result.Status);
                _logger.LogInformation("Job {JobId} raw status: '{RawStatus}' mapped to: '{MappedStatus}'",
                    jobId, result.Status, mappedStatus);

                return new ChatbotJobStatus
                {
                    JobId = result.JobId ?? jobId,
                    Status = mappedStatus,
                    Error = result.Error,
                    Document = result.Document,
                    CollectionName = result.CollectionName ?? collectionName,
                    CreatedAt = result.CreatedAt,
                    CompletedAt = result.CompletedAt,
                    QueuePosition = result.QueuePosition,
                    TotalInQueue = result.TotalInQueue
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error getting status for job {JobId} in collection {CollectionName}",
                    jobId, collectionName);
                throw new ChatbotApiException($"Network error communicating with DataLens API: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout getting status for job {JobId} in collection {CollectionName}",
                    jobId, collectionName);
                throw new ChatbotApiException("DataLens API request timed out", ex);
            }
        }

        public async Task<bool> EnsureCollectionExistsAsync(string collectionName)
        {
            try
            {
                _logger.LogInformation("Ensuring collection {CollectionName} exists", collectionName);

                var getUrl = $"api/v1/collections/{Uri.EscapeDataString(collectionName)}";
                var response = await _httpClient.GetAsync(getUrl);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Collection {CollectionName} already exists", collectionName);
                    return true;
                }

                if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Unexpected response checking collection {CollectionName}: {StatusCode} - {Error}",
                        collectionName, response.StatusCode, errorContent);
                    throw new ChatbotApiException($"Failed to check collection: {response.StatusCode}");
                }

                // Collection doesn't exist, create it
                _logger.LogInformation("Creating collection {CollectionName}", collectionName);

                var createRequest = new { name = collectionName };
                var createResponse = await _httpClient.PostAsJsonAsync("api/v1/collections", createRequest);

                if (!createResponse.IsSuccessStatusCode)
                {
                    var errorContent = await createResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to create collection {CollectionName}: {StatusCode} - {Error}",
                        collectionName, createResponse.StatusCode, errorContent);
                    throw new ChatbotApiException($"Failed to create collection: {createResponse.StatusCode} - {errorContent}");
                }

                _logger.LogInformation("Collection {CollectionName} created successfully", collectionName);
                return true;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error ensuring collection {CollectionName}", collectionName);
                throw new ChatbotApiException($"Network error communicating with DataLens API: {ex.Message}", ex);
            }
        }

        public async Task<bool> DocumentExistsAsync(string collectionName, string documentName)
        {
            try
            {
                var url = $"api/v1/collections/{Uri.EscapeDataString(collectionName)}/documents/{Uri.EscapeDataString(documentName)}";
                var response = await _httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking document existence for {DocumentName} in {CollectionName}, assuming not exists",
                    documentName, collectionName);
                return false;
            }
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                _logger.LogInformation("Checking DataLens API availability at: {BaseAddress}health",
                    _httpClient.BaseAddress);
                var response = await _httpClient.GetAsync("health");
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("DataLens API health check response: {StatusCode} - {Content}",
                    response.StatusCode, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DataLens API health check failed - API may be unavailable");
                return false;
            }
        }

        private static string MapStatus(string? status)
        {
            return status?.ToLowerInvariant() switch
            {
                "pending" => "pending",
                "queued" => "pending",
                "processing" => "processing",
                "in_progress" => "processing",
                "completed" => "completed",
                "success" => "completed",
                "done" => "completed",
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

            var currentExtension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var expectedExtension = filetype.TrimStart('.').ToLowerInvariant();

            if (currentExtension != expectedExtension)
            {
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

    internal class DataLensDocumentUploadResponse
    {
        [JsonPropertyName("job_id")]
        public string? JobId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("document")]
        public string? Document { get; set; }

        [JsonPropertyName("collection_name")]
        public string? CollectionName { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("queue_position")]
        public int? QueuePosition { get; set; }
    }

    internal class DataLensJobStatusResponse
    {
        [JsonPropertyName("job_id")]
        public string? JobId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("document")]
        public string? Document { get; set; }

        [JsonPropertyName("collection_name")]
        public string? CollectionName { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [JsonPropertyName("result_path")]
        public string? ResultPath { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("queue_position")]
        public int? QueuePosition { get; set; }

        [JsonPropertyName("total_in_queue")]
        public int? TotalInQueue { get; set; }
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
