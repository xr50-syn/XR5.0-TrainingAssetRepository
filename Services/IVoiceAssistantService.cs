using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for interacting with the Voice Assistant API (Siemens API wrapper).
    /// Provides document upload and conversational chat with audio responses.
    /// </summary>
    public interface IVoiceAssistantService
    {
        #region Default Endpoint Operations (no material required)

        /// <summary>
        /// Sends a query to the default voice assistant endpoint.
        /// </summary>
        /// <param name="query">The user's question</param>
        /// <param name="sessionId">Optional session ID for conversation continuity</param>
        /// <returns>The voice assistant's response including text and audio URL</returns>
        Task<VoiceAskResponse> AskAsync(string query, string? sessionId = null);

        /// <summary>
        /// Uploads a document to the default voice assistant for knowledge extraction.
        /// </summary>
        /// <param name="fileStream">The document file stream</param>
        /// <param name="fileName">The file name</param>
        /// <param name="contentType">The content type (e.g., application/pdf)</param>
        /// <returns>Upload response with job ID for tracking</returns>
        Task<VoiceDocumentUploadResponse> UploadDocumentAsync(Stream fileStream, string fileName, string contentType);

        /// <summary>
        /// Checks if the default voice assistant endpoint is available.
        /// </summary>
        /// <returns>True if the endpoint is reachable</returns>
        Task<bool> IsDefaultEndpointAvailableAsync();

        #endregion

        #region VoiceMaterial-specific Operations

        /// <summary>
        /// Sends a query to a voice assistant using VoiceMaterial configuration.
        /// </summary>
        /// <param name="voiceMaterialId">The ID of the VoiceMaterial</param>
        /// <param name="query">The user's question</param>
        /// <param name="sessionId">Optional session ID for conversation continuity</param>
        /// <returns>The voice assistant's response including text and audio URL</returns>
        Task<VoiceAskResponse> AskAsync(int voiceMaterialId, string query, string? sessionId = null);

        /// <summary>
        /// Uploads a document to a specific VoiceMaterial for knowledge extraction.
        /// The document will be associated with the VoiceMaterial.
        /// </summary>
        /// <param name="voiceMaterialId">The ID of the VoiceMaterial</param>
        /// <param name="fileStream">The document file stream</param>
        /// <param name="fileName">The file name</param>
        /// <param name="contentType">The content type (e.g., application/pdf)</param>
        /// <returns>Upload response with job ID for tracking</returns>
        Task<VoiceDocumentUploadResponse> UploadDocumentAsync(int voiceMaterialId, Stream fileStream, string fileName, string contentType);

        /// <summary>
        /// Gets documents (assets) associated with a VoiceMaterial.
        /// </summary>
        /// <param name="voiceMaterialId">The ID of the VoiceMaterial</param>
        /// <returns>List of document information</returns>
        Task<IEnumerable<VoiceDocumentInfo>> GetDocumentsAsync(int voiceMaterialId);

        /// <summary>
        /// Checks if the voice assistant endpoint for a VoiceMaterial is available.
        /// </summary>
        /// <param name="voiceMaterialId">The ID of the VoiceMaterial</param>
        /// <returns>True if the endpoint is reachable</returns>
        Task<bool> IsEndpointAvailableAsync(int voiceMaterialId);

        #endregion
    }
}
