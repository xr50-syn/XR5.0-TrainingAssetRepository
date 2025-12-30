using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Implementation of Siemens AI document processing API integration.
    /// </summary>
    public class SiemensApiService : ISiemensApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SiemensApiService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _baseUrl;

        public SiemensApiService(
            HttpClient httpClient,
            ILogger<SiemensApiService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;

            _baseUrl = configuration["SiemensApi:BaseUrl"]
                ?? Environment.GetEnvironmentVariable("SIEMENS_API_BASE_URL")
                ?? "http://localhost:5001";

            var apiKey = configuration["SiemensApi:ApiKey"]
                ?? Environment.GetEnvironmentVariable("SIEMENS_API_KEY");

            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }

            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> SubmitDocumentAsync(int assetId, string assetUrl)
        {
            try
            {
                _logger.LogInformation("Submitting asset {AssetId} to Siemens API for processing", assetId);

                var request = new SiemensSubmitRequest
                {
                    AssetId = assetId,
                    DocumentUrl = assetUrl
                };

                var response = await _httpClient.PostAsJsonAsync("/api/document", request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Siemens API submission failed for asset {AssetId}: {StatusCode} - {Error}",
                        assetId, response.StatusCode, errorContent);
                    throw new SiemensApiException($"Failed to submit document: {response.StatusCode} - {errorContent}");
                }

                var result = await response.Content.ReadFromJsonAsync<SiemensSubmitResponse>();

                if (result == null || string.IsNullOrEmpty(result.JobId))
                {
                    throw new SiemensApiException("Invalid response from Siemens API: missing job ID");
                }

                _logger.LogInformation("Asset {AssetId} submitted successfully. Job ID: {JobId}", assetId, result.JobId);

                return result.JobId;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error submitting asset {AssetId} to Siemens API", assetId);
                throw new SiemensApiException($"Network error communicating with Siemens API: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout submitting asset {AssetId} to Siemens API", assetId);
                throw new SiemensApiException("Siemens API request timed out", ex);
            }
        }

        public async Task<SiemensJobStatus> GetJobStatusAsync(string jobId)
        {
            try
            {
                _logger.LogDebug("Checking status for Siemens job {JobId}", jobId);

                var response = await _httpClient.GetAsync($"/api/document/jobs/{jobId}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("Siemens job {JobId} not found", jobId);
                        return new SiemensJobStatus
                        {
                            JobId = jobId,
                            Status = "failed",
                            Error = "Job not found"
                        };
                    }

                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get status for job {JobId}: {StatusCode} - {Error}",
                        jobId, response.StatusCode, errorContent);
                    throw new SiemensApiException($"Failed to get job status: {response.StatusCode}");
                }

                var result = await response.Content.ReadFromJsonAsync<SiemensStatusResponse>();

                if (result == null)
                {
                    throw new SiemensApiException("Invalid response from Siemens API: null status");
                }

                return new SiemensJobStatus
                {
                    JobId = result.JobId ?? jobId,
                    Status = MapSiemensStatus(result.Status),
                    Error = result.Error,
                    Progress = result.Progress,
                    CreatedAt = result.CreatedAt,
                    UpdatedAt = result.UpdatedAt
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error getting status for job {JobId}", jobId);
                throw new SiemensApiException($"Network error communicating with Siemens API: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout getting status for job {JobId}", jobId);
                throw new SiemensApiException("Siemens API request timed out", ex);
            }
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Siemens API health check failed");
                return false;
            }
        }

        private static string MapSiemensStatus(string? siemensStatus)
        {
            return siemensStatus?.ToLowerInvariant() switch
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
    }

    #region Request/Response Models

    internal class SiemensSubmitRequest
    {
        [JsonPropertyName("asset_id")]
        public int AssetId { get; set; }

        [JsonPropertyName("document_url")]
        public string DocumentUrl { get; set; } = string.Empty;
    }

    internal class SiemensSubmitResponse
    {
        [JsonPropertyName("job_id")]
        public string? JobId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    internal class SiemensStatusResponse
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

    public class SiemensApiException : Exception
    {
        public SiemensApiException(string message) : base(message) { }
        public SiemensApiException(string message, Exception innerException) : base(message, innerException) { }
    }

    #endregion
}
