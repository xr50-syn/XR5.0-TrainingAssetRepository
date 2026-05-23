using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service backing the generic Chat API.
    ///
    /// The configured chatbot backend is DataLens, whose query endpoint is
    /// /api/v1/collections/{collection}/inferences. The legacy root /ask path this service used to
    /// POST to no longer exists on DataLens (it returned 500/404), so chat requests are now routed
    /// through the AIAssistant inference path against the tenant's DefaultAICollection. That reuses
    /// DataLens bearer auth, collection resolution, and tolerant response parsing (no unhandled
    /// JsonException -> 500).
    ///
    /// Note: DataLens has no per-ChatbotMaterial collection, so material-scoped chat also targets
    /// the tenant default collection. If per-bot collections are needed later, add a collection
    /// field to ChatbotMaterial and pass it through.
    /// </summary>
    public class ChatService : IChatService
    {
        private readonly IAIAssistantService _aiAssistantService;
        private readonly ISimpleMaterialService _simpleMaterialService;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            IAIAssistantService aiAssistantService,
            ISimpleMaterialService simpleMaterialService,
            ILogger<ChatService> logger)
        {
            _aiAssistantService = aiAssistantService;
            _simpleMaterialService = simpleMaterialService;
            _logger = logger;
        }

        public async Task<ChatAskResponse> AskAsync(int chatbotMaterialId, string query, string? sessionId = null)
        {
            var chatbot = await _simpleMaterialService.GetChatbotByIdAsync(chatbotMaterialId);
            if (chatbot == null)
            {
                throw new KeyNotFoundException($"ChatbotMaterial with ID {chatbotMaterialId} not found");
            }

            _logger.LogInformation("Routing chat for chatbot {ChatbotId} to DataLens inferences", chatbotMaterialId);
            var response = await _aiAssistantService.AskAsync(query, sessionId);
            return MapFromAIAssistant(response);
        }

        public async Task<ChatAskResponse> AskAsync(string query, string? sessionId = null)
        {
            _logger.LogInformation("Routing default chat to DataLens inferences");
            var response = await _aiAssistantService.AskAsync(query, sessionId);
            return MapFromAIAssistant(response);
        }

        public async Task<bool> IsEndpointAvailableAsync(int chatbotMaterialId)
        {
            var chatbot = await _simpleMaterialService.GetChatbotByIdAsync(chatbotMaterialId);
            if (chatbot == null)
            {
                return false;
            }
            return await _aiAssistantService.IsDefaultEndpointAvailableAsync();
        }

        public Task<bool> IsDefaultEndpointAvailableAsync()
            => _aiAssistantService.IsDefaultEndpointAvailableAsync();

        // Map the AIAssistant (DataLens) response shape onto the Chat API response shape.
        private static ChatAskResponse MapFromAIAssistant(AIAssistantAskResponse r)
        {
            return new ChatAskResponse
            {
                SessionId = r.SessionId,
                Query = r.Query,
                Response = new ChatResponseContent
                {
                    Speech = new ChatSpeechContent { Text = r.Text, Link = r.AudioUrl },
                    Markdown = r.Markdown
                },
                Sources = r.Sources
            };
        }
    }
}
