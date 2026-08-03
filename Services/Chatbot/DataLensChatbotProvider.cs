namespace XR50TrainingAssetRepo.Services.Chatbot
{
    /// <summary>
    /// Adapts the existing DataLens client (<see cref="IChatbotApiService"/>) to the provider seam.
    /// This is a thin delegation layer that introduces no behaviour change to the DataLens stack —
    /// the AIAssistant material service still uses <see cref="IChatbotApiService"/> directly. It
    /// exists so DataLens is reachable through the same <see cref="IChatbotProvider"/> abstraction
    /// as INNOV, setting up future unification.
    ///
    /// DataLens reads its base URL and bearer token from global configuration, so the per-call
    /// <see cref="ChatbotConnection"/> is accepted for interface compatibility but ignored here.
    /// </summary>
    public sealed class DataLensChatbotProvider : IChatbotIngestionProvider
    {
        private readonly IChatbotApiService _chatbotApiService;

        public DataLensChatbotProvider(IChatbotApiService chatbotApiService)
        {
            _chatbotApiService = chatbotApiService;
        }

        public string ProviderKey => "datalens";

        public Task<bool> IsAvailableAsync(ChatbotConnection connection)
            => _chatbotApiService.IsAvailableAsync();

        public Task<bool> EnsureGroupingAsync(ChatbotConnection connection, string grouping)
            => _chatbotApiService.EnsureCollectionExistsAsync(grouping);

        public async Task<ChatbotIngestResult> IngestDocumentAsync(ChatbotIngestRequest request)
        {
            if (string.IsNullOrEmpty(request.SourceUrl))
            {
                // The DataLens client ingests by downloading from an asset URL; inline content
                // is not supported by this backend.
                throw new NotSupportedException("DataLens ingestion requires a downloadable SourceUrl.");
            }

            var jobId = await _chatbotApiService.SubmitDocumentAsync(
                assetId: 0,
                assetUrl: request.SourceUrl,
                filetype: request.Filetype ?? "pdf",
                collectionName: request.Grouping,
                documentName: request.FileName);

            return new ChatbotIngestResult
            {
                Status = "pending",
                JobId = jobId,
                CollectionName = request.Grouping
            };
        }

        public async Task<ChatbotIngestStatus> GetIngestStatusAsync(
            ChatbotConnection connection, string grouping, ChatbotDocumentRef document)
        {
            if (string.IsNullOrEmpty(document.JobId))
            {
                return new ChatbotIngestStatus { Status = "pending" };
            }

            var status = await _chatbotApiService.GetJobStatusAsync(document.JobId, grouping);
            return new ChatbotIngestStatus
            {
                Status = status.Status,
                ErrorMessage = status.Error
            };
        }
    }
}
