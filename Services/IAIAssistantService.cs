using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for interacting with the AI Assistant API (Siemens API wrapper).
    /// Provides document upload and conversational chat with audio responses.
    /// Sessions are automatically managed - first call appends filenames, subsequent calls use stored session.
    /// </summary>
    public interface IAIAssistantService
    {
        #region Default Endpoint Operations (no material required)

        /// <summary>
        /// Sends a query to the default AI assistant endpoint.
        /// </summary>
        /// <param name="query">The user's question</param>
        /// <param name="sessionId">Optional session ID for conversation continuity</param>
        /// <returns>The AI assistant's response including text and audio URL</returns>
        Task<AIAssistantAskResponse> AskAsync(string query, string? sessionId = null);

        /// <summary>
        /// Uploads a document to the default AI assistant for knowledge extraction.
        /// </summary>
        /// <param name="fileStream">The document file stream</param>
        /// <param name="fileName">The file name</param>
        /// <param name="contentType">The content type (e.g., application/pdf)</param>
        /// <returns>Upload response with job ID for tracking</returns>
        Task<AIAssistantDocumentUploadResponse> UploadDocumentAsync(Stream fileStream, string fileName, string contentType);

        /// <summary>
        /// Checks if the default AI assistant endpoint is available.
        /// </summary>
        /// <returns>True if the endpoint is reachable</returns>
        Task<bool> IsDefaultEndpointAvailableAsync();

        #endregion

        #region AIAssistantMaterial-specific Operations

        /// <summary>
        /// Sends a query to an AI assistant using AIAssistantMaterial configuration.
        /// On first call (no session), appends "using filenames a, b, c" to establish context.
        /// Stores returned session_id for subsequent calls.
        /// </summary>
        /// <param name="aiAssistantMaterialId">The ID of the AIAssistantMaterial</param>
        /// <param name="query">The user's question</param>
        /// <param name="sessionId">Optional session ID for conversation continuity (overrides stored session)</param>
        /// <returns>The AI assistant's response including text and audio URL</returns>
        Task<AIAssistantAskResponse> AskAsync(int aiAssistantMaterialId, string query, string? sessionId = null);

        /// <summary>
        /// Uploads a document to a specific AIAssistantMaterial for knowledge extraction.
        /// The document will be associated with the AIAssistantMaterial.
        /// </summary>
        /// <param name="aiAssistantMaterialId">The ID of the AIAssistantMaterial</param>
        /// <param name="fileStream">The document file stream</param>
        /// <param name="fileName">The file name</param>
        /// <param name="contentType">The content type (e.g., application/pdf)</param>
        /// <returns>Upload response with job ID for tracking</returns>
        Task<AIAssistantDocumentUploadResponse> UploadDocumentAsync(int aiAssistantMaterialId, Stream fileStream, string fileName, string contentType);

        /// <summary>
        /// Gets documents (assets) associated with an AIAssistantMaterial.
        /// </summary>
        /// <param name="aiAssistantMaterialId">The ID of the AIAssistantMaterial</param>
        /// <returns>List of document information</returns>
        Task<IEnumerable<AIAssistantDocumentInfo>> GetDocumentsAsync(int aiAssistantMaterialId);

        /// <summary>
        /// Checks if the AI assistant endpoint for an AIAssistantMaterial is available.
        /// </summary>
        /// <param name="aiAssistantMaterialId">The ID of the AIAssistantMaterial</param>
        /// <returns>True if the endpoint is reachable</returns>
        Task<bool> IsEndpointAvailableAsync(int aiAssistantMaterialId);

        #endregion
    }
}
