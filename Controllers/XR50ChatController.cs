using Microsoft.AspNetCore.Mvc;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "chat")]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly IMaterialService _materialService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(
            IChatService chatService,
            IMaterialService materialService,
            ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _materialService = materialService;
            _logger = logger;
        }

        /// <summary>
        /// Sends a query to a chatbot and returns the response.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="chatbotId">The ID of the ChatbotMaterial to use</param>
        /// <param name="request">The chat request containing the query and optional session ID</param>
        /// <returns>The chatbot's response</returns>
        [HttpPost("{chatbotId}/ask")]
        public async Task<ActionResult<ChatAskResponse>> AskChatbot(
            string tenantName,
            int chatbotId,
            [FromBody] ChatAskRequest request)
        {
            _logger.LogInformation("Chat request for chatbot {ChatbotId} in tenant {TenantName}: {Query}",
                chatbotId, tenantName, request.Query);

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new { Error = "Query cannot be empty" });
            }

            try
            {
                var response = await _chatService.AskAsync(chatbotId, request.Query, request.SessionId);

                _logger.LogInformation("Chat response received for chatbot {ChatbotId}, session {SessionId}",
                    chatbotId, response.SessionId);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning("Chatbot {ChatbotId} not found in tenant {TenantName}", chatbotId, tenantName);
                return NotFound(new { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Chat operation failed for chatbot {ChatbotId}", chatbotId);
                return BadRequest(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in chat for chatbot {ChatbotId}", chatbotId);
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Sends a query to a chatbot using form data (alternative to JSON body).
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="chatbotId">The ID of the ChatbotMaterial to use</param>
        /// <param name="query">The question to ask</param>
        /// <param name="session_id">Optional session ID for conversation continuity</param>
        /// <returns>The chatbot's response</returns>
        [HttpPost("{chatbotId}/ask/form")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<ActionResult<ChatAskResponse>> AskChatbotForm(
            string tenantName,
            int chatbotId,
            [FromForm] string query,
            [FromForm] string? session_id = null)
        {
            return await AskChatbot(tenantName, chatbotId, new ChatAskRequest
            {
                Query = query,
                SessionId = session_id
            });
        }

        /// <summary>
        /// Gets all available chatbots for the tenant.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <returns>List of chatbot materials</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetChatbots(string tenantName)
        {
            _logger.LogInformation("Getting chatbots for tenant: {TenantName}", tenantName);

            var chatbots = await _materialService.GetAllChatbotMaterialsAsync();

            var result = chatbots.Select(c => new
            {
                id = c.id,
                name = c.Name,
                description = c.Description,
                model = c.ChatbotModel,
                hasEndpoint = !string.IsNullOrEmpty(c.ChatbotConfig),
                created_at = c.Created_at,
                updated_at = c.Updated_at
            });

            _logger.LogInformation("Found {Count} chatbots for tenant: {TenantName}", chatbots.Count(), tenantName);

            return Ok(result);
        }

        /// <summary>
        /// Gets a specific chatbot's details.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="chatbotId">The chatbot material ID</param>
        /// <returns>Chatbot details</returns>
        [HttpGet("{chatbotId}")]
        public async Task<ActionResult<object>> GetChatbot(string tenantName, int chatbotId)
        {
            _logger.LogInformation("Getting chatbot {ChatbotId} for tenant: {TenantName}", chatbotId, tenantName);

            var chatbot = await _materialService.GetChatbotMaterialAsync(chatbotId);
            if (chatbot == null)
            {
                _logger.LogWarning("Chatbot {ChatbotId} not found in tenant: {TenantName}", chatbotId, tenantName);
                return NotFound(new { Error = $"Chatbot with ID {chatbotId} not found" });
            }

            return Ok(new
            {
                id = chatbot.id,
                name = chatbot.Name,
                description = chatbot.Description,
                model = chatbot.ChatbotModel,
                prompt = chatbot.ChatbotPrompt,
                hasEndpoint = !string.IsNullOrEmpty(chatbot.ChatbotConfig),
                created_at = chatbot.Created_at,
                updated_at = chatbot.Updated_at
            });
        }

        /// <summary>
        /// Checks if a chatbot's endpoint is available.
        /// </summary>
        /// <param name="tenantName">The tenant name</param>
        /// <param name="chatbotId">The chatbot material ID</param>
        /// <returns>Health status of the chatbot endpoint</returns>
        [HttpGet("{chatbotId}/health")]
        public async Task<ActionResult<object>> CheckChatbotHealth(string tenantName, int chatbotId)
        {
            _logger.LogInformation("Checking health for chatbot {ChatbotId} in tenant: {TenantName}",
                chatbotId, tenantName);

            var isAvailable = await _chatService.IsEndpointAvailableAsync(chatbotId);

            return Ok(new
            {
                chatbotId,
                available = isAvailable,
                checkedAt = DateTime.UtcNow
            });
        }
    }
}
