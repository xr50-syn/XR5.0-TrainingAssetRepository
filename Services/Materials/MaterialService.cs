using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Base material service providing CRUD operations.
    /// Type-specific operations are in separate services.
    /// </summary>
    public class MaterialServiceBase : IMaterialServiceBase
    {
        protected readonly IXR50TenantDbContextFactory _dbContextFactory;
        protected readonly ILogger<MaterialServiceBase> _logger;

        public MaterialServiceBase(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<MaterialServiceBase> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Basic CRUD Operations

        public async Task<IEnumerable<Material>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.ToListAsync();
        }

        public async Task<Material?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.FindAsync(id);
        }

        public async Task<T?> GetByIdAsync<T>(int id) where T : Material
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<T>().FirstOrDefaultAsync(m => m.id == id);
        }

        public async Task<Material> CreateAsync(Material material)
        {
            using var context = _dbContextFactory.CreateDbContext();
            material.Created_at = DateTime.UtcNow;
            material.Updated_at = DateTime.UtcNow;
            SetMaterialTypeFromClass(material);

            context.Materials.Add(material);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created material: {Name} (Type: {Type}) with ID: {Id}",
                material.Name, material.Type, material.id);

            return material;
        }

        public async Task<Material> CreateCompleteAsync(Material material)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("Creating complete material: {Name} (Type: {Type})",
                    material.Name, material.GetType().Name);

                material.Created_at = DateTime.UtcNow;
                material.Updated_at = DateTime.UtcNow;
                SetMaterialTypeFromClass(material);

                context.Materials.Add(material);
                await context.SaveChangesAsync();

                _logger.LogInformation("Created material with ID: {Id}", material.id);

                // Process child entities based on material type
                await ProcessChildEntitiesAsync(context, material);

                await transaction.CommitAsync();
                _logger.LogInformation("Completed creation of material {Id} ({Name})", material.id, material.Name);

                return material;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create material {Name} - Transaction rolled back", material.Name);
                throw;
            }
        }

        public async Task<Material> CreateFromJsonAsync(JsonElement materialData)
        {
            // Helper method to get property case-insensitively
            bool TryGetPropertyCI(JsonElement element, string propertyName, out JsonElement value)
            {
                if (element.TryGetProperty(propertyName, out value)) return true;
                if (element.TryGetProperty(propertyName.ToLower(), out value)) return true;
                if (element.TryGetProperty(char.ToUpper(propertyName[0]) + propertyName.Substring(1), out value)) return true;
                return false;
            }

            // Determine material type
            string materialType = "default";
            if (TryGetPropertyCI(materialData, "type", out var typeProp))
            {
                materialType = typeProp.GetString()?.ToLower() ?? "default";
            }
            else if (TryGetPropertyCI(materialData, "materialType", out var materialTypeProp))
            {
                materialType = materialTypeProp.GetString()?.ToLower() ?? "default";
            }

            _logger.LogInformation("Creating material of type: {Type} from JSON", materialType);

            // Create appropriate material based on type
            Material material = materialType switch
            {
                "workflow" => ParseWorkflowFromJson(materialData, TryGetPropertyCI),
                "checklist" => ParseChecklistFromJson(materialData, TryGetPropertyCI),
                "video" => ParseVideoFromJson(materialData, TryGetPropertyCI),
                "questionnaire" or "quiz" => ParseQuestionnaireFromJson(materialData, TryGetPropertyCI),
                "image" => ParseImageFromJson(materialData, TryGetPropertyCI),
                "pdf" => ParsePDFFromJson(materialData, TryGetPropertyCI),
                "unity" or "unitydemo" => ParseUnityFromJson(materialData, TryGetPropertyCI),
                "chatbot" => ParseChatbotFromJson(materialData, TryGetPropertyCI),
                "mqtt_template" or "mqtt" => ParseMQTTFromJson(materialData, TryGetPropertyCI),
                _ => ParseDefaultFromJson(materialData, TryGetPropertyCI)
            };

            // Create the material using the complete method
            return await CreateCompleteAsync(material);
        }

        #region JSON Parsing Helpers

        private delegate bool TryGetPropertyDelegate(JsonElement element, string propertyName, out JsonElement value);

        private Material ParseWorkflowFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var workflow = new WorkflowMaterial();
            SetCommonPropertiesFromJson(workflow, json, tryGet);

            if (tryGet(json, "steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array)
            {
                workflow.WorkflowSteps = new List<WorkflowStep>();
                foreach (var step in stepsProp.EnumerateArray())
                {
                    workflow.WorkflowSteps.Add(new WorkflowStep
                    {
                        Title = tryGet(step, "title", out var title) ? title.GetString() ?? "" : "",
                        Content = tryGet(step, "content", out var content) ? content.GetString() : null
                    });
                }
            }

            return workflow;
        }

        private Material ParseChecklistFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var checklist = new ChecklistMaterial();
            SetCommonPropertiesFromJson(checklist, json, tryGet);

            if (tryGet(json, "entries", out var entriesProp) && entriesProp.ValueKind == JsonValueKind.Array)
            {
                checklist.Entries = new List<ChecklistEntry>();
                foreach (var entry in entriesProp.EnumerateArray())
                {
                    checklist.Entries.Add(new ChecklistEntry
                    {
                        Text = tryGet(entry, "text", out var text) ? text.GetString() ?? "" : "",
                        Description = tryGet(entry, "description", out var desc) ? desc.GetString() : null
                    });
                }
            }

            return checklist;
        }

        private Material ParseVideoFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var video = new VideoMaterial();
            SetCommonPropertiesFromJson(video, json, tryGet);

            if (tryGet(json, "assetId", out var assetId)) video.AssetId = assetId.GetInt32();
            if (tryGet(json, "videoPath", out var path)) video.VideoPath = path.GetString();
            if (tryGet(json, "videoDuration", out var duration)) video.VideoDuration = duration.GetInt32();
            if (tryGet(json, "videoResolution", out var res)) video.VideoResolution = res.GetString();
            if (tryGet(json, "startTime", out var startTime)) video.startTime = startTime.GetString();
            if (tryGet(json, "annotations", out var annotations)) video.Annotations = annotations.GetRawText();

            return video;
        }

        private Material ParseQuestionnaireFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var questionnaire = new QuestionnaireMaterial();
            SetCommonPropertiesFromJson(questionnaire, json, tryGet);

            if (tryGet(json, "questions", out var questionsProp) && questionsProp.ValueKind == JsonValueKind.Array)
            {
                questionnaire.QuestionnaireEntries = new List<QuestionnaireEntry>();
                foreach (var q in questionsProp.EnumerateArray())
                {
                    questionnaire.QuestionnaireEntries.Add(new QuestionnaireEntry
                    {
                        Text = tryGet(q, "question", out var question) ? question.GetString() ?? "" :
                               (tryGet(q, "text", out var text) ? text.GetString() ?? "" : ""),
                        Description = tryGet(q, "description", out var desc) ? desc.GetString() : null
                    });
                }
            }

            return questionnaire;
        }

        private Material ParseImageFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var image = new ImageMaterial();
            SetCommonPropertiesFromJson(image, json, tryGet);

            if (tryGet(json, "assetId", out var assetId)) image.AssetId = assetId.GetInt32();
            if (tryGet(json, "imagePath", out var path)) image.ImagePath = path.GetString();

            return image;
        }

        private Material ParsePDFFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var pdf = new PDFMaterial();
            SetCommonPropertiesFromJson(pdf, json, tryGet);

            if (tryGet(json, "assetId", out var assetId)) pdf.AssetId = assetId.GetInt32();

            return pdf;
        }

        private Material ParseUnityFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var unity = new UnityMaterial();
            SetCommonPropertiesFromJson(unity, json, tryGet);

            if (tryGet(json, "assetId", out var assetId)) unity.AssetId = assetId.GetInt32();

            return unity;
        }

        private Material ParseChatbotFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var chatbot = new ChatbotMaterial();
            SetCommonPropertiesFromJson(chatbot, json, tryGet);

            if (tryGet(json, "chatbotConfig", out var config)) chatbot.ChatbotConfig = config.GetString();

            return chatbot;
        }

        private Material ParseMQTTFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var mqtt = new MQTT_TemplateMaterial();
            SetCommonPropertiesFromJson(mqtt, json, tryGet);

            if (tryGet(json, "messageType", out var msgType)) mqtt.message_type = msgType.GetString();
            if (tryGet(json, "messageText", out var msgText)) mqtt.message_text = msgText.GetString();

            return mqtt;
        }

        private Material ParseDefaultFromJson(JsonElement json, TryGetPropertyDelegate tryGet)
        {
            var defaultMat = new DefaultMaterial();
            SetCommonPropertiesFromJson(defaultMat, json, tryGet);

            if (tryGet(json, "assetId", out var assetId)) defaultMat.AssetId = assetId.GetInt32();

            return defaultMat;
        }

        private void SetCommonPropertiesFromJson(Material material, JsonElement json, TryGetPropertyDelegate tryGet)
        {
            if (tryGet(json, "name", out var name)) material.Name = name.GetString() ?? "";
            if (tryGet(json, "description", out var desc)) material.Description = desc.GetString();
            if (tryGet(json, "unique_id", out var unique_id) && unique_id.ValueKind == JsonValueKind.Number)
                material.Unique_id = unique_id.GetInt32();
        }

        #endregion

        public async Task<Material> UpdateAsync(Material material)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var existing = await context.Materials.FindAsync(material.id);
                if (existing == null)
                {
                    throw new KeyNotFoundException($"Material {material.id} not found");
                }

                if (existing.GetType() != material.GetType())
                {
                    throw new InvalidOperationException(
                        $"Cannot change material type from {existing.GetType().Name} to {material.GetType().Name}");
                }

                var createdAt = existing.Created_at;
                var uniqueId = existing.Unique_id;
                var existingAssetId = GetAssetId(existing);

                await DeleteChildEntriesAsync(context, material.id, existing.GetType());

                context.Materials.Remove(existing);
                await context.SaveChangesAsync();

                material.id = existing.id;
                material.Created_at = createdAt;
                material.Updated_at = DateTime.UtcNow;
                material.Unique_id = uniqueId;
                SetMaterialTypeFromClass(material);

                if (existingAssetId.HasValue && GetAssetId(material) == null)
                {
                    SetAssetId(material, existingAssetId.Value);
                }

                context.Materials.Add(material);
                await context.SaveChangesAsync();

                await ProcessChildEntitiesAsync(context, material);

                await transaction.CommitAsync();
                _logger.LogInformation("Updated material: {Id} ({Name})", material.id, material.Name);

                return material;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update material {Id}", material.id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials.FindAsync(id);
            if (material == null)
            {
                return false;
            }

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();

            context.MaterialRelationships.RemoveRange(relationships);
            context.Materials.Remove(material);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted material: {Id} (Type: {Type}) and {RelationshipCount} relationships",
                id, material.GetType().Name, relationships.Count);

            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.AnyAsync(e => e.id == id);
        }

        #endregion

        #region Type Filtering

        public async Task<IEnumerable<T>> GetAllOfTypeAsync<T>() where T : Material
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<T>().ToListAsync();
        }

        public async Task<IEnumerable<Material>> GetByTypeAsync(System.Type materialType)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var materials = await context.Materials.ToListAsync();
            return materials.Where(m => m.GetType() == materialType);
        }

        #endregion

        #region Complete Material Details

        public async Task<object?> GetCompleteDetailsAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var baseMaterial = await context.Materials.FindAsync(materialId);
            if (baseMaterial == null)
            {
                return null;
            }

            // This returns basic material info - type-specific services provide detailed views
            return baseMaterial;
        }

        public async Task<Material?> GetCompleteAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.FindAsync(materialId);
        }

        #endregion

        #region Asset Relationships

        public async Task<IEnumerable<Material>> GetByAssetIdAsync(int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var result = new List<Material>();

            result.AddRange(await context.Materials.OfType<VideoMaterial>()
                .Where(m => m.AssetId == assetId).ToListAsync());
            result.AddRange(await context.Materials.OfType<ImageMaterial>()
                .Where(m => m.AssetId == assetId).ToListAsync());
            result.AddRange(await context.Materials.OfType<PDFMaterial>()
                .Where(m => m.AssetId == assetId).ToListAsync());
            result.AddRange(await context.Materials.OfType<UnityMaterial>()
                .Where(m => m.AssetId == assetId).ToListAsync());
            result.AddRange(await context.Materials.OfType<DefaultMaterial>()
                .Where(m => m.AssetId == assetId).ToListAsync());

            return result;
        }

        public async Task<bool> AssignAssetAsync(int materialId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials.FindAsync(materialId);
            if (material == null) return false;

            SetAssetId(material, assetId);
            material.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();
            _logger.LogInformation("Assigned asset {AssetId} to material {MaterialId}", assetId, materialId);

            return true;
        }

        public async Task<bool> RemoveAssetAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials.FindAsync(materialId);
            if (material == null) return false;

            SetAssetId(material, null);
            material.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();
            _logger.LogInformation("Removed asset from material {MaterialId}", materialId);

            return true;
        }

        public async Task<int?> GetAssetIdAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var material = await context.Materials.FindAsync(materialId);
            return material != null ? GetAssetId(material) : null;
        }

        #endregion

        #region Helper Methods

        protected void SetMaterialTypeFromClass(Material material)
        {
            material.Type = material switch
            {
                VideoMaterial => MaterialType.Video,
                ImageMaterial => MaterialType.Image,
                ChecklistMaterial => MaterialType.Checklist,
                WorkflowMaterial => MaterialType.Workflow,
                PDFMaterial => MaterialType.PDF,
                UnityMaterial => MaterialType.Unity,
                ChatbotMaterial => MaterialType.Chatbot,
                QuestionnaireMaterial => MaterialType.Questionnaire,
                MQTT_TemplateMaterial => MaterialType.MQTT_Template,
                QuizMaterial => MaterialType.Quiz,
                DefaultMaterial => MaterialType.Default,
                _ => MaterialType.Default
            };
        }

        protected int? GetAssetId(Material material)
        {
            return material switch
            {
                VideoMaterial v => v.AssetId,
                ImageMaterial i => i.AssetId,
                PDFMaterial p => p.AssetId,
                UnityMaterial u => u.AssetId,
                DefaultMaterial d => d.AssetId,
                _ => null
            };
        }

        protected void SetAssetId(Material material, int? assetId)
        {
            switch (material)
            {
                case VideoMaterial v: v.AssetId = assetId; break;
                case ImageMaterial i: i.AssetId = assetId; break;
                case PDFMaterial p: p.AssetId = assetId; break;
                case UnityMaterial u: u.AssetId = assetId; break;
                case DefaultMaterial d: d.AssetId = assetId; break;
            }
        }

        protected async Task ProcessChildEntitiesAsync(XR50TrainingContext context, Material material)
        {
            switch (material)
            {
                case WorkflowMaterial workflow when workflow.WorkflowSteps?.Any() == true:
                    foreach (var step in workflow.WorkflowSteps.ToList())
                    {
                        var newStep = new WorkflowStep
                        {
                            Title = step.Title,
                            Content = step.Content,
                            WorkflowMaterialId = material.id
                        };
                        context.WorkflowSteps.Add(newStep);
                    }
                    await context.SaveChangesAsync();
                    break;

                case ChecklistMaterial checklist when checklist.Entries?.Any() == true:
                    foreach (var entry in checklist.Entries.ToList())
                    {
                        var newEntry = new ChecklistEntry
                        {
                            Text = entry.Text,
                            Description = entry.Description,
                            ChecklistMaterialId = material.id
                        };
                        context.Entries.Add(newEntry);
                    }
                    await context.SaveChangesAsync();
                    break;

                case VideoMaterial video:
                    _logger.LogInformation("Video material has {Count} timestamps (saved via navigation property)",
                        video.Timestamps?.Count ?? 0);
                    break;

                case QuestionnaireMaterial questionnaire when questionnaire.QuestionnaireEntries?.Any() == true:
                    foreach (var entry in questionnaire.QuestionnaireEntries.ToList())
                    {
                        var newEntry = new QuestionnaireEntry
                        {
                            Text = entry.Text,
                            Description = entry.Description,
                            QuestionnaireMaterialId = material.id
                        };
                        context.QuestionnaireEntries.Add(newEntry);
                    }
                    await context.SaveChangesAsync();
                    break;

                case QuizMaterial quiz when quiz.Questions?.Any() == true:
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
                            QuizMaterialId = material.id
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
                    break;
            }
        }

        protected async Task DeleteChildEntriesAsync(XR50TrainingContext context, int materialId, System.Type materialType)
        {
            if (materialType == typeof(ChecklistMaterial))
            {
                var entries = await context.Entries.Where(e => e.ChecklistMaterialId == materialId).ToListAsync();
                context.Entries.RemoveRange(entries);
            }
            else if (materialType == typeof(WorkflowMaterial))
            {
                var steps = await context.WorkflowSteps.Where(s => s.WorkflowMaterialId == materialId).ToListAsync();
                context.WorkflowSteps.RemoveRange(steps);
            }
            else if (materialType == typeof(VideoMaterial))
            {
                var timestamps = await context.Timestamps.Where(t => t.VideoMaterialId == materialId).ToListAsync();
                context.Timestamps.RemoveRange(timestamps);
            }
            else if (materialType == typeof(QuestionnaireMaterial))
            {
                var entries = await context.QuestionnaireEntries.Where(e => e.QuestionnaireMaterialId == materialId).ToListAsync();
                context.QuestionnaireEntries.RemoveRange(entries);
            }
            else if (materialType == typeof(QuizMaterial))
            {
                var questions = await context.QuizQuestions
                    .Include(q => q.Answers)
                    .Where(q => q.QuizMaterialId == materialId)
                    .ToListAsync();
                foreach (var question in questions)
                {
                    context.QuizAnswers.RemoveRange(question.Answers);
                }
                context.QuizQuestions.RemoveRange(questions);
            }
            else if (materialType == typeof(ImageMaterial))
            {
                var annotations = await context.ImageAnnotations.Where(a => a.ImageMaterialId == materialId).ToListAsync();
                context.ImageAnnotations.RemoveRange(annotations);
            }

            await context.SaveChangesAsync();
        }

        #endregion
    }
}
