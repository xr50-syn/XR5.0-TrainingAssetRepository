namespace XR50TrainingAssetRepo.Services.Chatbot
{
    /// <summary>
    /// Backend-neutral seam over an external RAG chatbot engine. Both the DataLens stack
    /// (via an adapter) and the INNOV "LLM Engine" implement this so future material types can
    /// pick a provider without each material service knowing the backend's protocol.
    ///
    /// "Grouping" is the backend's document bucket: a DataLens collection or an INNOV pilot.
    /// </summary>
    public interface IChatbotProvider
    {
        /// <summary>
        /// Stable key used to select a provider, e.g. "datalens" or "innov".
        /// </summary>
        string ProviderKey { get; }

        /// <summary>
        /// True if the backend is reachable.
        /// </summary>
        Task<bool> IsAvailableAsync(ChatbotConnection connection);
    }

    /// <summary>
    /// Providers that ingest documents into a grouping and report per-document processing status.
    /// </summary>
    public interface IChatbotIngestionProvider : IChatbotProvider
    {
        /// <summary>
        /// Ensures the grouping (collection/pilot) exists. Backends that manage groupings
        /// externally may treat this as a no-op returning true.
        /// </summary>
        Task<bool> EnsureGroupingAsync(ChatbotConnection connection, string grouping);

        /// <summary>
        /// Submits a single document for ingestion into the grouping.
        /// </summary>
        Task<ChatbotIngestResult> IngestDocumentAsync(ChatbotIngestRequest request);

        /// <summary>
        /// Reconciles the processing status of a previously ingested document.
        /// </summary>
        Task<ChatbotIngestStatus> GetIngestStatusAsync(ChatbotConnection connection, string grouping, ChatbotDocumentRef document);
    }

    /// <summary>
    /// Providers that answer conversational queries against a grouping.
    /// </summary>
    public interface IChatbotChatProvider : IChatbotProvider
    {
        /// <summary>
        /// Sends a query and returns the answer (text plus optional sources/images).
        /// </summary>
        Task<ChatbotChatResult> ChatAsync(ChatbotChatRequest request);

        /// <summary>
        /// Clears server-side conversation history for the grouping, where the backend keeps it.
        /// Backends without server-side history may treat this as a no-op.
        /// </summary>
        Task ClearHistoryAsync(ChatbotConnection connection, string grouping);
    }

    /// <summary>
    /// Connection details for a backend. For DataLens these come from global configuration and
    /// the per-call values may be ignored; for INNOV they are resolved per tenant.
    /// </summary>
    public sealed class ChatbotConnection
    {
        public string? BaseUrl { get; set; }

        /// <summary>Static bearer token. Treat as a secret: never log or echo it.</summary>
        public string? ApiToken { get; set; }
    }

    /// <summary>
    /// A single document to ingest. Content is supplied either inline (<see cref="Content"/>) or
    /// by reference to a downloadable URL (<see cref="SourceUrl"/>).
    /// </summary>
    public sealed class ChatbotIngestRequest
    {
        public ChatbotConnection Connection { get; set; } = new();
        public string Grouping { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public string? Filetype { get; set; }

        /// <summary>Inline content. Takes precedence over <see cref="SourceUrl"/> when set.</summary>
        public Stream? Content { get; set; }

        /// <summary>URL the provider can download the bytes from when <see cref="Content"/> is null.</summary>
        public string? SourceUrl { get; set; }

        // Optional ingestion metadata (used by backends that accept it, e.g. INNOV /api/upload).
        public string? DocType { get; set; }
        public string? Manufacturer { get; set; }
        public string? DocTitle { get; set; }
    }

    /// <summary>
    /// Identifies an ingested document for status reconciliation. Backends key on different
    /// fields (DataLens job id; INNOV collection/document name), so both are carried.
    /// </summary>
    public sealed class ChatbotDocumentRef
    {
        public string? JobId { get; set; }
        public string? CollectionName { get; set; }
        public string? FileName { get; set; }
    }

    /// <summary>
    /// Normalised ingestion status. Status values: "pending", "processing", "completed", "failed".
    /// </summary>
    public sealed class ChatbotIngestStatus
    {
        public string Status { get; set; } = "pending";
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result of an ingestion submission.
    /// </summary>
    public sealed class ChatbotIngestResult
    {
        /// <summary>Normalised status: "pending", "processing", "completed", "failed".</summary>
        public string Status { get; set; } = "pending";

        /// <summary>Backend job id, when the backend issues one (DataLens). Null for INNOV.</summary>
        public string? JobId { get; set; }

        /// <summary>Backend collection name for the ingested document, when returned (INNOV).</summary>
        public string? CollectionName { get; set; }

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// A conversational query against a grouping.
    /// </summary>
    public sealed class ChatbotChatRequest
    {
        public ChatbotConnection Connection { get; set; } = new();
        public string Grouping { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;

        /// <summary>Optional session id for backends that use client-managed sessions (DataLens).</summary>
        public string? SessionId { get; set; }

        /// <summary>Optional response adaptation level for backends that accept it (INNOV).</summary>
        public string? ExpertiseLevel { get; set; }
    }

    /// <summary>
    /// A conversational answer.
    /// </summary>
    public sealed class ChatbotChatResult
    {
        public string? Text { get; set; }
        public string? SessionId { get; set; }
        public string? Grouping { get; set; }
        public List<string> Sources { get; set; } = new();
        public List<string> Images { get; set; } = new();
        public int? TokensUsed { get; set; }
        public double? ProcessingTime { get; set; }
    }
}
