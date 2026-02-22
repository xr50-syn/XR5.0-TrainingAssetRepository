using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Workflow material-specific operations including steps.
    /// </summary>
    public class WorkflowMaterialService : IWorkflowMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<WorkflowMaterialService> _logger;

        public WorkflowMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<WorkflowMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Workflow Material CRUD

        public async Task<IEnumerable<WorkflowMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<WorkflowMaterial>()
                .ToListAsync();
        }

        public async Task<WorkflowMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<WorkflowMaterial>()
                .FirstOrDefaultAsync(w => w.id == id);
        }

        public async Task<WorkflowMaterial?> GetWithStepsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<WorkflowMaterial>()
                .Include(w => w.WorkflowSteps)
                .FirstOrDefaultAsync(w => w.id == id);
        }

        public async Task<WorkflowMaterial> CreateAsync(WorkflowMaterial workflow)
        {
            using var context = _dbContextFactory.CreateDbContext();

            workflow.Created_at = DateTime.UtcNow;
            workflow.Updated_at = DateTime.UtcNow;
            workflow.Type = MaterialType.Workflow;

            context.Materials.Add(workflow);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created workflow material: {Name} with ID: {Id}", workflow.Name, workflow.id);

            return workflow;
        }

        public async Task<WorkflowMaterial> CreateWithStepsAsync(WorkflowMaterial workflow, IEnumerable<WorkflowStep>? steps = null)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                workflow.Created_at = DateTime.UtcNow;
                workflow.Updated_at = DateTime.UtcNow;
                workflow.Type = MaterialType.Workflow;

                context.Materials.Add(workflow);
                await context.SaveChangesAsync();

                if (steps != null && steps.Any())
                {
                    foreach (var step in steps)
                    {
                        step.Id = 0; // Reset ID for new record
                        step.WorkflowMaterialId = workflow.id;
                        context.WorkflowSteps.Add(step);
                    }
                    await context.SaveChangesAsync();

                    _logger.LogInformation("Added {StepCount} initial steps to workflow {WorkflowId}",
                        steps.Count(), workflow.id);
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Created workflow material: {Name} with ID: {Id}", workflow.Name, workflow.id);

                return workflow;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create workflow material {Name} - Transaction rolled back", workflow.Name);
                throw;
            }
        }

        public async Task<WorkflowMaterial> UpdateAsync(WorkflowMaterial workflow)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var existing = await context.Materials
                    .OfType<WorkflowMaterial>()
                    .FirstOrDefaultAsync(w => w.id == workflow.id);

                if (existing == null)
                {
                    throw new KeyNotFoundException($"Workflow material {workflow.id} not found");
                }

                // Preserve original values
                var createdAt = existing.Created_at;
                var uniqueId = existing.Unique_id;

                // Delete existing steps
                var existingSteps = await context.WorkflowSteps
                    .Where(s => s.WorkflowMaterialId == workflow.id)
                    .ToListAsync();
                context.WorkflowSteps.RemoveRange(existingSteps);

                // Remove and re-add the material
                context.Materials.Remove(existing);
                await context.SaveChangesAsync();

                workflow.id = existing.id;
                workflow.Created_at = createdAt;
                workflow.Updated_at = DateTime.UtcNow;
                workflow.Unique_id = uniqueId;
                workflow.Type = MaterialType.Workflow;

                context.Materials.Add(workflow);
                await context.SaveChangesAsync();

                // Re-add steps if present
                if (workflow.WorkflowSteps?.Any() == true)
                {
                    foreach (var step in workflow.WorkflowSteps.ToList())
                    {
                        var newStep = new WorkflowStep
                        {
                            Title = step.Title,
                            Content = step.Content,
                            WorkflowMaterialId = workflow.id
                        };
                        context.WorkflowSteps.Add(newStep);
                    }
                    await context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Updated workflow material: {Id} ({Name})", workflow.id, workflow.Name);

                return await GetWithStepsAsync(workflow.id) ?? workflow;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update workflow material {Id}", workflow.id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var workflow = await context.Materials
                .OfType<WorkflowMaterial>()
                .FirstOrDefaultAsync(w => w.id == id);

            if (workflow == null)
            {
                return false;
            }

            // Delete steps
            var steps = await context.WorkflowSteps
                .Where(s => s.WorkflowMaterialId == id)
                .ToListAsync();
            context.WorkflowSteps.RemoveRange(steps);

            // Delete material relationships
            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            context.Materials.Remove(workflow);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted workflow material: {Id} with {StepCount} steps and {RelationshipCount} relationships",
                id, steps.Count, relationships.Count);

            return true;
        }

        #endregion

        #region Step Operations

        public async Task<WorkflowStep> AddStepAsync(int workflowId, WorkflowStep step)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var workflow = await context.Materials
                .OfType<WorkflowMaterial>()
                .FirstOrDefaultAsync(w => w.id == workflowId);

            if (workflow == null)
            {
                throw new ArgumentException($"Workflow material with ID {workflowId} not found");
            }

            step.Id = 0;
            step.WorkflowMaterialId = workflowId;

            context.WorkflowSteps.Add(step);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added step '{Title}' to workflow material {WorkflowId}",
                step.Title, workflowId);

            return step;
        }

        public async Task<bool> RemoveStepAsync(int workflowId, int stepId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var step = await context.WorkflowSteps.FindAsync(stepId);
            if (step == null || step.WorkflowMaterialId != workflowId)
            {
                return false;
            }

            var orphanedRelationships = await context.SubcomponentMaterialRelationships
                .Where(smr => smr.SubcomponentId == stepId && smr.SubcomponentType == "WorkflowStep")
                .ToListAsync();
            if (orphanedRelationships.Count > 0)
                context.SubcomponentMaterialRelationships.RemoveRange(orphanedRelationships);

            context.WorkflowSteps.Remove(step);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed step {StepId} from workflow material {WorkflowId}",
                stepId, workflowId);

            return true;
        }

        public async Task<IEnumerable<WorkflowStep>> GetStepsAsync(int workflowId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.WorkflowSteps
                .Where(s => s.WorkflowMaterialId == workflowId)
                .ToListAsync();
        }

        #endregion
    }
}
