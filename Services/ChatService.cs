using System.Text.Json;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for proxying chat requests to external chatbot APIs.
    /// </summary>
    public class ChatService : IChatService
    {
        private readonly HttpClient _httpClient;
        private readonly ISimpleMaterialService _simpleMaterialService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            HttpClient httpClient,
            ISimpleMaterialService simpleMaterialService,
            IConfiguration configuration,
            ILogger<ChatService> logger)
        {
            _httpClient = httpClient;
            _simpleMaterialService = simpleMaterialService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ChatAskResponse> AskAsync(int chatbotMaterialId, string query, string? sessionId = null)
        {
            var chatbot = await _simpleMaterialService.GetChatbotByIdAsync(chatbotMaterialId);
            if (chatbot == null)
            {
                throw new KeyNotFoundException($"ChatbotMaterial with ID {chatbotMaterialId} not found");
            }

            var endpoint = GetEndpointFromChatbot(chatbot.ChatbotConfig);
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidOperationException($"ChatbotMaterial {chatbotMaterialId} has no endpoint configured");
            }

            _logger.LogInformation("Sending query to chatbot {ChatbotId} at {Endpoint}", chatbotMaterialId, endpoint);

            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("query", query),
                new KeyValuePair<string, string>("session_id", sessionId ?? string.Empty)
            });

            var askUrl = endpoint.TrimEnd('/') + "/ask";

            try
            {
                var response = await _httpClient.PostAsync(askUrl, formContent);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("Received response from chatbot: {Response}",
                    responseContent.Length > 500 ? responseContent.Substring(0, 500) + "..." : responseContent);

                var chatResponse = JsonSerializer.Deserialize<ChatAskResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return chatResponse ?? new ChatAskResponse
                {
                    Query = query,
                    SessionId = sessionId ?? string.Empty
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to communicate with chatbot endpoint {Endpoint}", askUrl);
                throw new InvalidOperationException($"Failed to communicate with chatbot: {ex.Message}", ex);
            }
        }

        public async Task<ChatAskResponse> AskAsync(string query, string? sessionId = null)
        {
            var endpoint = _configuration.GetValue<string>("ChatbotApi:BaseUrl");
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidOperationException("No default chatbot endpoint configured");
            }

            _logger.LogInformation("Sending query to default chatbot at {Endpoint}", endpoint);

            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("query", query),
                new KeyValuePair<string, string>("session_id", sessionId ?? string.Empty)
            });

            var askUrl = endpoint.TrimEnd('/') + "/ask";

            try
            {
                var response = await _httpClient.PostAsync(askUrl, formContent);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("Received response from default chatbot: {Response}",
                    responseContent.Length > 500 ? responseContent.Substring(0, 500) + "..." : responseContent);

                var chatResponse = JsonSerializer.Deserialize<ChatAskResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return chatResponse ?? new ChatAskResponse
                {
                    Query = query,
                    SessionId = sessionId ?? string.Empty
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to communicate with default chatbot endpoint {Endpoint}", askUrl);
                throw new InvalidOperationException($"Failed to communicate with chatbot: {ex.Message}", ex);
            }
        }

        public async Task<bool> IsEndpointAvailableAsync(int chatbotMaterialId)
        {
            var chatbot = await _simpleMaterialService.GetChatbotByIdAsync(chatbotMaterialId);
            if (chatbot == null)
            {
                return false;
            }

            var endpoint = GetEndpointFromChatbot(chatbot.ChatbotConfig);
            if (string.IsNullOrEmpty(endpoint))
            {
                return false;
            }

            try
            {
                var response = await _httpClient.GetAsync(endpoint);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsDefaultEndpointAvailableAsync()
        {
            var endpoint = _configuration.GetValue<string>("ChatbotApi:BaseUrl");
            if (string.IsNullOrEmpty(endpoint))
            {
                return false;
            }

            try
            {
                var response = await _httpClient.GetAsync(endpoint);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts the endpoint URL from ChatbotConfig.
        /// Supports both plain URL strings and JSON config with an "endpoint" property.
        /// Falls back to default chatbot URL from configuration if not specified.
        /// </summary>
        private string? GetEndpointFromChatbot(string? chatbotConfig)
        {
            if (string.IsNullOrEmpty(chatbotConfig))
            {
                // Fall back to default chatbot URL from configuration
                return _configuration.GetValue<string>("Chatbot:DefaultEndpoint");
            }

            // Try to parse as JSON first
            if (chatbotConfig.TrimStart().StartsWith("{"))
            {
                try
                {
                    var config = JsonSerializer.Deserialize<JsonElement>(chatbotConfig);
                    if (config.TryGetProperty("endpoint", out var endpointProp) ||
                        config.TryGetProperty("Endpoint", out endpointProp))
                    {
                        return endpointProp.GetString();
                    }
                }
                catch (JsonException)
                {
                    // Not valid JSON, treat as plain URL
                }
            }

            // Treat as plain URL
            return chatbotConfig;
        }
    }
}
