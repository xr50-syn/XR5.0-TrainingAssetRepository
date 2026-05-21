using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XR50TrainingAssetRepo.Services.Chatbot
{
    /// <summary>
    /// Native provider for the partner "XR5.0 LLM Engine API" (INNOV). Documents are ingested into
    /// a pilot via /api/upload and queried via /api/chat. Connection details (base URL + static
    /// bearer token) are resolved per tenant and supplied per call, so this provider builds its
    /// HttpClient through <see cref="IHttpClientFactory"/> rather than a constructor-configured client.
    /// </summary>
    public sealed class InnovChatbotProvider : IChatbotIngestionProvider, IChatbotChatProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<InnovChatbotProvider> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public InnovChatbotProvider(IHttpClientFactory httpClientFactory, ILogger<InnovChatbotProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string ProviderKey => "innov";

        private HttpClient CreateClient(ChatbotConnection connection, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(connection.BaseUrl))
            {
                throw new InvalidOperationException("INNOV chatbot base URL is not configured for this tenant.");
            }

            var client = _httpClientFactory.CreateClient("innov-chatbot");
            client.BaseAddress = new Uri(connection.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = timeout;

            if (!string.IsNullOrEmpty(connection.ApiToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", connection.ApiToken);
            }

            return client;
        }

        public async Task<bool> IsAvailableAsync(ChatbotConnection connection)
        {
            try
            {
                var client = CreateClient(connection, TimeSpan.FromSeconds(15));
                var response = await client.GetAsync("health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "INNOV chatbot health check failed - API may be unavailable");
                return false;
            }
        }

        // Pilots are provisioned and managed on the partner side; there is no create-grouping step
        // in the ingest flow, so this is a no-op.
        public Task<bool> EnsureGroupingAsync(ChatbotConnection connection, string grouping)
            => Task.FromResult(true);

        public async Task<ChatbotIngestResult> IngestDocumentAsync(ChatbotIngestRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Grouping))
            {
                throw new InvalidOperationException("INNOV ingestion requires a pilot (grouping).");
            }

            var client = CreateClient(request.Connection, TimeSpan.FromSeconds(120));

            // Obtain the bytes: inline content wins; otherwise download from the source URL.
            byte[] fileBytes;
            if (request.Content != null)
            {
                using var ms = new MemoryStream();
                await request.Content.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }
            else if (!string.IsNullOrEmpty(request.SourceUrl))
            {
                try
                {
                    var downloadClient = _httpClientFactory.CreateClient("innov-chatbot");
                    fileBytes = await downloadClient.GetByteArrayAsync(request.SourceUrl);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    throw new InvalidOperationException($"Could not download asset for INNOV ingestion: {ex.Message}", ex);
                }
            }
            else
            {
                throw new InvalidOperationException("INNOV ingestion requires either inline Content or a SourceUrl.");
            }

            using var content = new MultipartFormDataContent();

            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue(request.ContentType ?? "application/octet-stream");
            content.Add(fileContent, "uploaded_file", request.FileName);

            content.Add(new StringContent(request.DocType ?? request.Filetype ?? "manual"), "doc_type");
            content.Add(new StringContent(request.Manufacturer ?? string.Empty), "manufacturer");
            content.Add(new StringContent(request.DocTitle ?? request.FileName ?? string.Empty), "doc_title");
            content.Add(new StringContent(request.Grouping), "pilot");

            _logger.LogInformation("Uploading document {FileName} to INNOV pilot {Pilot}",
                request.FileName, request.Grouping);

            HttpResponseMessage response;
            string rawContent;
            try
            {
                response = await client.PostAsync("api/upload", content);
                rawContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "INNOV upload transport failure for pilot {Pilot}", request.Grouping);
                throw new InvalidOperationException($"INNOV chatbot backend is unreachable: {ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("INNOV upload failed for {FileName} in pilot {Pilot}: {StatusCode} - {Error}",
                    request.FileName, request.Grouping, response.StatusCode, rawContent);
                return new ChatbotIngestResult
                {
                    Status = "failed",
                    ErrorMessage = $"INNOV upload failed: {response.StatusCode}"
                };
            }

            var result = JsonSerializer.Deserialize<InnovUploadResponse>(rawContent, JsonOptions);

            return new ChatbotIngestResult
            {
                Status = MapProcessingStatus(result?.ProcessingStatus, result?.VectorCount),
                CollectionName = result?.CollectionName,
                JobId = null
            };
        }

        public async Task<ChatbotIngestStatus> GetIngestStatusAsync(
            ChatbotConnection connection, string grouping, ChatbotDocumentRef document)
        {
            try
            {
                var client = CreateClient(connection, TimeSpan.FromSeconds(30));
                var response = await client.GetAsync($"api/documents/pilot/{Uri.EscapeDataString(grouping)}");

                if (!response.IsSuccessStatusCode)
                {
                    // Can't confirm yet; treat as still processing rather than failing the material.
                    return new ChatbotIngestStatus { Status = "processing" };
                }

                var rawContent = await response.Content.ReadAsStringAsync();
                var pilot = JsonSerializer.Deserialize<InnovPilotDocuments>(rawContent, JsonOptions);

                var present = pilot?.Documents?.Any(d =>
                    (!string.IsNullOrEmpty(document.CollectionName) &&
                        string.Equals(d.CollectionName, document.CollectionName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(document.FileName) &&
                        string.Equals(d.FileName, document.FileName, StringComparison.OrdinalIgnoreCase))) ?? false;

                return new ChatbotIngestStatus { Status = present ? "completed" : "processing" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reconcile INNOV ingest status for pilot {Pilot}", grouping);
                return new ChatbotIngestStatus { Status = "processing" };
            }
        }

        public async Task<ChatbotChatResult> ChatAsync(ChatbotChatRequest request)
        {
            var client = CreateClient(request.Connection, TimeSpan.FromSeconds(60));

            var payload = new InnovChatRequestBody
            {
                Query = request.Query,
                Pilot = string.IsNullOrEmpty(request.Grouping) ? null : request.Grouping,
                ExpertiseLevel = request.ExpertiseLevel
            };

            _logger.LogInformation("Sending chat query to INNOV pilot {Pilot}", request.Grouping);

            HttpResponseMessage response;
            string rawContent;
            try
            {
                response = await client.PostAsJsonAsync("api/chat", payload, JsonOptions);
                rawContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "INNOV chat transport failure for pilot {Pilot}", request.Grouping);
                throw new InvalidOperationException($"INNOV chatbot backend is unreachable: {ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("INNOV chat failed for pilot {Pilot}: {StatusCode} - {Error}",
                    request.Grouping, response.StatusCode, rawContent);
                throw new InvalidOperationException($"INNOV chat failed: {response.StatusCode}");
            }

            var result = JsonSerializer.Deserialize<InnovChatResponseBody>(rawContent, JsonOptions);

            return new ChatbotChatResult
            {
                Text = result?.Response,
                Grouping = result?.Pilot ?? request.Grouping,
                TokensUsed = result?.TokensUsed,
                ProcessingTime = result?.ProcessingTime,
                Images = result?.Images?.Select(i => i.ImageUrl).Where(u => !string.IsNullOrEmpty(u)).Cast<string>().ToList()
                         ?? new List<string>(),
                Sources = result?.Documents?.Select(d => d.PdfUrl).Where(u => !string.IsNullOrEmpty(u)).Cast<string>().ToList()
                          ?? new List<string>()
            };
        }

        public async Task ClearHistoryAsync(ChatbotConnection connection, string grouping)
        {
            var client = CreateClient(connection, TimeSpan.FromSeconds(30));
            var url = string.IsNullOrEmpty(grouping)
                ? "api/chat/history"
                : $"api/chat/history?pilot={Uri.EscapeDataString(grouping)}";

            HttpResponseMessage response;
            try
            {
                response = await client.DeleteAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "INNOV clear-history transport failure for pilot {Pilot}", grouping);
                throw new InvalidOperationException($"INNOV chatbot backend is unreachable: {ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var rawContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("INNOV clear-history returned {StatusCode} for pilot {Pilot}: {Error}",
                    response.StatusCode, grouping, rawContent);
                throw new InvalidOperationException($"INNOV clear-history failed: {response.StatusCode}");
            }
        }

        // INNOV /api/upload is effectively synchronous and returns a processing_status. Map it to
        // the neutral vocabulary; a non-zero vector count is treated as a completed ingest.
        private static string MapProcessingStatus(string? processingStatus, int? vectorCount)
        {
            var s = processingStatus?.ToLowerInvariant() ?? string.Empty;

            if (s.Contains("fail") || s.Contains("error"))
            {
                return "failed";
            }
            if (s.Contains("process") || s.Contains("pending") || s.Contains("queue"))
            {
                return "processing";
            }
            if (s.Contains("complet") || s.Contains("success") || s.Contains("done") || s.Contains("ready"))
            {
                return "completed";
            }

            return vectorCount.GetValueOrDefault() > 0 ? "completed" : "processing";
        }

        #region INNOV wire models

        private sealed class InnovUploadResponse
        {
            [JsonPropertyName("collection_name")] public string? CollectionName { get; set; }
            [JsonPropertyName("pilot")] public string? Pilot { get; set; }
            [JsonPropertyName("vector_count")] public int? VectorCount { get; set; }
            [JsonPropertyName("processing_status")] public string? ProcessingStatus { get; set; }
            [JsonPropertyName("file_type")] public string? FileType { get; set; }
        }

        private sealed class InnovPilotDocuments
        {
            [JsonPropertyName("pilot")] public string? Pilot { get; set; }
            [JsonPropertyName("documents")] public List<InnovDocumentInfo>? Documents { get; set; }
        }

        private sealed class InnovDocumentInfo
        {
            [JsonPropertyName("collection_name")] public string? CollectionName { get; set; }
            [JsonPropertyName("file_name")] public string? FileName { get; set; }
            [JsonPropertyName("doc_title")] public string? DocTitle { get; set; }
        }

        private sealed class InnovChatRequestBody
        {
            [JsonPropertyName("query")] public string Query { get; set; } = string.Empty;
            [JsonPropertyName("pilot")] public string? Pilot { get; set; }
            [JsonPropertyName("expertise_level")] public string? ExpertiseLevel { get; set; }
        }

        private sealed class InnovChatResponseBody
        {
            [JsonPropertyName("response")] public string? Response { get; set; }
            [JsonPropertyName("pilot")] public string? Pilot { get; set; }
            [JsonPropertyName("processing_time")] public double? ProcessingTime { get; set; }
            [JsonPropertyName("tokens_used")] public int? TokensUsed { get; set; }
            [JsonPropertyName("images")] public List<InnovChatImage>? Images { get; set; }
            [JsonPropertyName("documents")] public List<InnovChatSource>? Documents { get; set; }
        }

        private sealed class InnovChatImage
        {
            [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
            [JsonPropertyName("filename")] public string? Filename { get; set; }
        }

        private sealed class InnovChatSource
        {
            [JsonPropertyName("collection_name")] public string? CollectionName { get; set; }
            [JsonPropertyName("doc_title")] public string? DocTitle { get; set; }
            [JsonPropertyName("pdf_url")] public string? PdfUrl { get; set; }
        }

        #endregion
    }
}
