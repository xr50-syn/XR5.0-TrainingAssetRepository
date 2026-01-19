using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for proxying chat requests to external chatbot APIs.
    /// </summary>
    public interface IChatService
    {
        /// <summary>
        /// Sends a query to the chatbot associated with a ChatbotMaterial.
        /// </summary>
        /// <param name="chatbotMaterialId">The ID of the ChatbotMaterial containing the endpoint configuration</param>
        /// <param name="query">The user's question</param>
        /// <param name="sessionId">Optional session ID for conversation continuity</param>
        /// <returns>The chatbot's response</returns>
        Task<ChatAskResponse> AskAsync(int chatbotMaterialId, string query, string? sessionId = null);

        /// <summary>
        /// Sends a query to the default chatbot endpoint.
        /// </summary>
        /// <param name="query">The user's question</param>
        /// <param name="sessionId">Optional session ID for conversation continuity</param>
        /// <returns>The chatbot's response</returns>
        Task<ChatAskResponse> AskAsync(string query, string? sessionId = null);

        /// <summary>
        /// Checks if the chatbot endpoint for a given material is available.
        /// </summary>
        /// <param name="chatbotMaterialId">The ID of the ChatbotMaterial</param>
        /// <returns>True if the endpoint is reachable</returns>
        Task<bool> IsEndpointAvailableAsync(int chatbotMaterialId);

        /// <summary>
        /// Checks if the default chatbot endpoint is available.
        /// </summary>
        /// <returns>True if the default endpoint is reachable</returns>
        Task<bool> IsDefaultEndpointAvailableAsync();
    }
}
