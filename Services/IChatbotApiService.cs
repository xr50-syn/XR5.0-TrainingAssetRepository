namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for interacting with the DataLens AI document processing API (v1).
    /// All operations are scoped to a collection.
    /// </summary>
    public interface IChatbotApiService
    {
        /// <summary>
        /// Submits a document (asset) for AI processing in a specific collection.
        /// Automatically uses PUT if the document already exists.
        /// </summary>
        /// <param name="assetId">The asset ID to submit</param>
        /// <param name="assetUrl">The URL where the asset can be accessed</param>
        /// <param name="filetype">The file type/extension (e.g., "pdf", "docx")</param>
        /// <param name="collectionName">The DataLens collection name</param>
        /// <returns>The job ID from the API</returns>
        Task<string> SubmitDocumentAsync(int assetId, string assetUrl, string filetype, string collectionName);

        /// <summary>
        /// Gets the status of a submitted job within a collection.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <param name="collectionName">The DataLens collection name</param>
        /// <returns>The current job status</returns>
        Task<ChatbotJobStatus> GetJobStatusAsync(string jobId, string collectionName);

        /// <summary>
        /// Ensures a DataLens collection exists, creating it if necessary.
        /// </summary>
        /// <param name="collectionName">The collection name to ensure</param>
        /// <returns>True if the collection exists or was created</returns>
        Task<bool> EnsureCollectionExistsAsync(string collectionName);

        /// <summary>
        /// Checks if a document already exists in a collection.
        /// </summary>
        /// <param name="collectionName">The DataLens collection name</param>
        /// <param name="documentName">The document filename</param>
        /// <returns>True if the document exists</returns>
        Task<bool> DocumentExistsAsync(string collectionName, string documentName);

        /// <summary>
        /// Checks if the DataLens API is available.
        /// </summary>
        /// <returns>True if API is reachable</returns>
        Task<bool> IsAvailableAsync();
    }

    /// <summary>
    /// Represents the status of a DataLens document processing job.
    /// </summary>
    public class ChatbotJobStatus
    {
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Status values: "pending", "processing", "completed", "failed"
        /// </summary>
        public string Status { get; set; } = "pending";

        /// <summary>
        /// Error message if Status is "failed"
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The document filename
        /// </summary>
        public string? Document { get; set; }

        /// <summary>
        /// The collection this job belongs to
        /// </summary>
        public string? CollectionName { get; set; }

        /// <summary>
        /// When the job was created
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// When the job completed
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Position in the processing queue
        /// </summary>
        public int? QueuePosition { get; set; }

        /// <summary>
        /// Total jobs in the queue
        /// </summary>
        public int? TotalInQueue { get; set; }
    }
}
