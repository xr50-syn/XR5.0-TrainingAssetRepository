namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for interacting with the Chatbot AI document processing API.
    /// </summary>
    public interface IChatbotApiService
    {
        /// <summary>
        /// Submits a document (asset) for AI processing.
        /// </summary>
        /// <param name="assetId">The asset ID to submit</param>
        /// <param name="assetUrl">The URL where the asset can be accessed</param>
        /// <param name="filetype">The file type/extension (e.g., "pdf", "docx")</param>
        /// <returns>The job ID from Chatbot API</returns>
        Task<string> SubmitDocumentAsync(int assetId, string assetUrl, string filetype);

        /// <summary>
        /// Gets the status of a submitted job.
        /// </summary>
        /// <param name="jobId">The Chatbot job ID</param>
        /// <returns>The current job status</returns>
        Task<ChatbotJobStatus> GetJobStatusAsync(string jobId);

        /// <summary>
        /// Checks if the Chatbot API is available.
        /// </summary>
        /// <returns>True if API is reachable</returns>
        Task<bool> IsAvailableAsync();
    }

    /// <summary>
    /// Represents the status of a Chatbot AI processing job.
    /// </summary>
    public class ChatbotJobStatus
    {
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Status values: "pending", "processing", "success", "failed"
        /// </summary>
        public string Status { get; set; } = "pending";

        /// <summary>
        /// Error message if Status is "failed"
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Progress percentage (0-100) if available
        /// </summary>
        public int? Progress { get; set; }

        /// <summary>
        /// When the job was created
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// When the job was last updated
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
