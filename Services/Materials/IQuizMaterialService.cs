using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Quiz material-specific operations including questions and answers.
    /// </summary>
    public interface IQuizMaterialService
    {
        // Quiz Material CRUD
        Task<IEnumerable<QuizMaterial>> GetAllAsync();
        Task<QuizMaterial?> GetByIdAsync(int id);
        Task<QuizMaterial?> GetWithQuestionsAsync(int id);
        Task<QuizMaterial> CreateAsync(QuizMaterial quiz);
        Task<QuizMaterial> CreateWithQuestionsAsync(QuizMaterial quiz, IEnumerable<QuizQuestion>? questions = null);
        Task<QuizMaterial> UpdateAsync(QuizMaterial quiz);
        Task<bool> DeleteAsync(int id);

        // Question Operations
        Task<QuizQuestion> AddQuestionAsync(int quizId, QuizQuestion question);
        Task<bool> RemoveQuestionAsync(int quizId, int questionId);
        Task<IEnumerable<QuizQuestion>> GetQuestionsAsync(int quizId);
        Task<QuizQuestion?> GetQuestionWithAnswersAsync(int questionId);

        // Answer Operations
        Task<QuizAnswer> AddAnswerAsync(int questionId, QuizAnswer answer);
        Task<bool> RemoveAnswerAsync(int questionId, int answerId);
        Task<IEnumerable<QuizAnswer>> GetAnswersAsync(int questionId);
    }
}
