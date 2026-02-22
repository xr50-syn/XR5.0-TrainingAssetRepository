using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Implementation of material relationship management.
    /// Handles Material-to-Entity and Subcomponent-to-Material relationships.
    /// </summary>
    public class MaterialRelationshipService : IMaterialRelationshipService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<MaterialRelationshipService> _logger;

        private static readonly HashSet<string> ValidSubcomponentTypes = new HashSet<string>
        {
            "ChecklistEntry",
            "WorkflowStep",
            "QuestionnaireEntry",
            "VideoTimestamp",
            "QuizQuestion",
            "QuizAnswer",
            "ImageAnnotation"
        };

        public MaterialRelationshipService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<MaterialRelationshipService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Base Relationship Operations

        public async Task<MaterialRelationship> CreateRelationshipAsync(MaterialRelationship relationship)
        {
            using var context = _dbContextFactory.CreateDbContext();

            context.MaterialRelationships.Add(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created relationship {RelationshipId}: Material {MaterialId} → {RelatedEntityType} {RelatedEntityId} ({RelationshipType})",
                relationship.Id, relationship.MaterialId, relationship.RelatedEntityType,
                relationship.RelatedEntityId, relationship.RelationshipType);

            return relationship;
        }

        public async Task<bool> DeleteRelationshipAsync(int relationshipId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationship = await context.MaterialRelationships.FindAsync(relationshipId);
            if (relationship == null)
            {
                return false;
            }

            context.MaterialRelationships.Remove(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted relationship {RelationshipId}", relationshipId);

            return true;
        }

        public async Task<IEnumerable<MaterialRelationship>> GetMaterialRelationshipsAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.MaterialRelationships
                .Where(mr => mr.MaterialId == materialId)
                .OrderBy(mr => mr.DisplayOrder ?? int.MaxValue)
                .ToListAsync();
        }

        public async Task<IEnumerable<MaterialRelationship>> GetRelationshipsByTypeAsync(int materialId, string relatedEntityType)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.MaterialRelationships
                .Where(mr => mr.MaterialId == materialId && mr.RelatedEntityType == relatedEntityType)
                .OrderBy(mr => mr.DisplayOrder ?? int.MaxValue)
                .ToListAsync();
        }

        #endregion

        #region Learning Path Relationships

        public async Task<int> AssignMaterialToLearningPathAsync(int materialId, int learningPathId, string relationshipType = "contains", int? displayOrder = null)
        {
            var relationship = new MaterialRelationship
            {
                MaterialId = materialId,
                RelatedEntityId = learningPathId.ToString(),
                RelatedEntityType = "LearningPath",
                RelationshipType = relationshipType,
                DisplayOrder = displayOrder
            };

            var created = await CreateRelationshipAsync(relationship);
            return created.Id;
        }

        public async Task<bool> RemoveMaterialFromLearningPathAsync(int materialId, int learningPathId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationship = await context.MaterialRelationships
                .FirstOrDefaultAsync(mr => mr.MaterialId == materialId &&
                                         mr.RelatedEntityType == "LearningPath" &&
                                         mr.RelatedEntityId == learningPathId.ToString());

            if (relationship == null)
            {
                return false;
            }

            context.MaterialRelationships.Remove(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed material {MaterialId} from learning path {LearningPathId}",
                materialId, learningPathId);

            return true;
        }

        public async Task<IEnumerable<Material>> GetMaterialsByLearningPathAsync(int learningPathId, bool includeOrder = true)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var query = from mr in context.MaterialRelationships
                        join m in context.Materials on mr.MaterialId equals m.id
                        where mr.RelatedEntityType == "LearningPath" &&
                              mr.RelatedEntityId == learningPathId.ToString()
                        select new { Material = m, Relationship = mr };

            if (includeOrder)
            {
                query = query.OrderBy(x => x.Relationship.DisplayOrder ?? int.MaxValue);
            }

            var results = await query.ToListAsync();
            return results.Select(r => r.Material);
        }

        public async Task<bool> ReorderMaterialsInLearningPathAsync(int learningPathId, Dictionary<int, int> materialOrderMap)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.RelatedEntityType == "LearningPath" &&
                           mr.RelatedEntityId == learningPathId.ToString())
                .ToListAsync();

            foreach (var relationship in relationships)
            {
                if (materialOrderMap.TryGetValue(relationship.MaterialId, out int newOrder))
                {
                    relationship.DisplayOrder = newOrder;
                }
            }

            await context.SaveChangesAsync();

            _logger.LogInformation("Reordered {Count} materials in learning path {LearningPathId}",
                materialOrderMap.Count, learningPathId);

            return true;
        }

        #endregion

        #region Training Program Relationships

        public async Task<int> AssignMaterialToTrainingProgramAsync(int materialId, int trainingProgramId, string relationshipType = "assigned")
        {
            var relationship = new MaterialRelationship
            {
                MaterialId = materialId,
                RelatedEntityId = trainingProgramId.ToString(),
                RelatedEntityType = "TrainingProgram",
                RelationshipType = relationshipType
            };

            var created = await CreateRelationshipAsync(relationship);
            return created.Id;
        }

        public async Task<bool> RemoveMaterialFromTrainingProgramAsync(int materialId, int trainingProgramId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationship = await context.MaterialRelationships
                .FirstOrDefaultAsync(mr => mr.MaterialId == materialId &&
                                         mr.RelatedEntityType == "TrainingProgram" &&
                                         mr.RelatedEntityId == trainingProgramId.ToString());

            if (relationship == null)
            {
                return false;
            }

            context.MaterialRelationships.Remove(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed material {MaterialId} from training program {TrainingProgramId}",
                materialId, trainingProgramId);

            return true;
        }

        public async Task<IEnumerable<Material>> GetMaterialsByTrainingProgramAsync(int trainingProgramId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await (from mr in context.MaterialRelationships
                          join m in context.Materials on mr.MaterialId equals m.id
                          where mr.RelatedEntityType == "TrainingProgram" &&
                                mr.RelatedEntityId == trainingProgramId.ToString()
                          select m).ToListAsync();
        }

        #endregion

        #region Material Dependencies (Prerequisites)

        public async Task<int> CreateMaterialDependencyAsync(int materialId, int prerequisiteMaterialId, string relationshipType = "prerequisite")
        {
            var relationship = new MaterialRelationship
            {
                MaterialId = materialId,
                RelatedEntityId = prerequisiteMaterialId.ToString(),
                RelatedEntityType = "Material",
                RelationshipType = relationshipType
            };

            var created = await CreateRelationshipAsync(relationship);
            return created.Id;
        }

        public async Task<bool> RemoveMaterialDependencyAsync(int materialId, int prerequisiteMaterialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationship = await context.MaterialRelationships
                .FirstOrDefaultAsync(mr => mr.MaterialId == materialId &&
                                         mr.RelatedEntityType == "Material" &&
                                         mr.RelatedEntityId == prerequisiteMaterialId.ToString());

            if (relationship == null)
            {
                return false;
            }

            context.MaterialRelationships.Remove(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed dependency: Material {MaterialId} no longer requires Material {PrerequisiteId}",
                materialId, prerequisiteMaterialId);

            return true;
        }

        public async Task<IEnumerable<Material>> GetMaterialPrerequisitesAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == materialId &&
                             mr.RelatedEntityType == "Material" &&
                             mr.RelationshipType == "prerequisite")
                .ToListAsync();

            var prerequisiteIds = relationships
                .Select(mr => int.TryParse(mr.RelatedEntityId, out int id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

            if (!prerequisiteIds.Any())
                return Enumerable.Empty<Material>();

            return await context.Materials
                .Where(m => prerequisiteIds.Contains(m.id))
                .ToListAsync();
        }

        public async Task<IEnumerable<Material>> GetMaterialDependentsAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await (from mr in context.MaterialRelationships
                          join m in context.Materials on mr.MaterialId equals m.id
                          where mr.RelatedEntityType == "Material" &&
                                mr.RelatedEntityId == materialId.ToString() &&
                                mr.RelationshipType == "prerequisite"
                          select m).ToListAsync();
        }

        #endregion

        #region Material-to-Material Hierarchical Relationships

        public async Task<int> AssignMaterialToMaterialAsync(int parentMaterialId, int childMaterialId,
            string relationshipType = "contains", int? displayOrder = null)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Validate both materials exist
                var parentMaterial = await context.Materials.FindAsync(parentMaterialId);
                var childMaterial = await context.Materials.FindAsync(childMaterialId);

                if (parentMaterial == null)
                    throw new ArgumentException($"Parent material with ID {parentMaterialId} not found");
                if (childMaterial == null)
                    throw new ArgumentException($"Child material with ID {childMaterialId} not found");

                // Check for circular reference using the same context to see uncommitted changes
                if (await CheckCircularReference(context, childMaterialId, parentMaterialId, new HashSet<int>()))
                    throw new InvalidOperationException("Assignment would create a circular reference");

                // Check if relationship already exists
                var existingRelationship = await context.MaterialRelationships
                    .FirstOrDefaultAsync(mr => mr.MaterialId == parentMaterialId &&
                                             mr.RelatedEntityType == "Material" &&
                                             mr.RelatedEntityId == childMaterialId.ToString() &&
                                             mr.RelationshipType == relationshipType);

                if (existingRelationship != null)
                    throw new InvalidOperationException("Relationship already exists");

                // If no display order specified, set to next available
                if (displayOrder == null)
                {
                    var maxOrder = await context.MaterialRelationships
                        .Where(mr => mr.MaterialId == parentMaterialId &&
                                   mr.RelatedEntityType == "Material")
                        .MaxAsync(mr => (int?)mr.DisplayOrder) ?? 0;
                    displayOrder = maxOrder + 1;
                }

                var relationship = new MaterialRelationship
                {
                    MaterialId = parentMaterialId,
                    RelatedEntityId = childMaterialId.ToString(),
                    RelatedEntityType = "Material",
                    RelationshipType = relationshipType,
                    DisplayOrder = displayOrder
                };

                context.MaterialRelationships.Add(relationship);
                await context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation("Assigned material {ChildId} to material {ParentId} with relationship {RelationshipType} (ID: {RelationshipId})",
                    childMaterialId, parentMaterialId, relationshipType, relationship.Id);

                return relationship.Id;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to assign material {ChildId} to material {ParentId} - Transaction rolled back",
                    childMaterialId, parentMaterialId);
                throw;
            }
        }

        public async Task<bool> RemoveMaterialFromMaterialAsync(int parentMaterialId, int childMaterialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationship = await context.MaterialRelationships
                .FirstOrDefaultAsync(mr => mr.MaterialId == parentMaterialId &&
                                         mr.RelatedEntityType == "Material" &&
                                         mr.RelatedEntityId == childMaterialId.ToString());

            if (relationship == null)
                return false;

            context.MaterialRelationships.Remove(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed material {ChildId} from material {ParentId}",
                childMaterialId, parentMaterialId);

            return true;
        }

        public async Task<IEnumerable<Material>> GetChildMaterialsAsync(int parentMaterialId,
            bool includeOrder = true, string? relationshipType = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationshipsQuery = context.MaterialRelationships
                .Where(mr => mr.MaterialId == parentMaterialId &&
                             mr.RelatedEntityType == "Material" &&
                             (relationshipType == null || mr.RelationshipType == relationshipType));

            if (includeOrder)
            {
                relationshipsQuery = relationshipsQuery.OrderBy(mr => mr.DisplayOrder ?? int.MaxValue);
            }

            var relationships = await relationshipsQuery.ToListAsync();

            var childIds = relationships
                .Select(mr => int.TryParse(mr.RelatedEntityId, out int id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

            if (!childIds.Any())
                return Enumerable.Empty<Material>();

            var materials = await context.Materials
                .Where(m => childIds.Contains(m.id))
                .ToListAsync();

            if (includeOrder)
            {
                var materialDict = materials.ToDictionary(m => m.id);
                return childIds
                    .Where(id => materialDict.ContainsKey(id))
                    .Select(id => materialDict[id]);
            }

            return materials;
        }

        public async Task<IEnumerable<Material>> GetParentMaterialsAsync(int childMaterialId,
            string? relationshipType = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await (from mr in context.MaterialRelationships
                          join m in context.Materials on mr.MaterialId equals m.id
                          where mr.RelatedEntityType == "Material" &&
                                mr.RelatedEntityId == childMaterialId.ToString() &&
                                (relationshipType == null || mr.RelationshipType == relationshipType)
                          select m).ToListAsync();
        }

        public async Task<bool> ReorderChildMaterialsAsync(int parentMaterialId, Dictionary<int, int> materialOrderMap)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == parentMaterialId &&
                           mr.RelatedEntityType == "Material")
                .ToListAsync();

            foreach (var relationship in relationships)
            {
                if (int.TryParse(relationship.RelatedEntityId, out int childMaterialId) &&
                    materialOrderMap.TryGetValue(childMaterialId, out int newOrder))
                {
                    relationship.DisplayOrder = newOrder;
                }
            }

            await context.SaveChangesAsync();

            _logger.LogInformation("Reordered {Count} child materials for parent material {ParentId}",
                materialOrderMap.Count, parentMaterialId);

            return true;
        }

        public async Task<bool> WouldCreateCircularReferenceAsync(int parentMaterialId, int childMaterialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await CheckCircularReference(context, childMaterialId, parentMaterialId, new HashSet<int>());
        }

        private async Task<bool> CheckCircularReference(XR50TrainingContext context, int currentParentId,
            int targetChildId, HashSet<int> visited)
        {
            if (visited.Contains(currentParentId)) return true;
            if (currentParentId == targetChildId) return true;

            visited.Add(currentParentId);

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == currentParentId &&
                           mr.RelatedEntityType == "Material")
                .ToListAsync();

            var children = relationships
                .Select(mr => int.TryParse(mr.RelatedEntityId, out int id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

            foreach (var childId in children)
            {
                if (await CheckCircularReference(context, childId, targetChildId, new HashSet<int>(visited)))
                    return true;
            }

            return false;
        }

        public async Task<MaterialHierarchy> GetMaterialHierarchyAsync(int rootMaterialId, int maxDepth = 5)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var rootMaterial = await context.Materials.FindAsync(rootMaterialId);
            if (rootMaterial == null)
                throw new ArgumentException($"Root material with ID {rootMaterialId} not found");

            var hierarchy = new MaterialHierarchy
            {
                RootMaterial = rootMaterial
            };

            await BuildHierarchyRecursive(context, hierarchy.Children, rootMaterialId, 0, maxDepth);

            hierarchy.TotalDepth = CalculateMaxDepth(hierarchy.Children);
            hierarchy.TotalMaterials = CountTotalMaterials(hierarchy.Children) + 1;

            return hierarchy;
        }

        private async Task BuildHierarchyRecursive(XR50TrainingContext context, List<MaterialHierarchyNode> nodes,
            int parentId, int currentDepth, int maxDepth)
        {
            if (currentDepth >= maxDepth) return;

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == parentId &&
                             mr.RelatedEntityType == "Material")
                .OrderBy(mr => mr.DisplayOrder ?? int.MaxValue)
                .ToListAsync();

            var childIds = relationships
                .Select(mr => int.TryParse(mr.RelatedEntityId, out int id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

            if (!childIds.Any()) return;

            var materials = await context.Materials
                .Where(m => childIds.Contains(m.id))
                .ToListAsync();

            var materialDict = materials.ToDictionary(m => m.id);

            var childRelationships = new List<(Material Material, MaterialRelationship Relationship)>();
            foreach (var rel in relationships)
            {
                if (int.TryParse(rel.RelatedEntityId, out int id) && materialDict.ContainsKey(id))
                {
                    childRelationships.Add((materialDict[id], rel));
                }
            }

            foreach (var item in childRelationships)
            {
                var node = new MaterialHierarchyNode
                {
                    Material = item.Material,
                    RelationshipType = item.Relationship.RelationshipType ?? "contains",
                    DisplayOrder = item.Relationship.DisplayOrder,
                    Depth = currentDepth + 1
                };

                await BuildHierarchyRecursive(context, node.Children, item.Material.id, currentDepth + 1, maxDepth);
                nodes.Add(node);
            }
        }

        private int CalculateMaxDepth(List<MaterialHierarchyNode> nodes)
        {
            if (!nodes.Any()) return 0;
            return 1 + nodes.Max(n => CalculateMaxDepth(n.Children));
        }

        private int CountTotalMaterials(List<MaterialHierarchyNode> nodes)
        {
            return nodes.Count + nodes.Sum(n => CountTotalMaterials(n.Children));
        }

        #endregion

        #region Subcomponent-to-Material Relationships

        public async Task<int> AssignMaterialToSubcomponentAsync(
            int subcomponentId,
            string subcomponentType,
            int materialId,
            string? relationshipType = null,
            int? displayOrder = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            if (!ValidSubcomponentTypes.Contains(subcomponentType))
                throw new ArgumentException($"Invalid subcomponent type: {subcomponentType}");

            var subcomponentExists = await ValidateSubcomponentExistsAsync(context, subcomponentId, subcomponentType);
            if (!subcomponentExists)
                throw new ArgumentException($"{subcomponentType} with ID {subcomponentId} not found");

            var material = await context.Materials.FindAsync(materialId);
            if (material == null)
                throw new ArgumentException($"Material with ID {materialId} not found");

            var existingRelationship = await context.SubcomponentMaterialRelationships
                .FirstOrDefaultAsync(smr => smr.SubcomponentId == subcomponentId &&
                                           smr.SubcomponentType == subcomponentType &&
                                           smr.RelatedMaterialId == materialId);

            if (existingRelationship != null)
                throw new InvalidOperationException("Relationship already exists");

            if (displayOrder == null)
            {
                var maxOrder = await context.SubcomponentMaterialRelationships
                    .Where(smr => smr.SubcomponentId == subcomponentId &&
                                 smr.SubcomponentType == subcomponentType)
                    .MaxAsync(smr => (int?)smr.DisplayOrder) ?? 0;
                displayOrder = maxOrder + 1;
            }

            var relationship = new SubcomponentMaterialRelationship
            {
                SubcomponentId = subcomponentId,
                SubcomponentType = subcomponentType,
                RelatedMaterialId = materialId,
                RelationshipType = relationshipType,
                DisplayOrder = displayOrder
            };

            context.SubcomponentMaterialRelationships.Add(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Assigned material {MaterialId} to {SubcomponentType} {SubcomponentId} (Relationship ID: {RelationshipId})",
                materialId, subcomponentType, subcomponentId, relationship.Id);

            return relationship.Id;
        }

        private async Task<bool> ValidateSubcomponentExistsAsync(XR50TrainingContext context, int subcomponentId, string subcomponentType)
        {
            return subcomponentType switch
            {
                "ChecklistEntry" => await context.Entries.AnyAsync(e => e.ChecklistEntryId == subcomponentId),
                "WorkflowStep" => await context.WorkflowSteps.AnyAsync(w => w.Id == subcomponentId),
                "QuestionnaireEntry" => await context.QuestionnaireEntries.AnyAsync(q => q.QuestionnaireEntryId == subcomponentId),
                "VideoTimestamp" => await context.Timestamps.AnyAsync(v => v.id == subcomponentId),
                "QuizQuestion" => await context.QuizQuestions.AnyAsync(q => q.QuizQuestionId == subcomponentId),
                "QuizAnswer" => await context.QuizAnswers.AnyAsync(a => a.QuizAnswerId == subcomponentId),
                "ImageAnnotation" => await context.ImageAnnotations.AnyAsync(a => a.ImageAnnotationId == subcomponentId),
                _ => false
            };
        }

        public async Task<bool> RemoveMaterialFromSubcomponentAsync(
            int subcomponentId,
            string subcomponentType,
            int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationship = await context.SubcomponentMaterialRelationships
                .FirstOrDefaultAsync(smr => smr.SubcomponentId == subcomponentId &&
                                           smr.SubcomponentType == subcomponentType &&
                                           smr.RelatedMaterialId == materialId);

            if (relationship == null)
                return false;

            context.SubcomponentMaterialRelationships.Remove(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed material {MaterialId} from {SubcomponentType} {SubcomponentId}",
                materialId, subcomponentType, subcomponentId);

            return true;
        }

        public async Task<IEnumerable<Material>> GetMaterialsForSubcomponentAsync(
            int subcomponentId,
            string subcomponentType,
            bool includeOrder = true)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationshipsQuery = context.SubcomponentMaterialRelationships
                .Where(smr => smr.SubcomponentId == subcomponentId &&
                             smr.SubcomponentType == subcomponentType);

            if (includeOrder)
            {
                relationshipsQuery = relationshipsQuery.OrderBy(smr => smr.DisplayOrder ?? int.MaxValue);
            }

            var relationships = await relationshipsQuery.ToListAsync();
            var materialIds = relationships.Select(r => r.RelatedMaterialId).ToList();

            if (!materialIds.Any())
                return Enumerable.Empty<Material>();

            var materials = await context.Materials
                .Where(m => materialIds.Contains(m.id))
                .ToListAsync();

            if (includeOrder)
            {
                var materialDict = materials.ToDictionary(m => m.id);
                return materialIds
                    .Where(id => materialDict.ContainsKey(id))
                    .Select(id => materialDict[id]);
            }

            return materials;
        }

        public async Task<bool> ReorderSubcomponentMaterialsAsync(
            int subcomponentId,
            string subcomponentType,
            Dictionary<int, int> materialOrderMap)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var relationships = await context.SubcomponentMaterialRelationships
                .Where(smr => smr.SubcomponentId == subcomponentId &&
                             smr.SubcomponentType == subcomponentType)
                .ToListAsync();

            foreach (var relationship in relationships)
            {
                if (materialOrderMap.TryGetValue(relationship.RelatedMaterialId, out int newOrder))
                {
                    relationship.DisplayOrder = newOrder;
                }
            }

            await context.SaveChangesAsync();

            _logger.LogInformation("Reordered {Count} materials for {SubcomponentType} {SubcomponentId}",
                materialOrderMap.Count, subcomponentType, subcomponentId);

            return true;
        }

        public async Task<IEnumerable<SubcomponentMaterialRelationship>> GetSubcomponentRelationshipsAsync(
            int subcomponentId,
            string subcomponentType)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.SubcomponentMaterialRelationships
                .Include(smr => smr.RelatedMaterial)
                .Where(smr => smr.SubcomponentId == subcomponentId &&
                             smr.SubcomponentType == subcomponentType)
                .OrderBy(smr => smr.DisplayOrder)
                .ToListAsync();
        }

        #endregion

        #region Convenience Methods

        public Task<int> AssignMaterialToChecklistEntryAsync(int entryId, int materialId, string? relationshipType = null, int? displayOrder = null)
            => AssignMaterialToSubcomponentAsync(entryId, "ChecklistEntry", materialId, relationshipType, displayOrder);

        public Task<int> AssignMaterialToWorkflowStepAsync(int stepId, int materialId, string? relationshipType = null, int? displayOrder = null)
            => AssignMaterialToSubcomponentAsync(stepId, "WorkflowStep", materialId, relationshipType, displayOrder);

        public Task<int> AssignMaterialToQuestionnaireEntryAsync(int entryId, int materialId, string? relationshipType = null, int? displayOrder = null)
            => AssignMaterialToSubcomponentAsync(entryId, "QuestionnaireEntry", materialId, relationshipType, displayOrder);

        public Task<int> AssignMaterialToVideoTimestampAsync(int timestampId, int materialId, string? relationshipType = null, int? displayOrder = null)
            => AssignMaterialToSubcomponentAsync(timestampId, "VideoTimestamp", materialId, relationshipType, displayOrder);

        public Task<int> AssignMaterialToQuizQuestionAsync(int questionId, int materialId, string? relationshipType = null, int? displayOrder = null)
            => AssignMaterialToSubcomponentAsync(questionId, "QuizQuestion", materialId, relationshipType, displayOrder);

        public Task<int> AssignMaterialToImageAnnotationAsync(int annotationId, int materialId, string? relationshipType = null, int? displayOrder = null)
            => AssignMaterialToSubcomponentAsync(annotationId, "ImageAnnotation", materialId, relationshipType, displayOrder);

        public Task<IEnumerable<Material>> GetMaterialsForChecklistEntryAsync(int entryId, bool includeOrder = true)
            => GetMaterialsForSubcomponentAsync(entryId, "ChecklistEntry", includeOrder);

        public Task<IEnumerable<Material>> GetMaterialsForWorkflowStepAsync(int stepId, bool includeOrder = true)
            => GetMaterialsForSubcomponentAsync(stepId, "WorkflowStep", includeOrder);

        public Task<IEnumerable<Material>> GetMaterialsForQuestionnaireEntryAsync(int entryId, bool includeOrder = true)
            => GetMaterialsForSubcomponentAsync(entryId, "QuestionnaireEntry", includeOrder);

        public Task<IEnumerable<Material>> GetMaterialsForVideoTimestampAsync(int timestampId, bool includeOrder = true)
            => GetMaterialsForSubcomponentAsync(timestampId, "VideoTimestamp", includeOrder);

        public Task<IEnumerable<Material>> GetMaterialsForQuizQuestionAsync(int questionId, bool includeOrder = true)
            => GetMaterialsForSubcomponentAsync(questionId, "QuizQuestion", includeOrder);

        public Task<IEnumerable<Material>> GetMaterialsForImageAnnotationAsync(int annotationId, bool includeOrder = true)
            => GetMaterialsForSubcomponentAsync(annotationId, "ImageAnnotation", includeOrder);

        public Task<bool> RemoveMaterialFromChecklistEntryAsync(int entryId, int materialId)
            => RemoveMaterialFromSubcomponentAsync(entryId, "ChecklistEntry", materialId);

        public Task<bool> RemoveMaterialFromWorkflowStepAsync(int stepId, int materialId)
            => RemoveMaterialFromSubcomponentAsync(stepId, "WorkflowStep", materialId);

        public Task<bool> RemoveMaterialFromQuestionnaireEntryAsync(int entryId, int materialId)
            => RemoveMaterialFromSubcomponentAsync(entryId, "QuestionnaireEntry", materialId);

        public Task<bool> RemoveMaterialFromVideoTimestampAsync(int timestampId, int materialId)
            => RemoveMaterialFromSubcomponentAsync(timestampId, "VideoTimestamp", materialId);

        public Task<bool> RemoveMaterialFromQuizQuestionAsync(int questionId, int materialId)
            => RemoveMaterialFromSubcomponentAsync(questionId, "QuizQuestion", materialId);

        #endregion
    }
}
