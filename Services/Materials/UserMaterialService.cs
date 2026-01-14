using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Services.Materials
{
    public class UserMaterialService : IUserMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<UserMaterialService> _logger;

        public UserMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<UserMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        public async Task<SubmitQuizAnswersResponse> SubmitAnswersAsync(
            string userId,
            int materialId,
            SubmitQuizAnswersRequest request)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. Validate material exists and is a quiz
                var quiz = await context.Materials
                    .OfType<QuizMaterial>()
                    .Include(q => q.Questions)
                        .ThenInclude(q => q.Answers)
                    .FirstOrDefaultAsync(q => q.id == materialId);

                if (quiz == null)
                {
                    throw new ArgumentException($"Quiz material {materialId} not found");
                }

                // 2. Validate program if provided
                int? learningPathId = null;
                if (request.program_id.HasValue)
                {
                    // First check if material is directly assigned to program
                    var programMaterial = await context.ProgramMaterials
                        .FirstOrDefaultAsync(pm =>
                            pm.TrainingProgramId == request.program_id &&
                            pm.MaterialId == materialId);

                    // Get learning paths in this program
                    var programLearningPathIds = await context.Set<ProgramLearningPath>()
                        .Where(plp => plp.TrainingProgramId == request.program_id)
                        .Select(plp => plp.LearningPathId)
                        .ToListAsync();

                    // Check if material belongs to any learning path in this program
                    MaterialRelationship? materialRelationship = null;
                    if (programLearningPathIds.Any())
                    {
                        materialRelationship = await context.MaterialRelationships
                            .FirstOrDefaultAsync(mr =>
                                mr.MaterialId == materialId &&
                                mr.RelatedEntityType == "LearningPath" &&
                                programLearningPathIds.Select(id => id.ToString()).Contains(mr.RelatedEntityId));
                    }

                    // Material must be either directly in program OR in a learning path of the program
                    if (programMaterial == null && materialRelationship == null)
                    {
                        throw new ArgumentException($"Material {materialId} is not part of program {request.program_id}");
                    }

                    // Set learning path ID if found
                    if (materialRelationship != null && int.TryParse(materialRelationship.RelatedEntityId, out int lpId))
                    {
                        learningPathId = lpId;
                        _logger.LogInformation(
                            "Material {MaterialId} belongs to learning path {LearningPathId} within program {ProgramId}",
                            materialId, learningPathId, request.program_id);
                    }
                }

                // 3. Evaluate answers and calculate score
                var processedData = EvaluateAnswers(quiz, request);

                // 4. Upsert user_material_data
                var existingData = await context.UserMaterialData
                    .FirstOrDefaultAsync(umd =>
                        umd.UserId == userId &&
                        umd.MaterialId == materialId);

                if (existingData != null)
                {
                    existingData.Data = JsonSerializer.Serialize(processedData);
                    existingData.ProgramId = request.program_id;
                    existingData.LearningPathId = learningPathId;
                    existingData.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    context.UserMaterialData.Add(new UserMaterialData
                    {
                        UserId = userId,
                        MaterialId = materialId,
                        ProgramId = request.program_id,
                        LearningPathId = learningPathId,
                        Data = JsonSerializer.Serialize(processedData),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                // 5. Calculate progress
                int progress = 100; // Default for standalone material
                int? learningPathProgress = null;

                if (request.program_id.HasValue)
                {
                    progress = await CalculateProgramProgressInternalAsync(
                        context, userId, request.program_id.Value, materialId);
                }

                // 5a. Calculate learning path progress if applicable
                if (learningPathId.HasValue)
                {
                    learningPathProgress = await CalculateLearningPathProgressInternalAsync(
                        context, userId, learningPathId.Value, materialId);
                }

                // 6. Upsert user_material_scores
                var existingScore = await context.UserMaterialScores
                    .FirstOrDefaultAsync(ums =>
                        ums.UserId == userId &&
                        ums.MaterialId == materialId);

                if (existingScore != null)
                {
                    existingScore.Score = processedData.total_score;
                    existingScore.Progress = progress;
                    existingScore.ProgramId = request.program_id;
                    existingScore.LearningPathId = learningPathId;
                    existingScore.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    context.UserMaterialScores.Add(new UserMaterialScore
                    {
                        UserId = userId,
                        MaterialId = materialId,
                        ProgramId = request.program_id,
                        LearningPathId = learningPathId,
                        Score = processedData.total_score,
                        Progress = progress,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "User {UserId} submitted answers for material {MaterialId}. Score: {Score}, Progress: {Progress}, LearningPathId: {LearningPathId}, LearningPathProgress: {LearningPathProgress}",
                    userId, materialId, processedData.total_score, progress, learningPathId, learningPathProgress);

                return new SubmitQuizAnswersResponse
                {
                    success = true,
                    material_id = materialId,
                    program_id = request.program_id,
                    learning_path_id = learningPathId,
                    score = processedData.total_score,
                    progress = progress,
                    learning_path_progress = learningPathProgress
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to submit answers for material {MaterialId}", materialId);
                throw;
            }
        }

        public async Task<UserProgressResponse> GetUserProgressAsync(string userId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var user = await context.Users.FirstOrDefaultAsync(u => u.UserName == userId);
            if (user == null)
            {
                throw new ArgumentException($"User {userId} not found");
            }

            // Get all user scores
            var userScores = await context.UserMaterialScores
                .Where(ums => ums.UserId == userId)
                .ToListAsync();

            // Get programs the user has progress in
            var programIds = userScores
                .Where(s => s.ProgramId.HasValue)
                .Select(s => s.ProgramId!.Value)
                .Distinct()
                .ToList();

            var programs = await context.TrainingPrograms
                .Where(p => programIds.Contains(p.id))
                .Include(p => p.Materials)
                    .ThenInclude(pm => pm.Material)
                .ToListAsync();

            var programProgressList = new List<ProgramProgressDto>();

            foreach (var program in programs)
            {
                var programScores = userScores
                    .Where(s => s.ProgramId == program.id)
                    .ToList();

                var totalMaterials = program.Materials?.Count ?? 0;
                var completedMaterials = programScores.Count;
                var programProgress = totalMaterials > 0
                    ? (int)Math.Round((double)completedMaterials / totalMaterials * 100)
                    : 0;

                var materialScores = new List<MaterialScoreDto>();
                if (program.Materials != null)
                {
                    foreach (var pm in program.Materials)
                    {
                        var score = programScores.FirstOrDefault(s => s.MaterialId == pm.MaterialId);
                        materialScores.Add(new MaterialScoreDto
                        {
                            id = pm.MaterialId.ToString(),
                            name = pm.Material?.Name ?? "",
                            type = pm.Material?.Type.ToString() ?? "",
                            score = score?.Score ?? 0,
                            completed = score != null
                        });
                    }
                }

                programProgressList.Add(new ProgramProgressDto
                {
                    id = program.id.ToString(),
                    name = program.Name,
                    progress = programProgress,
                    materials = materialScores
                });
            }

            // Get standalone materials (no program)
            var standaloneMaterialIds = userScores
                .Where(s => !s.ProgramId.HasValue)
                .Select(s => s.MaterialId)
                .ToList();

            var standaloneMaterials = await context.Materials
                .Where(m => standaloneMaterialIds.Contains(m.id))
                .ToListAsync();

            var standaloneProgressList = standaloneMaterials.Select(m =>
            {
                var score = userScores.FirstOrDefault(s => s.MaterialId == m.id && !s.ProgramId.HasValue);
                return new StandaloneMaterialProgressDto
                {
                    id = m.id.ToString(),
                    name = m.Name,
                    type = m.Type.ToString(),
                    score = score?.Score ?? 0
                };
            }).ToList();

            // Calculate overall progress
            var totalCompleted = userScores.Count;
            var overallProgress = totalCompleted > 0 ? 100 : 0; // Simplified overall progress

            return new UserProgressResponse
            {
                id = userId,
                name = user.FullName ?? user.UserName,
                progress = overallProgress,
                programs = programProgressList,
                standalone_materials = standaloneProgressList
            };
        }

        public async Task<UserMaterialDetailResponse?> GetUserMaterialDetailAsync(
            string userId,
            int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var userData = await context.UserMaterialData
                .FirstOrDefaultAsync(umd =>
                    umd.UserId == userId &&
                    umd.MaterialId == materialId);

            if (userData == null)
            {
                return null;
            }

            var score = await context.UserMaterialScores
                .FirstOrDefaultAsync(ums =>
                    ums.UserId == userId &&
                    ums.MaterialId == materialId);

            ProcessedAnswerData? processedData = null;
            try
            {
                processedData = JsonSerializer.Deserialize<ProcessedAnswerData>(userData.Data);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize user material data for user {UserId}, material {MaterialId}",
                    userId, materialId);
            }

            return new UserMaterialDetailResponse
            {
                user_id = userId,
                program_id = userData.ProgramId,
                material_id = materialId,
                score = score?.Score ?? 0,
                data = processedData
            };
        }

        public async Task<UserProgramMaterialsResponse?> GetUserProgramMaterialsAsync(
            string userId,
            int programId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var program = await context.TrainingPrograms
                .Include(p => p.Materials)
                    .ThenInclude(pm => pm.Material)
                .FirstOrDefaultAsync(p => p.id == programId);

            if (program == null)
            {
                return null;
            }

            var materialIds = program.Materials?.Select(pm => pm.MaterialId).ToList() ?? new List<int>();

            var userDataList = await context.UserMaterialData
                .Where(umd => umd.UserId == userId && materialIds.Contains(umd.MaterialId))
                .ToListAsync();

            var userScores = await context.UserMaterialScores
                .Where(ums => ums.UserId == userId && materialIds.Contains(ums.MaterialId))
                .ToListAsync();

            var materials = new List<UserMaterialDetailResponse>();

            foreach (var materialId in materialIds)
            {
                var userData = userDataList.FirstOrDefault(ud => ud.MaterialId == materialId);
                var score = userScores.FirstOrDefault(s => s.MaterialId == materialId);

                ProcessedAnswerData? processedData = null;
                if (userData != null)
                {
                    try
                    {
                        processedData = JsonSerializer.Deserialize<ProcessedAnswerData>(userData.Data);
                    }
                    catch (JsonException)
                    {
                        // Ignore deserialization errors
                    }
                }

                materials.Add(new UserMaterialDetailResponse
                {
                    user_id = userId,
                    program_id = programId,
                    material_id = materialId,
                    score = score?.Score ?? 0,
                    data = processedData
                });
            }

            var progress = await CalculateProgramProgressAsync(userId, programId);

            return new UserProgramMaterialsResponse
            {
                user_id = userId,
                program_id = programId,
                program_name = program.Name,
                progress = progress,
                materials = materials
            };
        }

        public async Task<int> CalculateProgramProgressAsync(string userId, int programId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await CalculateProgramProgressInternalAsync(context, userId, programId, null);
        }

        public async Task<MarkMaterialCompleteResponse> MarkMaterialCompleteAsync(
            string userId,
            int materialId,
            MarkMaterialCompleteRequest request)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. Validate material exists
                var material = await context.Materials
                    .FirstOrDefaultAsync(m => m.id == materialId);

                if (material == null)
                {
                    throw new ArgumentException($"Material {materialId} not found");
                }

                // 2. Validate material is part of the program (directly or via learning path)
                var programMaterial = await context.ProgramMaterials
                    .FirstOrDefaultAsync(pm =>
                        pm.TrainingProgramId == request.program_id &&
                        pm.MaterialId == materialId);

                // Get learning paths in this program
                var programLearningPathIds = await context.Set<ProgramLearningPath>()
                    .Where(plp => plp.TrainingProgramId == request.program_id)
                    .Select(plp => plp.LearningPathId)
                    .ToListAsync();

                // Check if material belongs to any learning path in this program
                int? learningPathId = null;
                MaterialRelationship? materialRelationship = null;
                if (programLearningPathIds.Any())
                {
                    materialRelationship = await context.MaterialRelationships
                        .FirstOrDefaultAsync(mr =>
                            mr.MaterialId == materialId &&
                            mr.RelatedEntityType == "LearningPath" &&
                            programLearningPathIds.Select(id => id.ToString()).Contains(mr.RelatedEntityId));
                }

                // Material must be either directly in program OR in a learning path of the program
                if (programMaterial == null && materialRelationship == null)
                {
                    throw new ArgumentException($"Material {materialId} is not part of program {request.program_id}");
                }

                // Set learning path ID if found
                if (materialRelationship != null && int.TryParse(materialRelationship.RelatedEntityId, out int lpId))
                {
                    learningPathId = lpId;
                    _logger.LogInformation(
                        "Material {MaterialId} belongs to learning path {LearningPathId} within program {ProgramId}",
                        materialId, learningPathId, request.program_id);
                }

                // 4. Calculate progress
                int progress = await CalculateProgramProgressInternalAsync(
                    context, userId, request.program_id, materialId);

                int? learningPathProgress = null;
                if (learningPathId.HasValue)
                {
                    learningPathProgress = await CalculateLearningPathProgressInternalAsync(
                        context, userId, learningPathId.Value, materialId);
                }

                // 5. Upsert UserMaterialScore (preserve existing score if any)
                var existingScore = await context.UserMaterialScores
                    .FirstOrDefaultAsync(ums =>
                        ums.UserId == userId &&
                        ums.MaterialId == materialId);

                if (existingScore != null)
                {
                    // Update but preserve existing score
                    existingScore.Progress = progress;
                    existingScore.ProgramId = request.program_id;
                    existingScore.LearningPathId = learningPathId;
                    existingScore.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    // Create new with score = 0
                    context.UserMaterialScores.Add(new UserMaterialScore
                    {
                        UserId = userId,
                        MaterialId = materialId,
                        ProgramId = request.program_id,
                        LearningPathId = learningPathId,
                        Score = 0,
                        Progress = progress,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "User {UserId} marked material {MaterialId} as complete. Progress: {Progress}, LearningPathId: {LearningPathId}, LearningPathProgress: {LearningPathProgress}",
                    userId, materialId, progress, learningPathId, learningPathProgress);

                return new MarkMaterialCompleteResponse
                {
                    success = true,
                    material_id = materialId,
                    program_id = request.program_id,
                    learning_path_id = learningPathId,
                    progress = progress,
                    learning_path_progress = learningPathProgress
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to mark material {MaterialId} as complete", materialId);
                throw;
            }
        }

        public async Task<BulkMaterialCompleteResponse> BulkMarkMaterialsCompleteAsync(
            string userId,
            int programId,
            BulkMaterialCompleteRequest request)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. Validate program exists
                var program = await context.TrainingPrograms
                    .FirstOrDefaultAsync(p => p.id == programId);

                if (program == null)
                {
                    throw new ArgumentException($"Program {programId} not found");
                }

                // 2. Get all valid materials directly in this program
                var directProgramMaterialIds = await context.ProgramMaterials
                    .Where(pm => pm.TrainingProgramId == programId)
                    .Select(pm => pm.MaterialId)
                    .ToListAsync();

                // 3. Pre-fetch learning path mappings for this program
                var programLearningPathIds = await context.Set<ProgramLearningPath>()
                    .Where(plp => plp.TrainingProgramId == programId)
                    .Select(plp => plp.LearningPathId)
                    .ToListAsync();

                var materialToLearningPath = new Dictionary<int, int>();
                if (programLearningPathIds.Any())
                {
                    var materialRelationships = await context.MaterialRelationships
                        .Where(mr =>
                            mr.RelatedEntityType == "LearningPath" &&
                            programLearningPathIds.Select(id => id.ToString()).Contains(mr.RelatedEntityId))
                        .ToListAsync();

                    foreach (var mr in materialRelationships)
                    {
                        if (int.TryParse(mr.RelatedEntityId, out int lpId))
                        {
                            materialToLearningPath[mr.MaterialId] = lpId;
                        }
                    }
                }

                // Combine direct materials and materials via learning paths
                var programMaterialIds = directProgramMaterialIds
                    .Union(materialToLearningPath.Keys)
                    .ToList();

                // 4. Get existing scores for these materials
                var existingScores = await context.UserMaterialScores
                    .Where(ums => ums.UserId == userId && request.material_ids.Contains(ums.MaterialId))
                    .ToDictionaryAsync(ums => ums.MaterialId);

                // 5. Process each material
                var results = new List<MaterialCompleteResult>();
                var learningPathsAffected = new HashSet<int>();
                int successCount = 0;

                foreach (var materialId in request.material_ids)
                {
                    var result = new MaterialCompleteResult { material_id = materialId };

                    // Validate material is in program
                    if (!programMaterialIds.Contains(materialId))
                    {
                        result.success = false;
                        result.error = $"Material {materialId} is not part of program {programId}";
                        results.Add(result);
                        continue;
                    }

                    // Get learning path if any
                    int? learningPathId = materialToLearningPath.TryGetValue(materialId, out int lpId) ? lpId : null;
                    result.learning_path_id = learningPathId;

                    if (learningPathId.HasValue)
                    {
                        learningPathsAffected.Add(learningPathId.Value);
                    }

                    // Upsert score (preserve existing score)
                    if (existingScores.TryGetValue(materialId, out var existingScore))
                    {
                        existingScore.ProgramId = programId;
                        existingScore.LearningPathId = learningPathId;
                        existingScore.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        context.UserMaterialScores.Add(new UserMaterialScore
                        {
                            UserId = userId,
                            MaterialId = materialId,
                            ProgramId = programId,
                            LearningPathId = learningPathId,
                            Score = 0,
                            Progress = 0, // Will be recalculated
                            UpdatedAt = DateTime.UtcNow
                        });
                    }

                    result.success = true;
                    successCount++;
                    results.Add(result);
                }

                await context.SaveChangesAsync();

                // 6. Calculate final program progress
                int programProgress = await CalculateProgramProgressInternalAsync(context, userId, programId, null);

                // 7. Calculate per-learning-path progress and update results
                var learningPathSummary = new List<LearningPathProgressSummary>();

                foreach (var lpId in learningPathsAffected)
                {
                    int lpProgress = await CalculateLearningPathProgressInternalAsync(context, userId, lpId, null);

                    // Get learning path name
                    var lp = await context.LearningPaths.FirstOrDefaultAsync(l => l.id == lpId);

                    learningPathSummary.Add(new LearningPathProgressSummary
                    {
                        learning_path_id = lpId,
                        learning_path_name = lp?.LearningPathName,
                        progress = lpProgress
                    });

                    // Update results with learning path progress
                    foreach (var result in results.Where(r => r.learning_path_id == lpId))
                    {
                        result.learning_path_progress = lpProgress;
                    }
                }

                await transaction.CommitAsync();

                _logger.LogInformation(
                    "User {UserId} bulk completed {Count} materials in program {ProgramId}. Program progress: {Progress}",
                    userId, successCount, programId, programProgress);

                return new BulkMaterialCompleteResponse
                {
                    success = successCount > 0,
                    program_id = programId,
                    program_progress = programProgress,
                    materials_completed = successCount,
                    results = results,
                    learning_path_summary = learningPathSummary
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to bulk mark materials as complete in program {ProgramId}", programId);
                throw;
            }
        }

        public async Task<ProgramProgressResponse> GetProgramProgressAsync(
            int programId,
            string? userId,
            bool isAdmin)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // 1. Validate program exists
            var program = await context.TrainingPrograms
                .FirstOrDefaultAsync(p => p.id == programId);

            if (program == null)
            {
                throw new KeyNotFoundException($"Program {programId} not found");
            }

            // 2. Get all materials in this program
            var programMaterials = await context.ProgramMaterials
                .Where(pm => pm.TrainingProgramId == programId)
                .Join(context.Materials,
                    pm => pm.MaterialId,
                    m => m.id,
                    (pm, m) => new { pm.MaterialId, m.Name, Type = m.Type.ToString() })
                .ToListAsync();

            var programMaterialIds = programMaterials.Select(pm => pm.MaterialId).ToList();
            var totalMaterials = programMaterials.Count;

            // 3. Get learning path mappings for this program
            var programLearningPathIds = await context.Set<ProgramLearningPath>()
                .Where(plp => plp.TrainingProgramId == programId)
                .Select(plp => plp.LearningPathId)
                .ToListAsync();

            var materialToLearningPath = new Dictionary<int, int>();
            if (programLearningPathIds.Any())
            {
                var materialRelationships = await context.MaterialRelationships
                    .Where(mr =>
                        mr.RelatedEntityType == "LearningPath" &&
                        programLearningPathIds.Select(id => id.ToString()).Contains(mr.RelatedEntityId))
                    .ToListAsync();

                foreach (var mr in materialRelationships)
                {
                    if (int.TryParse(mr.RelatedEntityId, out int lpId))
                    {
                        materialToLearningPath[mr.MaterialId] = lpId;
                    }
                }
            }

            // 4. Build query for user scores - filter by user if not admin
            var scoresQuery = context.UserMaterialScores
                .Where(ums => programMaterialIds.Contains(ums.MaterialId));

            if (!isAdmin && !string.IsNullOrEmpty(userId))
            {
                scoresQuery = scoresQuery.Where(ums => ums.UserId == userId);
            }

            var scores = await scoresQuery.ToListAsync();

            // 5. Get user details for all users with progress
            var userIds = scores.Select(s => s.UserId).Distinct().ToList();
            var users = await context.Users
                .Where(u => userIds.Contains(u.UserName))
                .ToDictionaryAsync(u => u.UserName, u => u.FullName ?? u.UserName);

            // 6. Build per-user progress
            var userProgressList = new List<UserProgramProgressDetail>();

            foreach (var userGroup in scores.GroupBy(s => s.UserId))
            {
                var uid = userGroup.Key;
                var userScores = userGroup.ToList();
                var completedMaterialIds = userScores.Select(s => s.MaterialId).ToHashSet();

                var materialsProgress = programMaterials.Select(pm => new MaterialProgressDetail
                {
                    material_id = pm.MaterialId,
                    material_name = pm.Name ?? "",
                    material_type = pm.Type,
                    completed = completedMaterialIds.Contains(pm.MaterialId),
                    score = userScores.FirstOrDefault(s => s.MaterialId == pm.MaterialId)?.Score,
                    learning_path_id = materialToLearningPath.TryGetValue(pm.MaterialId, out int lpId) ? lpId : null,
                    completed_at = userScores.FirstOrDefault(s => s.MaterialId == pm.MaterialId)?.UpdatedAt
                }).ToList();

                var completedCount = completedMaterialIds.Count;
                var progress = totalMaterials > 0
                    ? (int)Math.Round((double)completedCount / totalMaterials * 100)
                    : 0;

                userProgressList.Add(new UserProgramProgressDetail
                {
                    user_id = uid,
                    user_name = users.TryGetValue(uid, out var name) ? name : uid,
                    progress = progress,
                    materials_completed = completedCount,
                    total_materials = totalMaterials,
                    last_activity = userScores.Max(s => s.UpdatedAt),
                    materials = materialsProgress
                });
            }

            // 7. Calculate learning path progress summaries
            var learningPathSummaries = new List<LearningPathProgressSummary>();
            foreach (var lpId in programLearningPathIds)
            {
                var lp = await context.LearningPaths.FirstOrDefaultAsync(l => l.id == lpId);
                var lpMaterialIds = materialToLearningPath
                    .Where(kvp => kvp.Value == lpId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                var lpTotalMaterials = lpMaterialIds.Count;
                var lpCompletedMaterials = scores
                    .Where(s => lpMaterialIds.Contains(s.MaterialId))
                    .Select(s => s.MaterialId)
                    .Distinct()
                    .Count();

                var lpProgress = lpTotalMaterials > 0
                    ? (int)Math.Round((double)lpCompletedMaterials / lpTotalMaterials * 100)
                    : 0;

                learningPathSummaries.Add(new LearningPathProgressSummary
                {
                    learning_path_id = lpId,
                    learning_path_name = lp?.LearningPathName,
                    progress = lpProgress
                });
            }

            // 8. Calculate overall stats
            var averageProgress = userProgressList.Any()
                ? userProgressList.Average(u => u.progress)
                : 0;

            return new ProgramProgressResponse
            {
                program_id = programId,
                program_name = program.Name ?? "",
                total_materials = totalMaterials,
                total_users = userProgressList.Count,
                average_progress = Math.Round(averageProgress, 1),
                user_progress = userProgressList,
                learning_paths = learningPathSummaries
            };
        }

        #region Private Helper Methods

        private async Task<int> CalculateProgramProgressInternalAsync(
            XR50TrainingContext context,
            string userId,
            int programId,
            int? newlyCompletedMaterialId = null)
        {
            // Get total materials in program
            var totalMaterials = await context.ProgramMaterials
                .Where(pm => pm.TrainingProgramId == programId)
                .CountAsync();

            if (totalMaterials == 0)
            {
                return 100;
            }

            // Get material IDs in program
            var programMaterialIds = await context.ProgramMaterials
                .Where(pm => pm.TrainingProgramId == programId)
                .Select(pm => pm.MaterialId)
                .ToListAsync();

            // Get completed materials (those with scores)
            var completedCount = await context.UserMaterialScores
                .Where(ums => ums.UserId == userId && programMaterialIds.Contains(ums.MaterialId))
                .CountAsync();

            // Add 1 if we're about to insert a new score
            if (newlyCompletedMaterialId.HasValue)
            {
                var alreadyCounted = await context.UserMaterialScores
                    .AnyAsync(ums =>
                        ums.UserId == userId &&
                        ums.MaterialId == newlyCompletedMaterialId.Value);

                if (!alreadyCounted)
                {
                    completedCount++;
                }
            }

            return (int)Math.Round((double)completedCount / totalMaterials * 100);
        }

        private async Task<int> CalculateLearningPathProgressInternalAsync(
            XR50TrainingContext context,
            string userId,
            int learningPathId,
            int? newlyCompletedMaterialId = null)
        {
            // Get all materials in this learning path via MaterialRelationship
            var learningPathMaterialIds = await context.MaterialRelationships
                .Where(mr =>
                    mr.RelatedEntityType == "LearningPath" &&
                    mr.RelatedEntityId == learningPathId.ToString())
                .Select(mr => mr.MaterialId)
                .ToListAsync();

            var totalMaterials = learningPathMaterialIds.Count;

            if (totalMaterials == 0)
            {
                return 100;
            }

            // Get completed materials (those with scores)
            var completedCount = await context.UserMaterialScores
                .Where(ums => ums.UserId == userId && learningPathMaterialIds.Contains(ums.MaterialId))
                .CountAsync();

            // Add 1 if we're about to insert a new score
            if (newlyCompletedMaterialId.HasValue)
            {
                var alreadyCounted = await context.UserMaterialScores
                    .AnyAsync(ums =>
                        ums.UserId == userId &&
                        ums.MaterialId == newlyCompletedMaterialId.Value);

                if (!alreadyCounted)
                {
                    completedCount++;
                }
            }

            return (int)Math.Round((double)completedCount / totalMaterials * 100);
        }

        private ProcessedAnswerData EvaluateAnswers(
            QuizMaterial quiz,
            SubmitQuizAnswersRequest request)
        {
            var processedData = new ProcessedAnswerData
            {
                version = 1,
                submitted_at = DateTime.UtcNow,
                answers = new List<ProcessedAnswer>()
            };

            decimal totalScore = 0;

            foreach (var questionAnswer in request.questions)
            {
                var question = quiz.Questions
                    .FirstOrDefault(q => q.QuizQuestionId == questionAnswer.question_id);

                if (question == null)
                {
                    _logger.LogWarning("Question {QuestionId} not found in quiz {QuizId}",
                        questionAnswer.question_id, quiz.id);
                    continue;
                }

                var processed = new ProcessedAnswer
                {
                    question_id = questionAnswer.question_id,
                    type = question.QuestionType,
                    answer_ids = questionAnswer.answer.answer_ids,
                    value = questionAnswer.answer.value,
                    text = questionAnswer.answer.text
                };

                // Evaluate based on question type
                var (scoreAwarded, isCorrect) = EvaluateQuestion(question, questionAnswer.answer);

                processed.score_awarded = scoreAwarded;
                processed.is_correct = isCorrect;
                totalScore += scoreAwarded;

                processedData.answers.Add(processed);
            }

            processedData.total_score = totalScore;
            return processedData;
        }

        private (decimal score, bool isCorrect) EvaluateQuestion(
            QuizQuestion question,
            AnswerRequest answer)
        {
            var questionType = question.QuestionType.ToLower();

            switch (questionType)
            {
                case "boolean":
                case "multiple-choice":
                case "single-choice":
                    return EvaluateChoiceQuestion(question, answer);

                case "scale":
                    return EvaluateScaleQuestion(question, answer);

                case "text":
                    // Text questions typically don't have auto-scoring
                    return (0, true);

                default:
                    _logger.LogWarning("Unknown question type: {Type}", question.QuestionType);
                    return (0, false);
            }
        }

        private (decimal score, bool isCorrect) EvaluateChoiceQuestion(
            QuizQuestion question,
            AnswerRequest answer)
        {
            if (answer.answer_ids == null || !answer.answer_ids.Any())
            {
                return (0, false);
            }

            // Get correct answer IDs
            var correctAnswerIds = question.Answers
                .Where(a => a.CorrectAnswer)
                .Select(a => a.QuizAnswerId)
                .ToHashSet();

            var submittedIds = answer.answer_ids.ToHashSet();

            // For boolean/single-choice: must match exactly
            // For multiple-choice with AllowMultiple: all correct must be selected
            bool isCorrect = correctAnswerIds.SetEquals(submittedIds);

            return isCorrect
                ? (question.Score ?? 0, true)
                : (0, false);
        }

        private (decimal score, bool isCorrect) EvaluateScaleQuestion(
            QuizQuestion question,
            AnswerRequest answer)
        {
            // Scale questions typically record the value without scoring
            // Could implement scoring logic based on ScaleConfig if needed
            return (0, true);
        }

        #endregion
    }
}
