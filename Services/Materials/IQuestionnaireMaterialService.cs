using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Questionnaire material-specific operations including entries.
    /// </summary>
    public interface IQuestionnaireMaterialService
    {
        // Questionnaire Material CRUD
        Task<IEnumerable<QuestionnaireMaterial>> GetAllAsync();
        Task<QuestionnaireMaterial?> GetByIdAsync(int id);
        Task<QuestionnaireMaterial?> GetWithEntriesAsync(int id);
        Task<QuestionnaireMaterial> CreateAsync(QuestionnaireMaterial questionnaire);
        Task<QuestionnaireMaterial> CreateWithEntriesAsync(QuestionnaireMaterial questionnaire, IEnumerable<QuestionnaireEntry>? entries = null);
        Task<QuestionnaireMaterial> UpdateAsync(QuestionnaireMaterial questionnaire);
        Task<bool> DeleteAsync(int id);

        // Entry Operations
        Task<QuestionnaireEntry> AddEntryAsync(int questionnaireId, QuestionnaireEntry entry);
        Task<bool> RemoveEntryAsync(int questionnaireId, int entryId);
        Task<IEnumerable<QuestionnaireEntry>> GetEntriesAsync(int questionnaireId);
    }
}
