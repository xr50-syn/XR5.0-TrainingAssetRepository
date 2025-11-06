using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Data;

namespace XR50TrainingAssetRepo.Services
{
    public interface ITrainingProgramService
    {
        Task<IEnumerable<TrainingProgram>> GetAllTrainingProgramsAsync();
        Task<TrainingProgram?> GetTrainingProgramAsync(int id);
        Task<CompleteTrainingProgramResponse> CreateTrainingProgramAsync(CreateTrainingProgramWithMaterialsRequest request);
        Task<CreateTrainingProgramWithMaterialsResponse> CreateTrainingProgramWithMaterialsAsync(CreateTrainingProgramWithMaterialsRequest request);
        Task<TrainingProgram> UpdateTrainingProgramAsync(TrainingProgram program);
        Task<bool> DeleteTrainingProgramAsync(int id);
        Task<bool> TrainingProgramExistsAsync(int id);
        Task<bool> AssignMaterialToTrainingProgramAsync(int trainingProgramId, int materialId);
        Task<bool> RemoveMaterialFromTrainingProgramAsync(int trainingProgramId, int materialId);
        Task<IEnumerable<Material>> GetMaterialsByTrainingProgramAsync(int trainingProgramId);
        Task<CompleteTrainingProgramResponse> CreateCompleteTrainingProgramAsync(CompleteTrainingProgramRequest request);
        Task<CompleteTrainingProgramResponse?> GetCompleteTrainingProgramAsync(int id);
        Task<IEnumerable<CompleteTrainingProgramResponse>> GetAllCompleteTrainingProgramsAsync();
    }

    public class TrainingProgramService : ITrainingProgramService
    {
       // private readonly IMaterialService _materialService;
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<TrainingProgramService> _logger;

        public TrainingProgramService(
         //   IMaterialService materialService,
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<TrainingProgramService> logger)
        {
        //    _materialService = materialService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        public async Task<IEnumerable<TrainingProgram>> GetAllTrainingProgramsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.TrainingPrograms
                .Include(tp => tp.LearningPaths)  // Show learning path associations
                .Include(tp => tp.Materials)      // Show material associations (if you have this)
                .ToListAsync();
        }

        public async Task<TrainingProgram?> GetTrainingProgramAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.TrainingPrograms
                .Include(tp => tp.LearningPaths)  // Show learning path associations
                .Include(tp => tp.Materials)      // Show material associations (if you have this)
                .FirstOrDefaultAsync(tp => tp.id == id);
        }

        public async Task<CompleteTrainingProgramResponse> CreateTrainingProgramAsync(
            CreateTrainingProgramWithMaterialsRequest request)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. Validate materials ONLY if any are provided
                if (request.Materials.Any())
                {
                    var existingMaterials = await context.Materials
                        .Where(m => request.Materials.Contains(m.id))
                        .Select(m => m.id)
                        .ToListAsync();

                    var missingMaterials = request.Materials
                        .Except(existingMaterials)
                        .ToList();

                    if (missingMaterials.Any())
                    {
                        throw new ArgumentException($"Materials not found: {string.Join(", ", missingMaterials)}");
                    }
                }

                // 2. Validate learning paths ONLY if any are provided
                if (request.LearningPaths?.Any() == true)
                {
                    var existingLearningPaths = await context.LearningPaths
                        .Where(lp => request.LearningPaths.Contains(lp.learningPath_id))
                        .Select(lp => lp.learningPath_id)
                        .ToListAsync();

                    var missingLearningPaths = request.LearningPaths
                        .Except(existingLearningPaths)
                        .ToList();

                    if (missingLearningPaths.Any())
                    {
                        throw new ArgumentException($"Learning paths not found: {string.Join(", ", missingLearningPaths)}");
                    }
                }

                // 3. Create the training program
                var trainingProgram = new TrainingProgram
                {
                    Name = request.Name,
                    Description = request.Description,
                    Objectives = request.Objectives,
                    Requirements = request.Requirements,
                    min_level_rank = request.min_level_rank,
                    max_level_rank = request.max_level_rank,
                    required_upto_level_rank = request.required_upto_level_rank,
                    Created_at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };

                context.TrainingPrograms.Add(trainingProgram);
                await context.SaveChangesAsync();

                _logger.LogInformation("Created training program: {Name} with ID: {Id}",
                    trainingProgram.Name, trainingProgram.id);

                // 4. Create material assignments ONLY if materials are provided
                if (request.Materials.Any())
                {
                    var programMaterials = request.Materials.Select(materialId => new ProgramMaterial
                    {
                        TrainingProgramId = trainingProgram.id,
                        MaterialId = materialId
                    }).ToList();

                    context.ProgramMaterials.AddRange(programMaterials);
                }

                // 5. Create learning path assignments ONLY if learning paths are provided
                if (request.LearningPaths?.Any() == true)
                {
                    var programLearningPaths = request.LearningPaths.Select(learningPathId => new ProgramLearningPath
                    {
                        TrainingProgramId = trainingProgram.id,
                        LearningPathId = learningPathId
                    }).ToList();

                    context.ProgramLearningPaths.AddRange(programLearningPaths);
                }

                // 6. Save all assignments
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Successfully created training program {Id} with {MaterialCount} materials and {LearningPathCount} learning paths",
                    trainingProgram.id, request.Materials.Count, request.LearningPaths?.Count ?? 0);

                // 7. Return the complete training program response
                return await GetCompleteTrainingProgramAsync(trainingProgram.id)
                    ?? throw new Exception("Failed to retrieve created training program");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<CreateTrainingProgramWithMaterialsResponse> CreateTrainingProgramWithMaterialsAsync(
            CreateTrainingProgramWithMaterialsRequest request)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. Validate materials ONLY if any are provided
                var existingMaterials = new List<MaterialInfo>();
                if (request.Materials.Any())
                {
                    existingMaterials = await context.Materials
                        .Where(m => request.Materials.Contains(m.id))
                        .Select(m => new MaterialInfo
                        {
                            Id = m.id,
                            Name = m.Name,
                            Type = (int)m.Type
                        })
                        .ToListAsync();

                    var missingMaterials = request.Materials
                        .Except(existingMaterials.Select(m => m.Id))
                        .ToList();

                    if (missingMaterials.Any())
                    {
                        throw new ArgumentException($"Materials not found: {string.Join(", ", missingMaterials)}");
                    }
                }

                // 2. Validate learning paths ONLY if any are provided
                List<LearningPath> existingLearningPaths = new();
                if (request.LearningPaths?.Any() == true)
                {
                    existingLearningPaths = await context.LearningPaths
                        .Where(lp => request.LearningPaths.Contains(lp.learningPath_id))
                        .ToListAsync();

                    var missingLearningPaths = request.LearningPaths
                        .Except(existingLearningPaths.Select(lp => lp.learningPath_id))
                        .ToList();

                    if (missingLearningPaths.Any())
                    {
                        throw new ArgumentException($"Learning paths not found: {string.Join(", ", missingLearningPaths)}");
                    }
                }

                // 3. Create the training program (this always happens)
                var trainingProgram = new TrainingProgram
                {
                    Name = request.Name,
                    Description = request.Description,
                    Objectives = request.Objectives,
                    Requirements = request.Requirements,
                    min_level_rank = request.min_level_rank,
                    max_level_rank = request.max_level_rank,
                    required_upto_level_rank = request.required_upto_level_rank,
                    Created_at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };

                context.TrainingPrograms.Add(trainingProgram);
                await context.SaveChangesAsync();

                _logger.LogInformation("Created training program: {Name} with ID: {Id}",
                    trainingProgram.Name, trainingProgram.id);

                // 4. Create material assignments ONLY if materials are provided
                var materialAssignments = new List<AssignedMaterial>();
                if (request.Materials.Any())
                {
                    foreach (var materialId in request.Materials)
                    {
                        var material = existingMaterials.First(m => m.Id == materialId);
                        
                        try
                        {
                            var programMaterial = new ProgramMaterial
                            {
                                TrainingProgramId = trainingProgram.id,
                                MaterialId = materialId
                            };

                            context.ProgramMaterials.Add(programMaterial);
                            
                            materialAssignments.Add(new AssignedMaterial
                            {
                                MaterialId = materialId,
                                MaterialName = material.Name,
                                MaterialType = GetMaterialTypeString(material.Type),
                                AssignmentSuccessful = true,
                                AssignmentNote = "Successfully assigned"
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to assign material {MaterialId} to program {ProgramId}",
                                materialId, trainingProgram.id);
                            
                            materialAssignments.Add(new AssignedMaterial
                            {
                                MaterialId = materialId,
                                MaterialName = material.Name,
                                MaterialType = GetMaterialTypeString(material.Type),
                                AssignmentSuccessful = false,
                                AssignmentNote = $"Assignment failed: {ex.Message}"
                            });
                        }
                    }
                }

                // 5. Create learning path assignments ONLY if learning paths are provided
                var learningPathAssignments = new List<AssignedLearningPath>();
                if (request.LearningPaths?.Any() == true)
                {
                    foreach (var learningPathId in request.LearningPaths)
                    {
                        var learningPath = existingLearningPaths.First(lp => lp.learningPath_id == learningPathId);
                        
                        try
                        {
                            var programLearningPath = new ProgramLearningPath
                            {
                                TrainingProgramId = trainingProgram.id,
                                LearningPathId = learningPathId
                            };

                            context.ProgramLearningPaths.Add(programLearningPath);
                            
                            learningPathAssignments.Add(new AssignedLearningPath
                            {
                                LearningPathId = learningPathId,
                                LearningPathName = learningPath.LearningPathName,
                                AssignmentSuccessful = true,
                                AssignmentNote = "Successfully assigned"
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to assign learning path {LearningPathId} to program {ProgramId}",
                                learningPathId, trainingProgram.id);
                            
                            learningPathAssignments.Add(new AssignedLearningPath
                            {
                                LearningPathId = learningPathId,
                                LearningPathName = learningPath.LearningPathName,
                                AssignmentSuccessful = false,
                                AssignmentNote = $"Assignment failed: {ex.Message}"
                            });
                        }
                    }
                }

                // 6. Save all assignments (only if there are any)
                if (materialAssignments.Any() || learningPathAssignments.Any())
                {
                    await context.SaveChangesAsync();
                }
                
                await transaction.CommitAsync();

                _logger.LogInformation("Successfully created training program {Id} with {MaterialCount} materials and {LearningPathCount} learning paths",
                    trainingProgram.id, materialAssignments.Count(m => m.AssignmentSuccessful), 
                    learningPathAssignments.Count(lp => lp.AssignmentSuccessful));

                // 7. Return response
                return new CreateTrainingProgramWithMaterialsResponse
                {
                    Status = "success",
                    Message = $"Training program '{trainingProgram.Name}' created successfully with {materialAssignments.Count(m => m.AssignmentSuccessful)} materials and {learningPathAssignments.Count(lp => lp.AssignmentSuccessful)} learning paths",
                    id = trainingProgram.id,
                    Name = trainingProgram.Name,
                    Description = trainingProgram.Description,
                    Objectives = trainingProgram.Objectives,
                    Requirements = trainingProgram.Requirements,
                    min_level_rank = trainingProgram.min_level_rank,
                    max_level_rank = trainingProgram.max_level_rank,
                    required_upto_level_rank = trainingProgram.required_upto_level_rank,
                    CreatedAt = trainingProgram.Created_at,
                    MaterialCount = materialAssignments.Count(m => m.AssignmentSuccessful),
                    LearningPathCount = learningPathAssignments.Count(lp => lp.AssignmentSuccessful),
                    AssignedMaterials = materialAssignments,
                    AssignedLearningPaths = learningPathAssignments
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Helper method to convert material type enum to string
        private string GetMaterialTypeString(int materialType)
        {
            return materialType switch
            {
                0 => "Default",
                1 => "Video",
                2 => "Image",
                3 => "PDF",
                4 => "Unity",
                5 => "Chatbot",
                6 => "MQTT_Template",
                7 => "Checklist",
                8 => "Workflow",
                9 => "Questionnaire",
                _ => "Unknown"
            };
        }
        public async Task<TrainingProgram> UpdateTrainingProgramAsync(TrainingProgram program)
        {
            using var context = _dbContextFactory.CreateDbContext();

            context.Entry(program).State = EntityState.Modified;
            await context.SaveChangesAsync();

            _logger.LogInformation("Updated training program: {Id}", program.id);

            return program;
        }

        public async Task<bool> DeleteTrainingProgramAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var program = await context.TrainingPrograms.FindAsync(id);
            if (program == null)
            {
                return false;
            }

            context.TrainingPrograms.Remove(program);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted training program: {Id}", id);

            return true;
        }

        public async Task<bool> TrainingProgramExistsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.TrainingPrograms.AnyAsync(e => e.id == id);
        }
        // Add these methods to TrainingProgramService class:

        #region Simple Material Assignment (ProgramMaterial Junction Table)

       
        /// Assign a material to training program using simple junction table
        
        public async Task<bool> AssignMaterialToTrainingProgramAsync(int trainingProgramId, int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Check if relationship already exists
            var exists = await context.ProgramMaterials
                .AnyAsync(pm => pm.MaterialId == materialId && pm.TrainingProgramId == trainingProgramId);

            if (exists)
            {
                _logger.LogWarning("Material {MaterialId} already assigned to training program {ProgramId}",
                    materialId, trainingProgramId);
                return false; // Already exists
            }

            // Verify both entities exist
            var materialExists = await context.Materials.AnyAsync(m => m.id == materialId);
            var programExists = await context.TrainingPrograms.AnyAsync(tp => tp.id == trainingProgramId);

            if (!materialExists)
            {
                _logger.LogError("Material {MaterialId} not found", materialId);
                throw new ArgumentException($"Material with ID {materialId} not found");
            }

            if (!programExists)
            {
                _logger.LogError("Training program {ProgramId} not found", trainingProgramId);
                throw new ArgumentException($"Training program with ID {trainingProgramId} not found");
            }

            // Create the relationship
            var programMaterial = new ProgramMaterial
            {
                MaterialId = materialId,
                TrainingProgramId = trainingProgramId
            };

            context.ProgramMaterials.Add(programMaterial);
            await context.SaveChangesAsync();

            _logger.LogInformation("Successfully assigned material {MaterialId} to training program {ProgramId}",
                materialId, trainingProgramId);

            return true;
        }

       
        /// Remove a material from training program using simple junction table
        
        public async Task<bool> RemoveMaterialFromTrainingProgramAsync(int trainingProgramId, int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var programMaterial = await context.ProgramMaterials
                .FirstOrDefaultAsync(pm => pm.MaterialId == materialId && pm.TrainingProgramId == trainingProgramId);

            if (programMaterial == null)
            {
                _logger.LogWarning("Material {MaterialId} not assigned to training program {ProgramId}",
                    materialId, trainingProgramId);
                return false;
            }

            context.ProgramMaterials.Remove(programMaterial);
            await context.SaveChangesAsync();

            _logger.LogInformation("Successfully removed material {MaterialId} from training program {ProgramId}",
                materialId, trainingProgramId);

            return true;
        }

       
        /// Get all materials assigned to training program via simple junction table
        
        public async Task<IEnumerable<Material>> GetMaterialsByTrainingProgramAsync(int trainingProgramId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var materials = await context.ProgramMaterials
                .Where(pm => pm.TrainingProgramId == trainingProgramId)
                .Include(pm => pm.Material)
                .Select(pm => pm.Material)
                .ToListAsync();

            _logger.LogInformation("Found {Count} materials for training program {ProgramId}",
                materials.Count, trainingProgramId);

            return materials;
        }

        #endregion
        #region Complete Training Program Operations

       
        /// Create a complete training program with materials and learning paths in one transaction
        
        public async Task<CompleteTrainingProgramResponse> CreateCompleteTrainingProgramAsync(CompleteTrainingProgramRequest request)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. Create the training program
                var program = new TrainingProgram
                {
                    Name = request.Name,
                    Description = request.Description,
                    Objectives = request.Objectives,
                    Requirements = request.Requirements,
                    min_level_rank = request.min_level_rank,
                    max_level_rank = request.max_level_rank,
                    required_upto_level_rank = request.required_upto_level_rank,
                    Created_at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };

                context.TrainingPrograms.Add(program);
                await context.SaveChangesAsync(); // Save to get the ID

                _logger.LogInformation("Created training program: {Name} with ID: {Id}", program.Name, program.id);

                // 2. Create new materials if specified
                var createdMaterials = new List<int>();
             /*   if (request.MaterialsToCreate != null && request.MaterialsToCreate.Any())
                {
                    foreach (var materialRequest in request.MaterialsToCreate)
                    {
                            // Don't create inline - use the MaterialService that works!
                        var material = await _materialService.CreateMaterialAsync(materialRequest);
                        createdMaterials.Add(material.id);
                        _logger.LogInformation("Created material: {Name} with ID: {Id}", material.Name, material.id);
                    }
                }
*/
                // 3. Combine existing material IDs with newly created ones
                var allMaterials = request.Materials.Concat(createdMaterials).Distinct().ToList();

                // 4. Assign materials to the program
                if (allMaterials.Any())
                {
                    var assignments = allMaterials.Select(materialId => new ProgramMaterial
                    {
                        TrainingProgramId = program.id,
                        MaterialId = materialId
                    }).ToList();

                    context.ProgramMaterials.AddRange(assignments);
                    _logger.LogInformation("Assigning {Count} materials to program {ProgramId}",
                        allMaterials.Count, program.id);
                }

                // 5. Assign learning paths to the program
                if (request.LearningPaths.Any())
                {
                    var pathAssignments = request.LearningPaths.Select(pathId => new ProgramLearningPath
                    {
                        TrainingProgramId = program.id,
                        LearningPathId = pathId
                    }).ToList();

                    context.ProgramLearningPaths.AddRange(pathAssignments);
                    _logger.LogInformation("Assigning {Count} learning paths to program {ProgramId}",
                        request.LearningPaths.Count, program.id);
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Successfully created complete training program {ProgramId} with {MaterialCount} materials and {PathCount} learning paths",
                    program.id, allMaterials.Count, request.LearningPaths.Count);

                // 6. Return the complete response
                return await GetCompleteTrainingProgramAsync(program.id);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create complete training program: {Name}", request.Name);
                throw;
            }
        }
       /* private Material MapRequestToMaterial(MaterialCreationRequest request)
        {
            Material material = request.MaterialType.ToLower() switch
            {
                "checklist" => new ChecklistMaterial
                {
                    Entries = MapChecklistEntries(request.Entries)
                },
                "workflow" => new WorkflowMaterial
                {
                    WorkflowSteps = MapWorkflowSteps(request.Steps)
                },
                "video" => new VideoMaterial
                {
                    AssetId = request.AssetId,
                    VideoPath = request.VideoPath,
                    VideoDuration = request.VideoDuration,
                    VideoResolution = request.VideoResolution,
                    VideoTimestamps = MapVideoTimestamps(request.Timestamps)
                },
                "questionnaire" => new QuestionnaireMaterial
                {
                    QuestionnaireEntries = MapQuestionnaireEntries(request.Questions)
                },
                "image" => new ImageMaterial
                {
                    AssetId = request.AssetId,
                    ImagePath = request.ImagePath
                },
                "chatbot" => new ChatbotMaterial
                {
                    ChatbotConfig = request.ChatbotConfig
                },
                "mqtt_template" => new MQTT_TemplateMaterial
                {
                    message_type = request.MessageType,
                    message_text = request.MessageText
                },
                "pdf" => new PDFMaterial 
                { 
                    AssetId = request.AssetId 
                },
                "unitydemo" => new UnityMaterial 
                { 
                    AssetId = request.AssetId 
                },
                _ => new DefaultMaterial 
                { 
                    AssetId = request.AssetId 
                }
            };

            // Set common properties
            material.Name = request.Name;
            material.Description = request.Description;
            material.UniqueId = request.UniqueId;
            material.Created_at = DateTime.UtcNow;
            material.Updated_at = DateTime.UtcNow;

            return material;
        }

        // Helper methods for complex types
        private List<ChecklistEntry> MapChecklistEntries(List<ChecklistEntryRequest>? entries)
        {
            if (entries == null) return new List<ChecklistEntry>();
            
            return entries.Select(e => new ChecklistEntry
            {
                Text = e.Text,
                Description = e.Description
            }).ToList();
        }

        private List<WorkflowStep> MapWorkflowSteps(List<WorkflowStepRequest>? steps)
        {
            if (steps == null) return new List<WorkflowStep>();
            
            return steps.Select(s => new WorkflowStep
            {
                Title = s.Title,
                Content = s.Content
            }).ToList();
        }
*/
       
        /// Get a complete training program with all materials and learning paths
        
        public async Task<CompleteTrainingProgramResponse?> GetCompleteTrainingProgramAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var program = await context.TrainingPrograms
                .Include(tp => tp.Materials)
                    .ThenInclude(pm => pm.Material)
                .Include(tp => tp.LearningPaths)
                    .ThenInclude(plp => plp.LearningPath)
                .FirstOrDefaultAsync(tp => tp.id == id);

            if (program == null)
            {
                return null;
            }

            // Get materials with their complete information
            var materials = new List<MaterialResponse>();
            foreach (var pm in program.Materials)
            {
                var materialResponse = await BuildMaterialResponse(pm.Material);
                materials.Add(materialResponse);
            }

            // Get learning paths
            var learningPaths = program.LearningPaths.Select(plp => new LearningPathResponse
            {
                learningPath_id = plp.LearningPath.learningPath_id,
                LearningPathName = plp.LearningPath.LearningPathName,
                Description = plp.LearningPath.Description
            }).ToList();

            _logger.LogInformation("Retrieved complete training program {Id}: {MaterialCount} materials, {PathCount} learning paths",
                id, materials.Count, learningPaths.Count);

            return new CompleteTrainingProgramResponse
            {
                Status = "success",
                Message = $"Training program '{program.Name}' retrieved successfully",
                id = program.id,
                Name = program.Name,
                Description = program.Description,
                Objectives = program.Objectives,
                Requirements = program.Requirements,
                min_level_rank = program.min_level_rank,
                max_level_rank = program.max_level_rank,
                required_upto_level_rank = program.required_upto_level_rank,
                Created_at = program.Created_at,
                Materials = materials,
                LearningPaths = learningPaths
            };
        }

       
        /// Get all training programs with complete information
        
        public async Task<IEnumerable<CompleteTrainingProgramResponse>> GetAllCompleteTrainingProgramsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();

            var programs = await context.TrainingPrograms.ToListAsync();
            var results = new List<CompleteTrainingProgramResponse>();

            foreach (var program in programs)
            {
                var completeProgram = await GetCompleteTrainingProgramAsync(program.id);
                if (completeProgram != null)
                {
                    results.Add(completeProgram);
                }
            }

            _logger.LogInformation("Retrieved {Count} complete training programs", results.Count);
            return results;
        }

        #endregion

        #region Helper Methods

        private Material CreateMaterialFromRequest(MaterialCreationRequest request)
        {
            Material material = request.MaterialType.ToLower() switch
            {
                "video" => new VideoMaterial
                {
                    AssetId = request.AssetId,
                    VideoPath = request.VideoPath,
                    VideoDuration = request.VideoDuration,
                    VideoResolution = request.VideoResolution
                },
                "image" => new ImageMaterial
                {
                    AssetId = request.AssetId,
                    ImagePath = request.ImagePath
                },
                "chatbot" => new ChatbotMaterial
                {
                    ChatbotConfig = request.ChatbotConfig
                },
                "mqtt_template" => new MQTT_TemplateMaterial
                {
                    message_type = request.MessageType,
                    message_text = request.MessageText
                },
                "checklist" => new ChecklistMaterial(),
                "workflow" => new WorkflowMaterial(),
                "pdf" => new PDFMaterial { AssetId = request.AssetId },
                "unitydemo" => new UnityMaterial { AssetId = request.AssetId },
                "questionnaire" => new QuestionnaireMaterial(),
                _ => new DefaultMaterial { AssetId = request.AssetId }
            };

            material.Name = request.Name;
            material.Description = request.Description;
            material.UniqueId = request.UniqueId;
            material.Created_at = DateTime.UtcNow;
            material.Updated_at = DateTime.UtcNow;

            return material;
        }

        private async Task<MaterialResponse> BuildMaterialResponse(Material material)
        {
            var response = new MaterialResponse
            {
                id = material.id,
                Name = material.Name,
                Description = material.Description,
                Type = material.Type.ToString(),
                UniqueId = material.UniqueId,
                Created_at = material.Created_at,
                Updated_at = material.Updated_at,
                Assignment = new AssignmentMetadata { AssignmentType = "Simple" }
            };

            // Add type-specific properties
            switch (material)
            {
                case VideoMaterial video:
                    response.AssetId = video.AssetId;
                    response.TypeSpecificProperties = new Dictionary<string, object?>
                    {
                        ["VideoPath"] = video.VideoPath,
                        ["VideoDuration"] = video.VideoDuration,
                        ["VideoResolution"] = video.VideoResolution
                    };
                    break;

                case ImageMaterial image:
                    response.AssetId = image.AssetId;
                    response.TypeSpecificProperties = new Dictionary<string, object?>
                    {
                        ["ImagePath"] = image.ImagePath,
                        ["ImageWidth"] = image.ImageWidth,
                        ["ImageHeight"] = image.ImageHeight,
                        ["ImageFormat"] = image.ImageFormat
                    };
                    break;

                case PDFMaterial pdf:
                    response.AssetId = pdf.AssetId;
                    response.TypeSpecificProperties = new Dictionary<string, object?>
                    {
                        ["PdfPath"] = pdf.PdfPath,
                        ["PdfPageCount"] = pdf.PdfPageCount,
                        ["PdfFileSize"] = pdf.PdfFileSize
                    };
                    break;

                case ChatbotMaterial chatbot:
                    response.TypeSpecificProperties = new Dictionary<string, object?>
                    {
                        ["ChatbotConfig"] = chatbot.ChatbotConfig,
                        ["ChatbotModel"] = chatbot.ChatbotModel,
                        ["ChatbotPrompt"] = chatbot.ChatbotPrompt
                    };
                    break;

                case MQTT_TemplateMaterial mqtt:
                    response.TypeSpecificProperties = new Dictionary<string, object?>
                    {
                        ["MessageType"] = mqtt.message_type,
                        ["MessageText"] = mqtt.message_text
                    };
                    break;

                case UnityMaterial unity:
                    response.AssetId = unity.AssetId;
                    response.TypeSpecificProperties = new Dictionary<string, object?>
                    {
                        ["UnityVersion"] = unity.UnityVersion,
                        ["UnityBuildTarget"] = unity.UnityBuildTarget,
                        ["UnitySceneName"] = unity.UnitySceneName
                    };
                    break;

                case DefaultMaterial defaultMat:
                    response.AssetId = defaultMat.AssetId;
                    break;
            }

            return response;
        }

        #endregion
    }
    
}