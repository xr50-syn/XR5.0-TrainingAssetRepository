namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Service for interacting with the Siemens AI document processing API.
    /// </summary>
    public interface ISiemensApiService
    {
        /// <summary>
        /// Submits a document (asset) for AI processing.
        /// </summary>
        /// <param name="assetId">The asset ID to submit</param>
        /// <param name="assetUrl">The URL where the asset can be accessed</param>
        /// <returns>The job ID from Siemens API</returns>
        Task<string> SubmitDocumentAsync(int assetId, string assetUrl);

        /// <summary>
        /// Gets the status of a submitted job.
        /// </summary>
        /// <param name="jobId">The Siemens job ID</param>
        /// <returns>The current job status</returns>
        Task<SiemensJobStatus> GetJobStatusAsync(string jobId);

        /// <summary>
        /// Checks if the Siemens API is available.
        /// </summary>
        /// <returns>True if API is reachable</returns>
        Task<bool> IsAvailableAsync();
    }

    /// <summary>
    /// Represents the status of a Siemens AI processing job.
    /// </summary>
    public class SiemensJobStatus
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
