namespace XR50TrainingAssetRepo.Models.DTOs
{
    /// <summary>
    /// Request DTO for asking a question to the AI assistant.
    /// </summary>
    public class AIAssistantAskRequest
    {
        /// <summary>
        /// The query/question to ask the AI assistant.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Optional session ID for conversation continuity.
        /// </summary>
        public string? SessionId { get; set; }
    }

    /// <summary>
    /// Response DTO from the AI assistant API.
    /// </summary>
    public class AIAssistantAskResponse
    {
        /// <summary>
        /// Session ID for conversation continuity.
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// The original query.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Text response from the assistant.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// URL to the WAV audio file.
        /// </summary>
        public string? AudioUrl { get; set; }

        /// <summary>
        /// Sources used to generate the response (if available).
        /// </summary>
        public List<string>? Sources { get; set; }

        /// <summary>
        /// Additional markdown content (if available).
        /// </summary>
        public string? Markdown { get; set; }
    }

    /// <summary>
    /// Response DTO for document upload.
    /// </summary>
    public class AIAssistantDocumentUploadResponse
    {
        /// <summary>
        /// Job ID for tracking the document processing.
        /// </summary>
        public string? JobId { get; set; }

        /// <summary>
        /// Processing status.
        /// </summary>
        public string Status { get; set; } = "pending";

        /// <summary>
        /// Message from the API.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Document ID if assigned.
        /// </summary>
        public string? DocumentId { get; set; }
    }

    /// <summary>
    /// DTO for document information.
    /// </summary>
    public class AIAssistantDocumentInfo
    {
        public int AssetId { get; set; }
        public string? FileName { get; set; }
        public string? Status { get; set; }
        public string? JobId { get; set; }
        public DateTime? UploadedAt { get; set; }
    }
}
