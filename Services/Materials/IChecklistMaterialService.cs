using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Checklist material-specific operations including entries.
    /// </summary>
    public interface IChecklistMaterialService
    {
        // Checklist Material CRUD
        Task<IEnumerable<ChecklistMaterial>> GetAllAsync();
        Task<ChecklistMaterial?> GetByIdAsync(int id);
        Task<ChecklistMaterial?> GetWithEntriesAsync(int id);
        Task<ChecklistMaterial> CreateAsync(ChecklistMaterial checklist);
        Task<ChecklistMaterial> CreateWithEntriesAsync(ChecklistMaterial checklist, IEnumerable<ChecklistEntry>? entries = null);
        Task<ChecklistMaterial> UpdateAsync(ChecklistMaterial checklist);
        Task<bool> DeleteAsync(int id);

        // Entry Operations
        Task<ChecklistEntry> AddEntryAsync(int checklistId, ChecklistEntry entry);
        Task<bool> RemoveEntryAsync(int checklistId, int entryId);
        Task<IEnumerable<ChecklistEntry>> GetEntriesAsync(int checklistId);
    }
}
