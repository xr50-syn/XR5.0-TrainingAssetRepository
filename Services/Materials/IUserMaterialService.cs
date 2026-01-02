using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Services.Materials
{
    public interface IUserMaterialService
    {
        /// <summary>
        /// Submit quiz answers for evaluation and store results
        /// </summary>
        Task<SubmitQuizAnswersResponse> SubmitAnswersAsync(
            string userId,
            int materialId,
            SubmitQuizAnswersRequest request);

        /// <summary>
        /// Get overall progress for a user including all programs and standalone materials
        /// </summary>
        Task<UserProgressResponse> GetUserProgressAsync(string userId);

        /// <summary>
        /// Get detailed data for a specific material submission
        /// </summary>
        Task<UserMaterialDetailResponse?> GetUserMaterialDetailAsync(
            string userId,
            int materialId);

        /// <summary>
        /// Get all materials with detailed data for a program
        /// </summary>
        Task<UserProgramMaterialsResponse?> GetUserProgramMaterialsAsync(
            string userId,
            int programId);

        /// <summary>
        /// Calculate progress percentage for a user within a program
        /// </summary>
        Task<int> CalculateProgramProgressAsync(string userId, int programId);
    }
}
