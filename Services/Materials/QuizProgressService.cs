using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Services.Materials
{
    public class QuizProgressService : IQuizProgressService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<QuizProgressService> _logger;

        public QuizProgressService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<QuizProgressService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        public async Task<TenantQuizProgressResponse> GetTenantQuizProgressAsync(
            string? userId, bool isAdmin)
        {
            using var context = _dbContextFactory.CreateDbContext();

            _logger.LogInformation("Getting tenant quiz progress. UserId: {UserId}, IsAdmin: {IsAdmin}",
                userId, isAdmin);

            // Get all quiz material IDs
            var quizMaterialIds = await context.Materials
                .Where(m => m.Type == Models.Type.Quiz)
                .Select(m => m.id)
                .ToListAsync();

            // Build query for user scores on quiz materials
            var scoresQuery = context.UserMaterialScores
                .Where(ums => quizMaterialIds.Contains(ums.MaterialId));

            // Apply access control filter
            if (!isAdmin && !string.IsNullOrEmpty(userId))
            {
                scoresQuery = scoresQuery.Where(ums => ums.UserId == userId);
            }

            // Execute query with joins
            var progressData = await (
                from ums in scoresQuery
                join u in context.Users on ums.UserId equals u.UserName
                join m in context.Materials on ums.MaterialId equals m.id
                join tp in context.TrainingPrograms on ums.ProgramId equals tp.id into tpGroup
                from tp in tpGroup.DefaultIfEmpty()
                select new UserQuizProgressSummary
                {
                    UserId = ums.UserId,
                    UserName = u.FullName ?? u.UserName,
                    Score = ums.Score,
                    Progress = ums.Progress,
                    MaterialId = ums.MaterialId,
                    MaterialName = m.Name,
                    ProgramId = ums.ProgramId,
                    ProgramName = tp != null ? tp.Name : null,
                    UpdatedAt = ums.UpdatedAt
                }).ToListAsync();

            return new TenantQuizProgressResponse
            {
                TenantName = "",
                TotalUsers = progressData.Select(p => p.UserId).Distinct().Count(),
                TotalQuizAttempts = progressData.Count,
                AverageScore = progressData.Count > 0
                    ? progressData.Average(p => p.Score)
                    : 0,
                UserProgress = progressData
            };
        }

        public async Task<TrainingProgramQuizProgressResponse> GetTrainingProgramQuizProgressAsync(
            int programId, string? userId, bool isAdmin)
        {
            using var context = _dbContextFactory.CreateDbContext();

            _logger.LogInformation("Getting program {ProgramId} quiz progress. UserId: {UserId}, IsAdmin: {IsAdmin}",
                programId, userId, isAdmin);

            // Get program details
            var program = await context.TrainingPrograms
                .FirstOrDefaultAsync(p => p.id == programId);

            if (program == null)
            {
                throw new KeyNotFoundException($"Training program {programId} not found");
            }

            // Get quiz materials in this program via ProgramMaterial junction
            var quizMaterialIds = await (
                from pm in context.ProgramMaterials
                join m in context.Materials on pm.MaterialId equals m.id
                where pm.TrainingProgramId == programId && m.Type == Models.Type.Quiz
                select m.id
            ).ToListAsync();

            // Build query for scores
            var scoresQuery = context.UserMaterialScores
                .Where(ums => quizMaterialIds.Contains(ums.MaterialId));

            if (!isAdmin && !string.IsNullOrEmpty(userId))
            {
                scoresQuery = scoresQuery.Where(ums => ums.UserId == userId);
            }

            var progressData = await (
                from ums in scoresQuery
                join u in context.Users on ums.UserId equals u.UserName
                join m in context.Materials on ums.MaterialId equals m.id
                select new UserQuizProgressSummary
                {
                    UserId = ums.UserId,
                    UserName = u.FullName ?? u.UserName,
                    Score = ums.Score,
                    Progress = ums.Progress,
                    MaterialId = ums.MaterialId,
                    MaterialName = m.Name,
                    ProgramId = programId,
                    ProgramName = program.Name,
                    UpdatedAt = ums.UpdatedAt
                }).ToListAsync();

            return new TrainingProgramQuizProgressResponse
            {
                ProgramId = programId,
                ProgramName = program.Name ?? "",
                TotalQuizzes = quizMaterialIds.Count,
                TotalUsers = progressData.Select(p => p.UserId).Distinct().Count(),
                AverageScore = progressData.Count > 0
                    ? progressData.Average(p => p.Score)
                    : 0,
                UserProgress = progressData
            };
        }

        public async Task<LearningPathQuizProgressResponse> GetLearningPathQuizProgressAsync(
            int learningPathId, string? userId, bool isAdmin)
        {
            using var context = _dbContextFactory.CreateDbContext();

            _logger.LogInformation("Getting learning path {LearningPathId} quiz progress. UserId: {UserId}, IsAdmin: {IsAdmin}",
                learningPathId, userId, isAdmin);

            // Get learning path details
            var learningPath = await context.LearningPaths
                .FirstOrDefaultAsync(lp => lp.id == learningPathId);

            if (learningPath == null)
            {
                throw new KeyNotFoundException($"Learning path {learningPathId} not found");
            }

            // Get quiz materials in this learning path via MaterialRelationship
            var quizMaterialIds = await (
                from mr in context.MaterialRelationships
                join m in context.Materials on mr.MaterialId equals m.id
                where mr.RelatedEntityType == "LearningPath"
                      && mr.RelatedEntityId == learningPathId.ToString()
                      && m.Type == Models.Type.Quiz
                select m.id
            ).ToListAsync();

            // Build query for scores
            var scoresQuery = context.UserMaterialScores
                .Where(ums => quizMaterialIds.Contains(ums.MaterialId));

            if (!isAdmin && !string.IsNullOrEmpty(userId))
            {
                scoresQuery = scoresQuery.Where(ums => ums.UserId == userId);
            }

            var progressData = await (
                from ums in scoresQuery
                join u in context.Users on ums.UserId equals u.UserName
                join m in context.Materials on ums.MaterialId equals m.id
                join tp in context.TrainingPrograms on ums.ProgramId equals tp.id into tpGroup
                from tp in tpGroup.DefaultIfEmpty()
                select new UserQuizProgressSummary
                {
                    UserId = ums.UserId,
                    UserName = u.FullName ?? u.UserName,
                    Score = ums.Score,
                    Progress = ums.Progress,
                    MaterialId = ums.MaterialId,
                    MaterialName = m.Name,
                    ProgramId = ums.ProgramId,
                    ProgramName = tp != null ? tp.Name : null,
                    UpdatedAt = ums.UpdatedAt
                }).ToListAsync();

            return new LearningPathQuizProgressResponse
            {
                LearningPathId = learningPathId,
                LearningPathName = learningPath.LearningPathName ?? "",
                TotalQuizzes = quizMaterialIds.Count,
                TotalUsers = progressData.Select(p => p.UserId).Distinct().Count(),
                AverageScore = progressData.Count > 0
                    ? progressData.Average(p => p.Score)
                    : 0,
                UserProgress = progressData
            };
        }

        public async Task<MaterialQuizProgressResponse> GetMaterialQuizProgressAsync(
            int materialId, string? userId, bool isAdmin)
        {
            using var context = _dbContextFactory.CreateDbContext();

            _logger.LogInformation("Getting material {MaterialId} quiz progress. UserId: {UserId}, IsAdmin: {IsAdmin}",
                materialId, userId, isAdmin);

            // Validate material is a quiz
            var material = await context.Materials
                .FirstOrDefaultAsync(m => m.id == materialId && m.Type == Models.Type.Quiz);

            if (material == null)
            {
                throw new KeyNotFoundException($"Quiz material {materialId} not found");
            }

            // Build query for scores
            var scoresQuery = context.UserMaterialScores
                .Where(ums => ums.MaterialId == materialId);

            if (!isAdmin && !string.IsNullOrEmpty(userId))
            {
                scoresQuery = scoresQuery.Where(ums => ums.UserId == userId);
            }

            var progressData = await (
                from ums in scoresQuery
                join u in context.Users on ums.UserId equals u.UserName
                join tp in context.TrainingPrograms on ums.ProgramId equals tp.id into tpGroup
                from tp in tpGroup.DefaultIfEmpty()
                select new UserQuizProgressSummary
                {
                    UserId = ums.UserId,
                    UserName = u.FullName ?? u.UserName,
                    Score = ums.Score,
                    Progress = ums.Progress,
                    MaterialId = materialId,
                    MaterialName = material.Name,
                    ProgramId = ums.ProgramId,
                    ProgramName = tp != null ? tp.Name : null,
                    UpdatedAt = ums.UpdatedAt
                }).ToListAsync();

            return new MaterialQuizProgressResponse
            {
                MaterialId = materialId,
                MaterialName = material.Name ?? "",
                TotalAttempts = progressData.Count,
                AverageScore = progressData.Count > 0
                    ? progressData.Average(p => p.Score)
                    : 0,
                UserProgress = progressData
            };
        }
    }
}
