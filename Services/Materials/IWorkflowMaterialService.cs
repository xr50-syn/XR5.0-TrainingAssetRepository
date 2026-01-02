using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Workflow material-specific operations including steps.
    /// </summary>
    public interface IWorkflowMaterialService
    {
        // Workflow Material CRUD
        Task<IEnumerable<WorkflowMaterial>> GetAllAsync();
        Task<WorkflowMaterial?> GetByIdAsync(int id);
        Task<WorkflowMaterial?> GetWithStepsAsync(int id);
        Task<WorkflowMaterial> CreateAsync(WorkflowMaterial workflow);
        Task<WorkflowMaterial> CreateWithStepsAsync(WorkflowMaterial workflow, IEnumerable<WorkflowStep>? steps = null);
        Task<WorkflowMaterial> UpdateAsync(WorkflowMaterial workflow);
        Task<bool> DeleteAsync(int id);

        // Step Operations
        Task<WorkflowStep> AddStepAsync(int workflowId, WorkflowStep step);
        Task<bool> RemoveStepAsync(int workflowId, int stepId);
        Task<IEnumerable<WorkflowStep>> GetStepsAsync(int workflowId);
    }
}
