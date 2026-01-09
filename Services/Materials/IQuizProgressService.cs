using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Services.Materials
{
    public interface IQuizProgressService
    {
        /// <summary>
        /// Get all quiz progress in a tenant
        /// </summary>
        /// <param name="userId">Current user ID for access control (null = admin view)</param>
        /// <param name="isAdmin">Whether current user is admin</param>
        Task<TenantQuizProgressResponse> GetTenantQuizProgressAsync(string? userId, bool isAdmin);

        /// <summary>
        /// Get quiz progress for materials in a specific training program
        /// </summary>
        Task<TrainingProgramQuizProgressResponse> GetTrainingProgramQuizProgressAsync(
            int programId, string? userId, bool isAdmin);

        /// <summary>
        /// Get quiz progress for materials in a specific learning path
        /// </summary>
        Task<LearningPathQuizProgressResponse> GetLearningPathQuizProgressAsync(
            int learningPathId, string? userId, bool isAdmin);

        /// <summary>
        /// Get quiz progress for a specific material
        /// </summary>
        Task<MaterialQuizProgressResponse> GetMaterialQuizProgressAsync(
            int materialId, string? userId, bool isAdmin);
    }
}
