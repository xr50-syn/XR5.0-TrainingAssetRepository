using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Quiz material-specific operations including questions and answers.
    /// </summary>
    public class QuizMaterialService : IQuizMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<QuizMaterialService> _logger;

        public QuizMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<QuizMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Quiz Material CRUD

        public async Task<IEnumerable<QuizMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<QuizMaterial>()
                .ToListAsync();
        }

        public async Task<QuizMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<QuizMaterial>()
                .FirstOrDefaultAsync(q => q.id == id);
        }

        public async Task<QuizMaterial?> GetWithQuestionsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<QuizMaterial>()
                .Include(q => q.Questions!)
                    .ThenInclude(question => question.Answers)
                .FirstOrDefaultAsync(q => q.id == id);
        }

        public async Task<QuizMaterial> CreateAsync(QuizMaterial quiz)
        {
            using var context = _dbContextFactory.CreateDbContext();

            quiz.Created_at = DateTime.UtcNow;
            quiz.Updated_at = DateTime.UtcNow;
            quiz.Type = MaterialType.Quiz;

            context.Materials.Add(quiz);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created quiz material: {Name} with ID: {Id}", quiz.Name, quiz.id);

            return quiz;
        }

        public async Task<QuizMaterial> CreateWithQuestionsAsync(QuizMaterial quiz, IEnumerable<QuizQuestion>? questions = null)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                quiz.Created_at = DateTime.UtcNow;
                quiz.Updated_at = DateTime.UtcNow;
                quiz.Type = MaterialType.Quiz;

                context.Materials.Add(quiz);
                await context.SaveChangesAsync();

                if (questions != null && questions.Any())
                {
                    foreach (var question in questions)
                    {
                        question.QuizQuestionId = 0; // Reset ID for new record
                        question.QuizMaterialId = quiz.id;

                        // Temporarily store answers
                        var answers = question.Answers?.ToList();
                        question.Answers = new List<QuizAnswer>();

                        context.QuizQuestions.Add(question);
                        await context.SaveChangesAsync();

                        // Add answers if present
                        if (answers != null && answers.Any())
                        {
                            foreach (var answer in answers)
                            {
                                answer.QuizAnswerId = 0;
                                answer.QuizQuestionId = question.QuizQuestionId;
                                context.QuizAnswers.Add(answer);
                            }
                            await context.SaveChangesAsync();
                        }
                    }

                    _logger.LogInformation("Added {QuestionCount} initial questions to quiz {QuizId}",
                        questions.Count(), quiz.id);
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Created quiz material: {Name} with ID: {Id}", quiz.Name, quiz.id);

                return quiz;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create quiz material {Name} - Transaction rolled back", quiz.Name);
                throw;
            }
        }

        public async Task<QuizMaterial> UpdateAsync(QuizMaterial quiz)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var existing = await context.Materials
                    .OfType<QuizMaterial>()
                    .FirstOrDefaultAsync(q => q.id == quiz.id);

                if (existing == null)
                {
                    throw new KeyNotFoundException($"Quiz material {quiz.id} not found");
                }

                // Preserve original values
                var createdAt = existing.Created_at;
                var uniqueId = existing.Unique_id;

                // Delete existing questions and answers
                var existingQuestions = await context.QuizQuestions
                    .Include(q => q.Answers)
                    .Where(q => q.QuizMaterialId == quiz.id)
                    .ToListAsync();

                foreach (var question in existingQuestions)
                {
                    context.QuizAnswers.RemoveRange(question.Answers);
                }
                context.QuizQuestions.RemoveRange(existingQuestions);

                // Remove and re-add the material
                context.Materials.Remove(existing);
                await context.SaveChangesAsync();

                quiz.id = existing.id;
                quiz.Created_at = createdAt;
                quiz.Updated_at = DateTime.UtcNow;
                quiz.Unique_id = uniqueId;
                quiz.Type = MaterialType.Quiz;

                context.Materials.Add(quiz);
                await context.SaveChangesAsync();

                // Re-add questions and answers if present
                if (quiz.Questions?.Any() == true)
                {
                    foreach (var question in quiz.Questions.ToList())
                    {
                        var newQuestion = new QuizQuestion
                        {
                            QuestionNumber = question.QuestionNumber,
                            QuestionType = question.QuestionType,
                            Text = question.Text,
                            Description = question.Description,
                            Score = question.Score,
                            HelpText = question.HelpText,
                            AllowMultiple = question.AllowMultiple,
                            ScaleConfig = question.ScaleConfig,
                            QuizMaterialId = quiz.id
                        };
                        context.QuizQuestions.Add(newQuestion);
                        await context.SaveChangesAsync();

                        if (question.Answers?.Any() == true)
                        {
                            foreach (var answer in question.Answers.ToList())
                            {
                                var newAnswer = new QuizAnswer
                                {
                                    Text = answer.Text,
                                    CorrectAnswer = answer.CorrectAnswer,
                                    DisplayOrder = answer.DisplayOrder,
                                    Extra = answer.Extra,
                                    QuizQuestionId = newQuestion.QuizQuestionId
                                };
                                context.QuizAnswers.Add(newAnswer);
                            }
                            await context.SaveChangesAsync();
                        }
                    }
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Updated quiz material: {Id} ({Name})", quiz.id, quiz.Name);

                return await GetWithQuestionsAsync(quiz.id) ?? quiz;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update quiz material {Id}", quiz.id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var quiz = await context.Materials
                .OfType<QuizMaterial>()
                .FirstOrDefaultAsync(q => q.id == id);

            if (quiz == null)
            {
                return false;
            }

            // Delete questions and answers
            var questions = await context.QuizQuestions
                .Include(q => q.Answers)
                .Where(q => q.QuizMaterialId == id)
                .ToListAsync();

            foreach (var question in questions)
            {
                context.QuizAnswers.RemoveRange(question.Answers);
            }
            context.QuizQuestions.RemoveRange(questions);

            // Delete material relationships
            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            context.Materials.Remove(quiz);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted quiz material: {Id} with {QuestionCount} questions and {RelationshipCount} relationships",
                id, questions.Count, relationships.Count);

            return true;
        }

        #endregion

        #region Question Operations

        public async Task<QuizQuestion> AddQuestionAsync(int quizId, QuizQuestion question)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var quiz = await context.Materials
                .OfType<QuizMaterial>()
                .FirstOrDefaultAsync(q => q.id == quizId);

            if (quiz == null)
            {
                throw new ArgumentException($"Quiz material with ID {quizId} not found");
            }

            question.QuizQuestionId = 0;
            question.QuizMaterialId = quizId;

            context.QuizQuestions.Add(question);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added question '{Text}' to quiz material {QuizId}",
                question.Text, quizId);

            return question;
        }

        public async Task<bool> RemoveQuestionAsync(int quizId, int questionId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var question = await context.QuizQuestions
                .Include(q => q.Answers)
                .FirstOrDefaultAsync(q => q.QuizQuestionId == questionId);

            if (question == null || question.QuizMaterialId != quizId)
            {
                return false;
            }

            var orphanedRelationships = await context.SubcomponentMaterialRelationships
                .Where(smr => smr.SubcomponentId == questionId && smr.SubcomponentType == "QuizQuestion")
                .ToListAsync();
            if (orphanedRelationships.Count > 0)
                context.SubcomponentMaterialRelationships.RemoveRange(orphanedRelationships);

            // Remove answers first
            context.QuizAnswers.RemoveRange(question.Answers);
            context.QuizQuestions.Remove(question);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed question {QuestionId} from quiz material {QuizId}",
                questionId, quizId);

            return true;
        }

        public async Task<IEnumerable<QuizQuestion>> GetQuestionsAsync(int quizId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.QuizQuestions
                .Include(q => q.Answers)
                .Where(q => q.QuizMaterialId == quizId)
                .OrderBy(q => q.QuestionNumber)
                .ToListAsync();
        }

        public async Task<QuizQuestion?> GetQuestionWithAnswersAsync(int questionId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.QuizQuestions
                .Include(q => q.Answers)
                .FirstOrDefaultAsync(q => q.QuizQuestionId == questionId);
        }

        #endregion

        #region Answer Operations

        public async Task<QuizAnswer> AddAnswerAsync(int questionId, QuizAnswer answer)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var question = await context.QuizQuestions.FindAsync(questionId);
            if (question == null)
            {
                throw new ArgumentException($"Quiz question with ID {questionId} not found");
            }

            answer.QuizAnswerId = 0;
            answer.QuizQuestionId = questionId;

            context.QuizAnswers.Add(answer);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added answer '{Text}' to quiz question {QuestionId}",
                answer.Text, questionId);

            return answer;
        }

        public async Task<bool> RemoveAnswerAsync(int questionId, int answerId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var answer = await context.QuizAnswers.FindAsync(answerId);
            if (answer == null || answer.QuizQuestionId != questionId)
            {
                return false;
            }

            context.QuizAnswers.Remove(answer);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed answer {AnswerId} from quiz question {QuestionId}",
                answerId, questionId);

            return true;
        }

        public async Task<IEnumerable<QuizAnswer>> GetAnswersAsync(int questionId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.QuizAnswers
                .Where(a => a.QuizQuestionId == questionId)
                .OrderBy(a => a.DisplayOrder)
                .ToListAsync();
        }

        #endregion
    }
}
