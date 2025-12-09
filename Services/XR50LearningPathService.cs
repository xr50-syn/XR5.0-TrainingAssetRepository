using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Data;

namespace XR50TrainingAssetRepo.Services
{
    public interface ILearningPathService
    {
        Task<IEnumerable<LearningPath>> GetAllLearningPathsAsync();
        Task<LearningPath?> GetLearningPathAsync(int id);
        Task<LearningPath> CreateLearningPathAsync(LearningPath learningPath);
        Task<LearningPath> CreateLearningPathAsync(LearningPath learningPath, IEnumerable<int>? trainingProgramIds = null);
        Task<LearningPath> UpdateLearningPathAsync(LearningPath learningPath);
         Task<CreateLearningPathWithMaterialsResponse> CreateLearningPathWithMaterialsAsync(CreateLearningPathWithMaterialsRequest request);
        Task<CompleteLearningPathResponse?> GetCompleteLearningPathAsync(int id);
        Task<IEnumerable<CompleteLearningPathResponse>> GetAllCompleteLearningPathsAsync();
        Task<bool> DeleteLearningPathAsync(int id);
        Task<bool> LearningPathExistsAsync(int id);
        
        // Junction table operations for Training Program associations
        Task<IEnumerable<LearningPath>> GetLearningPathsByTrainingProgramAsync(int trainingProgramId);
        Task<bool> AssignLearningPathToTrainingProgramAsync(int trainingProgramId, int learningPathId);
        Task<int> AssignMultipleLearningPathsToTrainingProgramAsync(int trainingProgramId, IEnumerable<int> LearningPaths);
        Task<bool> RemoveLearningPathFromTrainingProgramAsync(int trainingProgramId, int learningPathId);
    }

    public class LearningPathService : ILearningPathService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly IMaterialService _materialService;
        private readonly ILogger<LearningPathService> _logger;

        public LearningPathService(
            IXR50TenantDbContextFactory dbContextFactory,
            IMaterialService materialService,
            ILogger<LearningPathService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _materialService = materialService;
            _logger = logger;
        }

        public async Task<IEnumerable<LearningPath>> GetAllLearningPathsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.LearningPaths
                .Include(lp => lp.ProgramLearningPaths)
                    .ThenInclude(plp => plp.TrainingProgram)
                .ToListAsync();
        }

        public async Task<IEnumerable<LearningPath>> GetLearningPathsByTrainingProgramAsync(int trainingProgramId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.ProgramLearningPaths
                .Where(plp => plp.TrainingProgramId == trainingProgramId)
                .Select(plp => plp.LearningPath)
                .ToListAsync();
        }

        public async Task<bool> AssignLearningPathToTrainingProgramAsync(int trainingProgramId, int learningPathId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Check if association already exists
            var existingAssociation = await context.ProgramLearningPaths
                .AnyAsync(plp => plp.TrainingProgramId == trainingProgramId && plp.LearningPathId == learningPathId);

            if (existingAssociation)
            {
                _logger.LogWarning("Association between training program {TrainingProgramId} and learning path {LearningPathId} already exists",
                    trainingProgramId, learningPathId);
                return false;
            }

            var programLearningPath = new ProgramLearningPath
            {
                TrainingProgramId = trainingProgramId,
                LearningPathId = learningPathId
            };

            context.ProgramLearningPaths.Add(programLearningPath);
            await context.SaveChangesAsync();

            _logger.LogInformation("Associated learning path {LearningPathId} with training program {TrainingProgramId}",
                learningPathId, trainingProgramId);

            return true;
        }

        public async Task<int> AssignMultipleLearningPathsToTrainingProgramAsync(int trainingProgramId, IEnumerable<int> LearningPaths)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var associationsToAdd = new List<ProgramLearningPath>();

            foreach (var learningPathId in LearningPaths)
            {
                // Check if association already exists
                var existingAssociation = await context.ProgramLearningPaths
                    .AnyAsync(plp => plp.TrainingProgramId == trainingProgramId && plp.LearningPathId == learningPathId);

                if (!existingAssociation)
                {
                    // Verify learning path exists
                    var learningPathExists = await context.LearningPaths.AnyAsync(lp => lp.id == learningPathId);
                    if (learningPathExists)
                    {
                        associationsToAdd.Add(new ProgramLearningPath
                        {
                            TrainingProgramId = trainingProgramId,
                            LearningPathId = learningPathId
                        });
                    }
                    else
                    {
                        _logger.LogWarning("Learning path {LearningPathId} not found, skipping association", learningPathId);
                    }
                }
                else
                {
                    _logger.LogDebug("Association between training program {TrainingProgramId} and learning path {LearningPathId} already exists",
                        trainingProgramId, learningPathId);
                }
            }

            if (associationsToAdd.Any())
            {
                context.ProgramLearningPaths.AddRange(associationsToAdd);
                await context.SaveChangesAsync();

                _logger.LogInformation("Created {Count} new associations for training program {TrainingProgramId}",
                    associationsToAdd.Count, trainingProgramId);
            }

            return associationsToAdd.Count;
        }

        public async Task<bool> RemoveLearningPathFromTrainingProgramAsync(int trainingProgramId, int learningPathId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var programLearningPath = await context.ProgramLearningPaths
                .FirstOrDefaultAsync(plp => plp.TrainingProgramId == trainingProgramId && plp.LearningPathId == learningPathId);

            if (programLearningPath == null)
            {
                return false;
            }

            context.ProgramLearningPaths.Remove(programLearningPath);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed learning path {LearningPathId} from training program {TrainingProgramId}",
                learningPathId, trainingProgramId);

            return true;
        }

        public async Task<LearningPath?> GetLearningPathAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.LearningPaths
                .Include(lp => lp.ProgramLearningPaths)
                    .ThenInclude(plp => plp.TrainingProgram)
                .FirstOrDefaultAsync(lp => lp.id == id);
        }

        public async Task<LearningPath> CreateLearningPathAsync(LearningPath learningPath)
        {
            using var context = _dbContextFactory.CreateDbContext();

            context.LearningPaths.Add(learningPath);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created learning path: {Name} with ID: {Id}",
                learningPath.LearningPathName, learningPath.id);

            return learningPath;
        }

        public async Task<LearningPath> CreateLearningPathAsync(LearningPath learningPath, IEnumerable<int>? trainingProgramIds = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Create the learning path first
            context.LearningPaths.Add(learningPath);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created learning path: {Name} with ID: {Id}",
                learningPath.LearningPathName, learningPath.id);

            // If training program IDs are provided, create associations
            if (trainingProgramIds != null && trainingProgramIds.Any())
            {
                var associations = new List<ProgramLearningPath>();
                foreach (var trainingProgramId in trainingProgramIds)
                {
                    // Verify training program exists
                    var programExists = await context.TrainingPrograms.AnyAsync(tp => tp.id == trainingProgramId);
                    if (programExists)
                    {
                        associations.Add(new ProgramLearningPath
                        {
                            TrainingProgramId = trainingProgramId,
                            LearningPathId = learningPath.id
                        });
                    }
                    else
                    {
                        _logger.LogWarning("Training program {TrainingProgramId} not found, skipping association", trainingProgramId);
                    }
                }

                if (associations.Any())
                {
                    context.ProgramLearningPaths.AddRange(associations);
                    await context.SaveChangesAsync();

                    _logger.LogInformation("Created {Count} associations for learning path {LearningPathId}",
                        associations.Count, learningPath.id);
                }
            }

            return learningPath;
        }

        public async Task<LearningPath> UpdateLearningPathAsync(LearningPath learningPath)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Find existing learning path
                var existing = await context.LearningPaths.FindAsync(learningPath.id);
                if (existing == null)
                {
                    throw new KeyNotFoundException($"Learning path {learningPath.id} not found");
                }

                // Delete old learning path
                context.LearningPaths.Remove(existing);
                await context.SaveChangesAsync();

                // Add new learning path with same ID (full replacement)
                context.LearningPaths.Add(learningPath);
                await context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation("Updated learning path {Id} via delete-recreate",
                    learningPath.id);

                return learningPath;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> DeleteLearningPathAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var learningPath = await context.LearningPaths.FindAsync(id);
            if (learningPath == null)
            {
                return false;
            }

            context.LearningPaths.Remove(learningPath);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted learning path: {Id}", id);

            return true;
        }

        public async Task<bool> LearningPathExistsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.LearningPaths.AnyAsync(e => e.id == id);
        }
        public async Task<CreateLearningPathWithMaterialsResponse> CreateLearningPathWithMaterialsAsync(CreateLearningPathWithMaterialsRequest request)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. Create the learning path
                var learningPath = new LearningPath
                {
                    LearningPathName = request.LearningPathName,
                    Description = request.Description
                };

                context.LearningPaths.Add(learningPath);
                await context.SaveChangesAsync(); // Save to get the ID

                _logger.LogInformation("Created learning path: {Name} with ID: {Id}", learningPath.LearningPathName, learningPath.id);

                // 2. Validate that all requested materials exist
                var materialIds = request.Materials.Distinct().ToList();
                if (request.MaterialAssignments != null)
                {
                    materialIds.AddRange(request.MaterialAssignments.Select(ma => ma.MaterialId).Distinct());
                    materialIds = materialIds.Distinct().ToList();
                }

                var existingMaterials = await context.Materials
                    .Where(m => materialIds.Contains(m.id))
                    .ToListAsync();

                var missingMaterials = materialIds.Except(existingMaterials.Select(m => m.id)).ToList();
                if (missingMaterials.Any())
                {
                    throw new ArgumentException($"Materials not found: {string.Join(", ", missingMaterials)}");
                }

                // 3. Assign materials to the learning path
                var materialAssignments = new List<AssignedMaterialDetails>();
                
                // Handle simple material assignments first
                foreach (var materialId in request.Materials)
                {
                    if (request.MaterialAssignments?.Any(ma => ma.MaterialId == materialId) == true)
                        continue; // Skip if detailed assignment exists

                    await AssignMaterialToLearningPath(context, learningPath.id, materialId, "contains", null, materialAssignments, existingMaterials);
                }

                // Handle detailed material assignments
                if (request.MaterialAssignments != null)
                {
                    foreach (var assignment in request.MaterialAssignments)
                    {
                        await AssignMaterialToLearningPath(
                            context, 
                            learningPath.id, 
                            assignment.MaterialId, 
                            assignment.RelationshipType, 
                            assignment.DisplayOrder, 
                            materialAssignments, 
                            existingMaterials);
                    }
                }

                // 4. Assign learning path to training programs if specified
                var trainingProgramAssignments = new List<AssignedTrainingProgram>();
                if (request.TrainingPrograms?.Any() == true)
                {
                    var existingPrograms = await context.TrainingPrograms
                        .Where(tp => request.TrainingPrograms.Contains(tp.id))
                        .ToListAsync();

                    foreach (var programId in request.TrainingPrograms)
                    {
                        var program = existingPrograms.FirstOrDefault(p => p.id == programId);
                        if (program != null)
                        {
                            try
                            {
                                var programLearningPath = new ProgramLearningPath
                                {
                                    TrainingProgramId = programId,
                                    LearningPathId = learningPath.id
                                };

                                context.ProgramLearningPaths.Add(programLearningPath);

                                trainingProgramAssignments.Add(new AssignedTrainingProgram
                                {
                                    TrainingProgramId = programId,
                                    TrainingProgramName = program.Name,
                                    AssignmentSuccessful = true,
                                    AssignmentNote = "Successfully assigned"
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to assign learning path {LearningPathId} to training program {ProgramId}", 
                                    learningPath.id, programId);
                                
                                trainingProgramAssignments.Add(new AssignedTrainingProgram
                                {
                                    TrainingProgramId = programId,
                                    TrainingProgramName = program.Name,
                                    AssignmentSuccessful = false,
                                    AssignmentNote = $"Assignment failed: {ex.Message}"
                                });
                            }
                        }
                        else
                        {
                            trainingProgramAssignments.Add(new AssignedTrainingProgram
                            {
                                TrainingProgramId = programId,
                                TrainingProgramName = null,
                                AssignmentSuccessful = false,
                                AssignmentNote = "Training program not found"
                            });
                        }
                    }
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Successfully created complete learning path {LearningPathId} with {MaterialCount} materials and {ProgramCount} training programs",
                    learningPath.id, materialAssignments.Count, trainingProgramAssignments.Count);

                // 5. Return complete response
                return new CreateLearningPathWithMaterialsResponse
                {
                    Status = "success",
                    Message = $"Learning path '{learningPath.LearningPathName}' created successfully with {materialAssignments.Count} materials and {trainingProgramAssignments.Count} training programs",
                    id = learningPath.id,
                    LearningPathName = learningPath.LearningPathName,
                    Description = learningPath.Description,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    MaterialCount = materialAssignments.Count,
                    TrainingProgramCount = trainingProgramAssignments.Count,
                    AssignedMaterials = materialAssignments,
                    AssignedTrainingPrograms = trainingProgramAssignments
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create complete learning path: {Name}", request.LearningPathName);
                throw;
            }
        }

       
        /// Helper method to assign a material to a learning path
        
        private async Task AssignMaterialToLearningPath(
            XR50TrainingContext context,
            int learningPathId,
            int materialId,
            string relationshipType,
            int? displayOrder,
            List<AssignedMaterialDetails> assignments,
            List<Material> existingMaterials)
        {
            var material = existingMaterials.First(m => m.id == materialId);
            
            try
            {
                // Create MaterialRelationship entry for Learning Path
                var relationship = new MaterialRelationship
                {
                    MaterialId = materialId,
                    RelatedEntityId = learningPathId.ToString(),
                    RelatedEntityType = "LearningPath",
                    RelationshipType = relationshipType,
                    DisplayOrder = displayOrder
                };

                context.MaterialRelationships.Add(relationship);

                assignments.Add(new AssignedMaterialDetails
                {
                    MaterialId = materialId,
                    MaterialName = material.Name,
                    MaterialType = GetMaterialTypeString((int)material.Type),
                    AssignmentSuccessful = true,
                    AssignmentNote = "Successfully assigned",
                    RelationshipType = relationshipType,
                    DisplayOrder = displayOrder
                });

                _logger.LogInformation("Assigned material {MaterialId} to learning path {LearningPathId}", materialId, learningPathId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assign material {MaterialId} to learning path {LearningPathId}", materialId, learningPathId);
                
                assignments.Add(new AssignedMaterialDetails
                {
                    MaterialId = materialId,
                    MaterialName = material.Name,
                    MaterialType = GetMaterialTypeString((int)material.Type),
                    AssignmentSuccessful = false,
                    AssignmentNote = $"Assignment failed: {ex.Message}",
                    RelationshipType = relationshipType,
                    DisplayOrder = displayOrder
                });
            }
        }

       
        /// Get a complete learning path with all materials and training programs
        
        public async Task<CompleteLearningPathResponse?> GetCompleteLearningPathAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            var learningPath = await context.LearningPaths
                .Include(lp => lp.ProgramLearningPaths)
                    .ThenInclude(plp => plp.TrainingProgram)
                .FirstOrDefaultAsync(lp => lp.id == id);

            if (learningPath == null)
                return null;

            // Get materials assigned to this learning path
            var materials = await GetMaterialsForLearningPath(context, id);
            
            // Get training programs
            var trainingPrograms = learningPath.ProgramLearningPaths?.Select(plp => new TrainingProgramResponse
            {
                id = plp.TrainingProgram.id,
                Name = plp.TrainingProgram.Name,
                Description = plp.TrainingProgram.Description,
                Created_at = DateTime.TryParse(plp.TrainingProgram.Created_at, out var createdDate) ? createdDate : null
            }).ToList() ?? new List<TrainingProgramResponse>();

            // Build summary
            var materialsByType = materials.GroupBy(m => m.Type)
                .ToDictionary(g => g.Key, g => g.Count());

            return new CompleteLearningPathResponse
            {
                id = learningPath.id,
                LearningPathName = learningPath.LearningPathName,
                Description = learningPath.Description,
                Materials = materials,
                TrainingPrograms = trainingPrograms,
                Summary = new LearningPathSummary
                {
                    TotalMaterials = materials.Count,
                    TotalTrainingPrograms = trainingPrograms.Count,
                    MaterialsByType = materialsByType,
                    //Created = DateTime.TryParse(learningPath.Created_at, out var created) ? created : null,
                    //LastModified = DateTime.TryParse(learningPath.Updated_at, out var updated) ? updated : null
                }
            };
        }

       
        /// Get all learning paths with complete information
        
        public async Task<IEnumerable<CompleteLearningPathResponse>> GetAllCompleteLearningPathsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            var learningPaths = await context.LearningPaths
                .Include(lp => lp.ProgramLearningPaths)
                    .ThenInclude(plp => plp.TrainingProgram)
                .ToListAsync();

            var results = new List<CompleteLearningPathResponse>();

            foreach (var learningPath in learningPaths)
            {
                var completeLearningPath = await GetCompleteLearningPathAsync(learningPath.id);
                if (completeLearningPath != null)
                {
                    results.Add(completeLearningPath);
                }
            }

            return results;
        }

       
        /// Helper method to get materials for a learning path
        
        private async Task<List<MaterialResponse>> GetMaterialsForLearningPath(XR50TrainingContext context, int learningPathId)
        {
            // Query MaterialRelationships to get materials assigned to this learning path
            var query = from mr in context.MaterialRelationships
                        join m in context.Materials on mr.MaterialId equals m.id
                        where mr.RelatedEntityType == "LearningPath" &&
                              mr.RelatedEntityId == learningPathId.ToString()
                        orderby mr.DisplayOrder ?? int.MaxValue
                        select new { Material = m, Relationship = mr };

            var results = await query.ToListAsync();
            
            return results.Select(r => new MaterialResponse
            {
                id = r.Material.id,
                Name = r.Material.Name,
                Description = r.Material.Description,
                Type = GetMaterialTypeString((int)r.Material.Type),
                Unique_Id = r.Material.Unique_Id,
                Created_at = r.Material.Created_at,
                Updated_at = r.Material.Updated_at,
                Assignment = new AssignmentMetadata
                {
                    AssignmentType = "Complex",
                    RelationshipType = r.Relationship.RelationshipType,
                    DisplayOrder = r.Relationship.DisplayOrder,
                    RelationshipId = r.Relationship.Id
                }
            }).ToList();
        }

       
        /// Helper method to get material type string
        
        private string GetMaterialTypeString(int typeId)
        {
            // This should match the Type enum from Material.cs
            return typeId switch
            {
                0 => "Image",
                1 => "Video", 
                2 => "PDF",
                3 => "Unity",
                4 => "Chatbot",
                5 => "Questionnaire",
                6 => "Checklist",
                7 => "Workflow",
                8 => "MQTT_Template",
                9 => "Answers",
                10 => "Default",
                _ => "Unknown"
            };
        }
    }
}
