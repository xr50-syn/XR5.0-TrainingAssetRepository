namespace XR50TrainingAssetRepo.Models.DTOs
{
    /// <summary>
    /// Request DTO for asking a question to an INNOV chatbot material.
    /// </summary>
    public class InnovChatRequest
    {
        /// <summary>
        /// The query/question to ask.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Optional response adaptation level: "beginner", "intermediate", or "expert".
        /// Falls back to the material's configured ExpertiseLevel when omitted.
        /// </summary>
        public string? ExpertiseLevel { get; set; }
    }

    /// <summary>
    /// Response DTO from the INNOV chat API.
    /// </summary>
    public class InnovChatResponse
    {
        /// <summary>
        /// The original query.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// The AI response text.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// The INNOV pilot the query ran against.
        /// </summary>
        public string? Pilot { get; set; }

        /// <summary>
        /// Source document URLs referenced in the answer (if any).
        /// </summary>
        public List<string> Sources { get; set; } = new();

        /// <summary>
        /// Image URLs returned with the answer (if any).
        /// </summary>
        public List<string> Images { get; set; } = new();

        /// <summary>
        /// Number of tokens used (if reported).
        /// </summary>
        public int? TokensUsed { get; set; }

        /// <summary>
        /// Processing time in seconds (if reported).
        /// </summary>
        public double? ProcessingTime { get; set; }
    }

    /// <summary>
    /// Response DTO for an INNOV document upload.
    /// </summary>
    public class InnovDocumentUploadResponse
    {
        /// <summary>
        /// Normalised processing status: "pending", "processing", "completed", "failed".
        /// </summary>
        public string Status { get; set; } = "pending";

        /// <summary>
        /// Message from the API.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// The INNOV collection name assigned to the uploaded document, if returned.
        /// </summary>
        public string? CollectionName { get; set; }

        /// <summary>
        /// The INNOV pilot the document was uploaded to.
        /// </summary>
        public string? Pilot { get; set; }
    }

    /// <summary>
    /// DTO for INNOV document (asset) information.
    /// </summary>
    public class InnovChatbotDocumentInfo
    {
        public int AssetId { get; set; }
        public string? FileName { get; set; }
        public string? Status { get; set; }
        public string? Pilot { get; set; }
        public string? CollectionName { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
