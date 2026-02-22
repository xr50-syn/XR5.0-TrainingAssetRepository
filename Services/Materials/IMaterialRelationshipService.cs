using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for managing material relationships:
    /// - Material-to-Entity relationships (LearningPath, TrainingProgram, Material)
    /// - Subcomponent-to-Material relationships (ChecklistEntry, WorkflowStep, etc.)
    /// </summary>
    public interface IMaterialRelationshipService
    {
        #region Base Relationship Operations

        Task<MaterialRelationship> CreateRelationshipAsync(MaterialRelationship relationship);
        Task<bool> DeleteRelationshipAsync(int relationshipId);
        Task<IEnumerable<MaterialRelationship>> GetMaterialRelationshipsAsync(int materialId);
        Task<IEnumerable<MaterialRelationship>> GetRelationshipsByTypeAsync(int materialId, string relatedEntityType);

        #endregion

        #region Learning Path Relationships

        Task<int> AssignMaterialToLearningPathAsync(int materialId, int learningPathId, string relationshipType = "contains", int? displayOrder = null);
        Task<bool> RemoveMaterialFromLearningPathAsync(int materialId, int learningPathId);
        Task<IEnumerable<Material>> GetMaterialsByLearningPathAsync(int learningPathId, bool includeOrder = true);
        Task<bool> ReorderMaterialsInLearningPathAsync(int learningPathId, Dictionary<int, int> materialOrderMap);

        #endregion

        #region Training Program Relationships

        Task<int> AssignMaterialToTrainingProgramAsync(int materialId, int trainingProgramId, string relationshipType = "assigned");
        Task<bool> RemoveMaterialFromTrainingProgramAsync(int materialId, int trainingProgramId);
        Task<IEnumerable<Material>> GetMaterialsByTrainingProgramAsync(int trainingProgramId);

        #endregion

        #region Material Dependencies (Prerequisites)

        Task<int> CreateMaterialDependencyAsync(int materialId, int prerequisiteMaterialId, string relationshipType = "prerequisite");
        Task<bool> RemoveMaterialDependencyAsync(int materialId, int prerequisiteMaterialId);
        Task<IEnumerable<Material>> GetMaterialPrerequisitesAsync(int materialId);
        Task<IEnumerable<Material>> GetMaterialDependentsAsync(int materialId);

        #endregion

        #region Material-to-Material Hierarchical Relationships

        Task<int> AssignMaterialToMaterialAsync(int parentMaterialId, int childMaterialId, string relationshipType = "contains", int? displayOrder = null);
        Task<bool> RemoveMaterialFromMaterialAsync(int parentMaterialId, int childMaterialId);
        Task<IEnumerable<Material>> GetChildMaterialsAsync(int parentMaterialId, bool includeOrder = true, string? relationshipType = null);
        Task<IEnumerable<Material>> GetParentMaterialsAsync(int childMaterialId, string? relationshipType = null);
        Task<bool> ReorderChildMaterialsAsync(int parentMaterialId, Dictionary<int, int> materialOrderMap);
        Task<bool> WouldCreateCircularReferenceAsync(int parentMaterialId, int childMaterialId);
        Task<MaterialHierarchy> GetMaterialHierarchyAsync(int rootMaterialId, int maxDepth = 5);

        #endregion

        #region Subcomponent-to-Material Relationships

        Task<int> AssignMaterialToSubcomponentAsync(int subcomponentId, string subcomponentType, int materialId, string? relationshipType = null, int? displayOrder = null);
        Task<bool> RemoveMaterialFromSubcomponentAsync(int subcomponentId, string subcomponentType, int materialId);
        Task<IEnumerable<Material>> GetMaterialsForSubcomponentAsync(int subcomponentId, string subcomponentType, bool includeOrder = true);
        Task<bool> ReorderSubcomponentMaterialsAsync(int subcomponentId, string subcomponentType, Dictionary<int, int> materialOrderMap);
        Task<IEnumerable<SubcomponentMaterialRelationship>> GetSubcomponentRelationshipsAsync(int subcomponentId, string subcomponentType);

        // Convenience methods for specific subcomponent types
        Task<int> AssignMaterialToChecklistEntryAsync(int entryId, int materialId, string? relationshipType = null, int? displayOrder = null);
        Task<int> AssignMaterialToWorkflowStepAsync(int stepId, int materialId, string? relationshipType = null, int? displayOrder = null);
        Task<int> AssignMaterialToQuestionnaireEntryAsync(int entryId, int materialId, string? relationshipType = null, int? displayOrder = null);
        Task<int> AssignMaterialToVideoTimestampAsync(int timestampId, int materialId, string? relationshipType = null, int? displayOrder = null);
        Task<int> AssignMaterialToQuizQuestionAsync(int questionId, int materialId, string? relationshipType = null, int? displayOrder = null);
        Task<int> AssignMaterialToImageAnnotationAsync(int annotationId, int materialId, string? relationshipType = null, int? displayOrder = null);

        Task<IEnumerable<Material>> GetMaterialsForChecklistEntryAsync(int entryId, bool includeOrder = true);
        Task<IEnumerable<Material>> GetMaterialsForWorkflowStepAsync(int stepId, bool includeOrder = true);
        Task<IEnumerable<Material>> GetMaterialsForQuestionnaireEntryAsync(int entryId, bool includeOrder = true);
        Task<IEnumerable<Material>> GetMaterialsForVideoTimestampAsync(int timestampId, bool includeOrder = true);
        Task<IEnumerable<Material>> GetMaterialsForQuizQuestionAsync(int questionId, bool includeOrder = true);
        Task<IEnumerable<Material>> GetMaterialsForImageAnnotationAsync(int annotationId, bool includeOrder = true);

        Task<bool> RemoveMaterialFromChecklistEntryAsync(int entryId, int materialId);
        Task<bool> RemoveMaterialFromWorkflowStepAsync(int stepId, int materialId);
        Task<bool> RemoveMaterialFromQuestionnaireEntryAsync(int entryId, int materialId);
        Task<bool> RemoveMaterialFromVideoTimestampAsync(int timestampId, int materialId);
        Task<bool> RemoveMaterialFromQuizQuestionAsync(int questionId, int materialId);

        #endregion
    }
}
