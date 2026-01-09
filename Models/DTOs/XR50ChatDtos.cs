namespace XR50TrainingAssetRepo.Models.DTOs
{
    /// <summary>
    /// Request DTO for asking a question to a chatbot.
    /// </summary>
    public class ChatAskRequest
    {
        /// <summary>
        /// The query/question to ask the chatbot.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Optional session ID for conversation continuity.
        /// If not provided, the chatbot will treat this as a new conversation.
        /// </summary>
        public string? SessionId { get; set; }
    }

    /// <summary>
    /// Response DTO from the chatbot API.
    /// </summary>
    public class ChatAskResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public ChatResponseContent? Response { get; set; }
        public string? Reasoning { get; set; }
        public List<string>? Sources { get; set; }
    }

    /// <summary>
    /// The content portion of a chatbot response.
    /// </summary>
    public class ChatResponseContent
    {
        public ChatSpeechContent? Speech { get; set; }
        public string? Markdown { get; set; }
        public List<string>? Images { get; set; }
    }

    /// <summary>
    /// Speech/audio content from the chatbot.
    /// </summary>
    public class ChatSpeechContent
    {
        public string? Text { get; set; }
        public string? Link { get; set; }
    }
}
