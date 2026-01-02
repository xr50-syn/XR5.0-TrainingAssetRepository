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
                if (request.program_id.HasValue)
                {
                    var programMaterial = await context.ProgramMaterials
                        .FirstOrDefaultAsync(pm =>
                            pm.TrainingProgramId == request.program_id &&
                            pm.MaterialId == materialId);

                    if (programMaterial == null)
                    {
                        throw new ArgumentException($"Material {materialId} is not part of program {request.program_id}");
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
                    existingData.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    context.UserMaterialData.Add(new UserMaterialData
                    {
                        UserId = userId,
                        MaterialId = materialId,
                        ProgramId = request.program_id,
                        Data = JsonSerializer.Serialize(processedData),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                // 5. Calculate progress
                int progress = 100; // Default for standalone material
                if (request.program_id.HasValue)
                {
                    progress = await CalculateProgramProgressInternalAsync(
                        context, userId, request.program_id.Value, materialId);
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
                    existingScore.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    context.UserMaterialScores.Add(new UserMaterialScore
                    {
                        UserId = userId,
                        MaterialId = materialId,
                        ProgramId = request.program_id,
                        Score = processedData.total_score,
                        Progress = progress,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "User {UserId} submitted answers for material {MaterialId}. Score: {Score}, Progress: {Progress}",
                    userId, materialId, processedData.total_score, progress);

                return new SubmitQuizAnswersResponse
                {
                    success = true,
                    material_id = materialId,
                    program_id = request.program_id,
                    score = processedData.total_score,
                    progress = progress
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
