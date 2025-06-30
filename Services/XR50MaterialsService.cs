using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services
{
    public interface IMaterialService
    {
        // Base Material Operations
        Task<IEnumerable<Material>> GetAllMaterialsAsync();
        Task<Material?> GetMaterialAsync(int id);
        Task<Material> CreateMaterialAsync(Material material);
        Task<Material> UpdateMaterialAsync(Material material);
        Task<bool> DeleteMaterialAsync(int id);
        Task<bool> MaterialExistsAsync(int id);
        
        // Material Type-Specific Operations
        Task<IEnumerable<Material>> GetMaterialsByTypeAsync(System.Type materialType);
        Task<IEnumerable<VideoMaterial>> GetAllVideoMaterialsAsync();
        Task<IEnumerable<ChecklistMaterial>> GetAllChecklistMaterialsAsync();
        Task<IEnumerable<WorkflowMaterial>> GetAllWorkflowMaterialsAsync();
        Task<IEnumerable<ImageMaterial>> GetAllImageMaterialsAsync();
        
        // Additional Material Type Operations
        Task<IEnumerable<PDFMaterial>> GetAllPDFMaterialsAsync();
        Task<PDFMaterial?> GetPDFMaterialAsync(int id);
        Task<IEnumerable<ChatbotMaterial>> GetAllChatbotMaterialsAsync();
        Task<ChatbotMaterial?> GetChatbotMaterialAsync(int id);
        Task<IEnumerable<QuestionnaireMaterial>> GetAllQuestionnaireMaterialsAsync();
        Task<IEnumerable<MQTT_TemplateMaterial>> GetAllMQTTTemplateMaterialsAsync();
        Task<MQTT_TemplateMaterial?> GetMQTTTemplateMaterialAsync(int id);
        Task<IEnumerable<UnityDemoMaterial>> GetAllUnityDemoMaterialsAsync();
        Task<UnityDemoMaterial?> GetUnityDemoMaterialAsync(int id);
        
        // Video Material Specific
        Task<VideoMaterial?> GetVideoMaterialWithTimestampsAsync(int id);
        Task<VideoMaterial> AddTimestampToVideoAsync(int videoId, VideoTimestamp timestamp);
        Task<bool> RemoveTimestampFromVideoAsync(int videoId, int timestampId);
        
        // Checklist Material Specific
        Task<ChecklistMaterial?> GetChecklistMaterialWithEntriesAsync(int id);
        Task<ChecklistMaterial> AddEntryToChecklistAsync(int checklistId, ChecklistEntry entry);
        Task<bool> RemoveEntryFromChecklistAsync(int checklistId, int entryId);
        
        // Workflow Material Specific
        Task<WorkflowMaterial?> GetWorkflowMaterialWithStepsAsync(int id);
        Task<WorkflowMaterial> AddStepToWorkflowAsync(int workflowId, WorkflowStep step);
        Task<bool> RemoveStepFromWorkflowAsync(int workflowId, int stepId);
        
        // Questionnaire Material Specific
        Task<QuestionnaireMaterial?> GetQuestionnaireMaterialWithEntriesAsync(int id);
        Task<QuestionnaireMaterial> AddEntryToQuestionnaireAsync(int questionnaireId, QuestionnaireEntry entry);
        Task<bool> RemoveEntryFromQuestionnaireAsync(int questionnaireId, int entryId);
        
        // Chatbot Material Specific
        Task<ChatbotMaterial> UpdateChatbotConfigAsync(int chatbotId, string config, string? model = null, string? prompt = null);
        
        // MQTT Template Material Specific
        Task<MQTT_TemplateMaterial> UpdateMQTTTemplateAsync(int templateId, string messageType, string messageText);
        
        // Unity Demo Material Specific
        Task<UnityDemoMaterial> UpdateUnityDemoConfigAsync(int unityId, string? version = null, string? buildTarget = null, string? sceneName = null, string? assetId = null);
        
        // Complex Material Creation (One-shot creation with child entities)
        Task<WorkflowMaterial> CreateWorkflowWithStepsAsync(WorkflowMaterial workflow, IEnumerable<WorkflowStep>? initialSteps = null);
        Task<VideoMaterial> CreateVideoWithTimestampsAsync(VideoMaterial video, IEnumerable<VideoTimestamp>? initialTimestamps = null);
        Task<ChecklistMaterial> CreateChecklistWithEntriesAsync(ChecklistMaterial checklist, IEnumerable<ChecklistEntry>? initialEntries = null);
        
        // Direct Asset Relationships (Many-to-One, only for certain material types)
        Task<IEnumerable<Material>> GetMaterialsByAssetIdAsync(string assetId);
        Task<bool> AssignAssetToMaterialAsync(int materialId, string assetId);
        Task<bool> RemoveAssetFromMaterialAsync(int materialId);
        Task<string?> GetMaterialAssetIdAsync(int materialId);
        
        // Polymorphic Relationships via MaterialRelationships Table
        Task<MaterialRelationship> CreateRelationshipAsync(MaterialRelationship relationship);
        Task<bool> DeleteRelationshipAsync(string relationshipId);
        Task<IEnumerable<MaterialRelationship>> GetMaterialRelationshipsAsync(int materialId);
        Task<IEnumerable<MaterialRelationship>> GetRelationshipsByTypeAsync(int materialId, string relatedEntityType);
        
        // Learning Path Associations
        Task<string> AssignMaterialToLearningPathAsync(int materialId, int learningPathId, string relationshipType = "contains", int? displayOrder = null);
        Task<bool> RemoveMaterialFromLearningPathAsync(int materialId, int learningPathId);
        Task<IEnumerable<Material>> GetMaterialsByLearningPathAsync(int learningPathId, bool includeOrder = true);
        Task<bool> ReorderMaterialsInLearningPathAsync(int learningPathId, Dictionary<int, int> materialOrderMap);
        
        // Training Program Direct Associations  
        Task<string> AssignMaterialToTrainingProgramAsync(int materialId, int trainingProgramId, string relationshipType = "assigned");
        Task<bool> RemoveMaterialFromTrainingProgramAsync(int materialId, int trainingProgramId);
        Task<IEnumerable<Material>> GetMaterialsByTrainingProgramAsync(int trainingProgramId);
        
        // Material Dependencies
        Task<string> CreateMaterialDependencyAsync(int materialId, int prerequisiteMaterialId, string relationshipType = "prerequisite");
        Task<bool> RemoveMaterialDependencyAsync(int materialId, int prerequisiteMaterialId);
        Task<IEnumerable<Material>> GetMaterialPrerequisitesAsync(int materialId);
        Task<IEnumerable<Material>> GetMaterialDependentsAsync(int materialId);
    }
    public class MaterialService : IMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<MaterialService> _logger;

        public MaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<MaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Base Material Operations

        public async Task<IEnumerable<Material>> GetAllMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.ToListAsync();
        }

        public async Task<Material?> GetMaterialAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.FindAsync(id);
        }

        public async Task<Material> CreateMaterialAsync(Material material)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Set timestamps
            material.Created_at = DateTime.UtcNow;
            material.Updated_at = DateTime.UtcNow;

            // Ensure the Type property matches the actual class type
            SetMaterialTypeFromClass(material);

            context.Materials.Add(material);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created material: {Name} (Type: {Type}, Discriminator: {Discriminator}) with ID: {Id}",
                material.Name, material.Type, material.GetType().Name, material.Id);

            return material;
        }

        private void SetMaterialTypeFromClass(Material material)
        {
            material.Type = material switch
            {
                VideoMaterial => MaterialType.Video,
                ImageMaterial => MaterialType.Image,
                ChecklistMaterial => MaterialType.Checklist,
                WorkflowMaterial => MaterialType.Workflow,
                PDFMaterial => MaterialType.PDF,
                UnityDemoMaterial => MaterialType.UnityDemo,
                ChatbotMaterial => MaterialType.Chatbot,
                QuestionnaireMaterial => MaterialType.Questionnaire,
                MQTT_TemplateMaterial => MaterialType.MQTT_Template,
                DefaultMaterial => MaterialType.Default,
                _ => MaterialType.Default
            };
        }

        public async Task<Material> UpdateMaterialAsync(Material material)
        {
            using var context = _dbContextFactory.CreateDbContext();

            material.Updated_at = DateTime.UtcNow;
            context.Entry(material).State = EntityState.Modified;
            await context.SaveChangesAsync();

            _logger.LogInformation("Updated material: {Id} (Type: {Type})",
                material.Id, material.GetType().Name);

            return material;
        }

        public async Task<bool> DeleteMaterialAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials.FindAsync(id);
            if (material == null)
            {
                return false;
            }

            // Also delete all relationships involving this material
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

        public async Task<bool> MaterialExistsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.AnyAsync(e => e.Id == id);
        }

        #endregion

        #region Material Type-Specific Operations

        public async Task<IEnumerable<Material>> GetMaterialsByTypeAsync(System.Type materialType)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var discriminator = materialType.Name;
            return await context.Materials
                .Where(m => EF.Property<string>(m, "Discriminator") == discriminator)
                .ToListAsync();
        }

        public async Task<IEnumerable<VideoMaterial>> GetAllVideoMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<VideoMaterial>()
                .ToListAsync();
        }

        public async Task<IEnumerable<ChecklistMaterial>> GetAllChecklistMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ChecklistMaterial>()
                .ToListAsync();
        }

        public async Task<IEnumerable<WorkflowMaterial>> GetAllWorkflowMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<WorkflowMaterial>()
                .ToListAsync();
        }

        public async Task<IEnumerable<ImageMaterial>> GetAllImageMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ImageMaterial>()
                .ToListAsync();
        }

        public async Task<IEnumerable<PDFMaterial>> GetAllPDFMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<PDFMaterial>()
                .ToListAsync();
        }

        public async Task<PDFMaterial?> GetPDFMaterialAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<PDFMaterial>()
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<IEnumerable<ChatbotMaterial>> GetAllChatbotMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ChatbotMaterial>()
                .ToListAsync();
        }

        public async Task<ChatbotMaterial?> GetChatbotMaterialAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ChatbotMaterial>()
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<IEnumerable<QuestionnaireMaterial>> GetAllQuestionnaireMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<QuestionnaireMaterial>()
                .ToListAsync();
        }

        public async Task<IEnumerable<MQTT_TemplateMaterial>> GetAllMQTTTemplateMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<MQTT_TemplateMaterial>()
                .ToListAsync();
        }

        public async Task<MQTT_TemplateMaterial?> GetMQTTTemplateMaterialAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<MQTT_TemplateMaterial>()
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<IEnumerable<UnityDemoMaterial>> GetAllUnityDemoMaterialsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<UnityDemoMaterial>()
                .ToListAsync();
        }

        public async Task<UnityDemoMaterial?> GetUnityDemoMaterialAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<UnityDemoMaterial>()
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        #endregion

        #region Video Material Specific

        public async Task<VideoMaterial?> GetVideoMaterialWithTimestampsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.Materials
                .OfType<VideoMaterial>()
                .Include(v => v.VideoTimestamps)
                .FirstOrDefaultAsync(v => v.Id == id);
        }

        public async Task<VideoMaterial> AddTimestampToVideoAsync(int videoId, VideoTimestamp timestamp)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var video = await context.Materials
                .OfType<VideoMaterial>()
                .FirstOrDefaultAsync(v => v.Id == videoId);

            if (video == null)
            {
                throw new ArgumentException($"Video material with ID {videoId} not found");
            }

            // Set the foreign key
            context.Entry(timestamp).Property("VideoMaterialId").CurrentValue = videoId;

            context.VideoTimestamps.Add(timestamp);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added timestamp '{Title}' to video material {VideoId}",
                timestamp.Title, videoId);

            return video;
        }

        public async Task<bool> RemoveTimestampFromVideoAsync(int videoId, int timestampId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var timestamp = await context.VideoTimestamps.FindAsync(timestampId);
            if (timestamp == null)
            {
                return false;
            }

            context.VideoTimestamps.Remove(timestamp);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed timestamp {TimestampId} from video material {VideoId}",
                timestampId, videoId);

            return true;
        }

        #endregion

        #region Checklist Material Specific

        public async Task<ChecklistMaterial?> GetChecklistMaterialWithEntriesAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.Materials
                .OfType<ChecklistMaterial>()
                .Include(c => c.ChecklistEntries)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<ChecklistMaterial> AddEntryToChecklistAsync(int checklistId, ChecklistEntry entry)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var checklist = await context.Materials
                .OfType<ChecklistMaterial>()
                .FirstOrDefaultAsync(c => c.Id == checklistId);

            if (checklist == null)
            {
                throw new ArgumentException($"Checklist material with ID {checklistId} not found");
            }

            // Set the foreign key
            context.Entry(entry).Property("ChecklistMaterialId").CurrentValue = checklistId;

            context.ChecklistEntries.Add(entry);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added entry '{Text}' to checklist material {ChecklistId}",
                entry.Text, checklistId);

            return checklist;
        }

        public async Task<bool> RemoveEntryFromChecklistAsync(int checklistId, int entryId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var entry = await context.ChecklistEntries.FindAsync(entryId);
            if (entry == null)
            {
                return false;
            }

            context.ChecklistEntries.Remove(entry);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed entry {EntryId} from checklist material {ChecklistId}",
                entryId, checklistId);

            return true;
        }

        #endregion

        #region Workflow Material Specific

        public async Task<WorkflowMaterial?> GetWorkflowMaterialWithStepsAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.Materials
                .OfType<WorkflowMaterial>()
                .Include(w => w.WorkflowSteps)
                .FirstOrDefaultAsync(w => w.Id == id);
        }

        public async Task<WorkflowMaterial> AddStepToWorkflowAsync(int workflowId, WorkflowStep step)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var workflow = await context.Materials
                .OfType<WorkflowMaterial>()
                .FirstOrDefaultAsync(w => w.Id == workflowId);

            if (workflow == null)
            {
                throw new ArgumentException($"Workflow material with ID {workflowId} not found");
            }

            // Set the foreign key
            context.Entry(step).Property("WorkflowMaterialId").CurrentValue = workflowId;

            context.WorkflowSteps.Add(step);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added step '{Title}' to workflow material {WorkflowId}",
                step.Title, workflowId);

            return workflow;
        }

        public async Task<bool> RemoveStepFromWorkflowAsync(int workflowId, int stepId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var step = await context.WorkflowSteps.FindAsync(stepId);
            if (step == null)
            {
                return false;
            }

            context.WorkflowSteps.Remove(step);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed step {StepId} from workflow material {WorkflowId}",
                stepId, workflowId);

            return true;
        }

        #endregion

        #region Questionnaire Material Specific

        public async Task<QuestionnaireMaterial?> GetQuestionnaireMaterialWithEntriesAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.Materials
                .OfType<QuestionnaireMaterial>()
                .Include(q => q.QuestionnaireEntries)
                .FirstOrDefaultAsync(q => q.Id == id);
        }

        public async Task<QuestionnaireMaterial> AddEntryToQuestionnaireAsync(int questionnaireId, QuestionnaireEntry entry)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var questionnaire = await context.Materials
                .OfType<QuestionnaireMaterial>()
                .FirstOrDefaultAsync(q => q.Id == questionnaireId);

            if (questionnaire == null)
            {
                throw new ArgumentException($"Questionnaire material with ID {questionnaireId} not found");
            }

            context.QuestionnaireEntries.Add(entry);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added entry '{Text}' to questionnaire material {QuestionnaireId}",
                entry.Text, questionnaireId);

            return questionnaire;
        }

        public async Task<bool> RemoveEntryFromQuestionnaireAsync(int questionnaireId, int entryId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var entry = await context.QuestionnaireEntries.FindAsync(entryId);
            if (entry == null)
            {
                return false;
            }

            context.QuestionnaireEntries.Remove(entry);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed entry {EntryId} from questionnaire material {QuestionnaireId}",
                entryId, questionnaireId);

            return true;
        }

        #endregion

        #region Chatbot Material Specific

        public async Task<ChatbotMaterial> UpdateChatbotConfigAsync(int chatbotId, string config, string? model = null, string? prompt = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var chatbot = await context.Materials
                .OfType<ChatbotMaterial>()
                .FirstOrDefaultAsync(c => c.Id == chatbotId);

            if (chatbot == null)
            {
                throw new ArgumentException($"Chatbot material with ID {chatbotId} not found");
            }

            chatbot.ChatbotConfig = config;
            if (model != null) chatbot.ChatbotModel = model;
            if (prompt != null) chatbot.ChatbotPrompt = prompt;
            chatbot.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();

            _logger.LogInformation("Updated chatbot configuration for material {ChatbotId}", chatbotId);

            return chatbot;
        }

        #endregion

        #region MQTT Template Material Specific

        public async Task<MQTT_TemplateMaterial> UpdateMQTTTemplateAsync(int templateId, string messageType, string messageText)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var template = await context.Materials
                .OfType<MQTT_TemplateMaterial>()
                .FirstOrDefaultAsync(m => m.Id == templateId);

            if (template == null)
            {
                throw new ArgumentException($"MQTT template material with ID {templateId} not found");
            }

            template.message_type = messageType;
            template.message_text = messageText;
            template.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();

            _logger.LogInformation("Updated MQTT template {TemplateId} with type {MessageType}",
                templateId, messageType);

            return template;
        }

        #endregion

        #region Unity Demo Material Specific

        public async Task<UnityDemoMaterial> UpdateUnityDemoConfigAsync(int unityId, string? version = null, string? buildTarget = null, string? sceneName = null, string? assetId = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var unity = await context.Materials
                .OfType<UnityDemoMaterial>()
                .FirstOrDefaultAsync(u => u.Id == unityId);

            if (unity == null)
            {
                throw new ArgumentException($"Unity demo material with ID {unityId} not found");
            }

            if (version != null) unity.UnityVersion = version;
            if (buildTarget != null) unity.UnityBuildTarget = buildTarget;
            if (sceneName != null) unity.UnitySceneName = sceneName;
            if (assetId != null) unity.AssetId = assetId;
            unity.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();

            _logger.LogInformation("Updated Unity demo material {UnityId}", unityId);

            return unity;
        }

        #endregion

        #region Complex Material Creation Methods

        public async Task<WorkflowMaterial> CreateWorkflowWithStepsAsync(WorkflowMaterial workflow, IEnumerable<WorkflowStep>? initialSteps = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Set timestamps and type
            workflow.Created_at = DateTime.UtcNow;
            workflow.Updated_at = DateTime.UtcNow;
            workflow.Type = MaterialType.Workflow;

            context.Materials.Add(workflow);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created workflow material: {Name} with ID: {Id}",
                workflow.Name, workflow.Id);

            // Add initial steps if provided
            if (initialSteps != null && initialSteps.Any())
            {
                foreach (var step in initialSteps)
                {
                    context.Entry(step).Property("WorkflowMaterialId").CurrentValue = workflow.Id;
                    context.WorkflowSteps.Add(step);
                }

                await context.SaveChangesAsync();

                _logger.LogInformation("Added {StepCount} initial steps to workflow {WorkflowId}",
                    initialSteps.Count(), workflow.Id);
            }

            return workflow;
        }

        public async Task<VideoMaterial> CreateVideoWithTimestampsAsync(VideoMaterial video, IEnumerable<VideoTimestamp>? initialTimestamps = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Set timestamps and type
            video.Created_at = DateTime.UtcNow;
            video.Updated_at = DateTime.UtcNow;
            video.Type = MaterialType.Video;

            context.Materials.Add(video);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created video material: {Name} with ID: {Id}",
                video.Name, video.Id);

            // Add initial timestamps if provided
            if (initialTimestamps != null && initialTimestamps.Any())
            {
                foreach (var timestamp in initialTimestamps)
                {
                    context.Entry(timestamp).Property("VideoMaterialId").CurrentValue = video.Id;
                    context.VideoTimestamps.Add(timestamp);
                }

                await context.SaveChangesAsync();

                _logger.LogInformation("Added {TimestampCount} initial timestamps to video {VideoId}",
                    initialTimestamps.Count(), video.Id);
            }

            return video;
        }

        public async Task<ChecklistMaterial> CreateChecklistWithEntriesAsync(ChecklistMaterial checklist, IEnumerable<ChecklistEntry>? initialEntries = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Set timestamps and type
            checklist.Created_at = DateTime.UtcNow;
            checklist.Updated_at = DateTime.UtcNow;
            checklist.Type = MaterialType.Checklist;

            context.Materials.Add(checklist);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created checklist material: {Name} with ID: {Id}",
                checklist.Name, checklist.Id);

            // Add initial entries if provided
            if (initialEntries != null && initialEntries.Any())
            {
                foreach (var entry in initialEntries)
                {
                    context.Entry(entry).Property("ChecklistMaterialId").CurrentValue = checklist.Id;
                    context.ChecklistEntries.Add(entry);
                }

                await context.SaveChangesAsync();

                _logger.LogInformation("Added {EntryCount} initial entries to checklist {ChecklistId}",
                    initialEntries.Count(), checklist.Id);
            }

            return checklist;
        }

        #endregion

        #region Direct Asset Relationships

        public async Task<IEnumerable<Material>> GetMaterialsByAssetIdAsync(string assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Query all materials that can have AssetId
            return await context.Materials
                .Where(m => (m is VideoMaterial && ((VideoMaterial)m).AssetId == assetId) ||
                           (m is ImageMaterial && ((ImageMaterial)m).AssetId == assetId) ||
                           (m is PDFMaterial && ((PDFMaterial)m).AssetId == assetId) ||
                           (m is UnityDemoMaterial && ((UnityDemoMaterial)m).AssetId == assetId) ||
                           (m is DefaultMaterial && ((DefaultMaterial)m).AssetId == assetId))
                .ToListAsync();
        }

        public async Task<bool> AssignAssetToMaterialAsync(int materialId, string assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials.FindAsync(materialId);
            if (material == null)
            {
                return false;
            }

            // Check if this material type supports assets and assign accordingly
            var success = material switch
            {
                VideoMaterial video => AssignAssetToVideoMaterial(video, assetId),
                ImageMaterial image => AssignAssetToImageMaterial(image, assetId),
                PDFMaterial pdf => AssignAssetToPDFMaterial(pdf, assetId),
                UnityDemoMaterial unity => AssignAssetToUnityMaterial(unity, assetId),
                DefaultMaterial defaultMat => AssignAssetToDefaultMaterial(defaultMat, assetId),
                _ => false
            };

            if (success)
            {
                material.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();

                _logger.LogInformation("Assigned asset {AssetId} to material {MaterialId} (Type: {Type})",
                    assetId, materialId, material.GetType().Name);
            }

            return success;
        }

        private bool AssignAssetToVideoMaterial(VideoMaterial video, string assetId)
        {
            video.AssetId = assetId;
            return true;
        }

        private bool AssignAssetToImageMaterial(ImageMaterial image, string assetId)
        {
            image.AssetId = assetId;
            return true;
        }

        private bool AssignAssetToPDFMaterial(PDFMaterial pdf, string assetId)
        {
            pdf.AssetId = assetId;
            return true;
        }

        private bool AssignAssetToUnityMaterial(UnityDemoMaterial unity, string assetId)
        {
            unity.AssetId = assetId;
            return true;
        }

        private bool AssignAssetToDefaultMaterial(DefaultMaterial defaultMat, string assetId)
        {
            defaultMat.AssetId = assetId;
            return true;
        }

        public async Task<bool> RemoveAssetFromMaterialAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials.FindAsync(materialId);
            if (material == null)
            {
                return false;
            }

            var success = material switch
            {
                VideoMaterial video => RemoveAssetFromVideoMaterial(video),
                ImageMaterial image => RemoveAssetFromImageMaterial(image),
                PDFMaterial pdf => RemoveAssetFromPDFMaterial(pdf),
                UnityDemoMaterial unity => RemoveAssetFromUnityMaterial(unity),
                DefaultMaterial defaultMat => RemoveAssetFromDefaultMaterial(defaultMat),
                _ => false
            };

            if (success)
            {
                material.Updated_at = DateTime.UtcNow;
                await context.SaveChangesAsync();

                _logger.LogInformation("Removed asset from material {MaterialId} (Type: {Type})",
                    materialId, material.GetType().Name);
            }

            return success;
        }

        private bool RemoveAssetFromVideoMaterial(VideoMaterial video)
        {
            video.AssetId = null;
            return true;
        }

        private bool RemoveAssetFromImageMaterial(ImageMaterial image)
        {
            image.AssetId = null;
            return true;
        }

        private bool RemoveAssetFromPDFMaterial(PDFMaterial pdf)
        {
            pdf.AssetId = null;
            return true;
        }

        private bool RemoveAssetFromUnityMaterial(UnityDemoMaterial unity)
        {
            unity.AssetId = null;
            return true;
        }

        private bool RemoveAssetFromDefaultMaterial(DefaultMaterial defaultMat)
        {
            defaultMat.AssetId = null;
            return true;
        }

        public async Task<string?> GetMaterialAssetIdAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials.FindAsync(materialId);
            if (material == null)
            {
                return null;
            }

            return material switch
            {
                VideoMaterial video => video.AssetId,
                ImageMaterial image => image.AssetId,
                PDFMaterial pdf => pdf.AssetId,
                UnityDemoMaterial unity => unity.AssetId,
                DefaultMaterial defaultMat => defaultMat.AssetId,
                _ => null
            };
        }

        #endregion

        #region Polymorphic Relationships via MaterialRelationships

        public async Task<MaterialRelationship> CreateRelationshipAsync(MaterialRelationship relationship)
        {
            using var context = _dbContextFactory.CreateDbContext();

            relationship.Id = Guid.NewGuid().ToString();

            context.MaterialRelationships.Add(relationship);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created relationship {RelationshipId}: Material {MaterialId} → {RelatedEntityType} {RelatedEntityId} ({RelationshipType})",
                relationship.Id, relationship.MaterialId, relationship.RelatedEntityType,
                relationship.RelatedEntityId, relationship.RelationshipType);

            return relationship;
        }

        public async Task<bool> DeleteRelationshipAsync(string relationshipId)
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

        #region Learning Path Associations

        public async Task<string> AssignMaterialToLearningPathAsync(int materialId, int learningPathId, string relationshipType = "contains", int? displayOrder = null)
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
                        join m in context.Materials on mr.MaterialId equals m.Id
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

        #region Training Program Direct Associations

        public async Task<string> AssignMaterialToTrainingProgramAsync(int materialId, int trainingProgramId, string relationshipType = "assigned")
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
                          join m in context.Materials on mr.MaterialId equals m.Id
                          where mr.RelatedEntityType == "TrainingProgram" &&
                                mr.RelatedEntityId == trainingProgramId.ToString()
                          select m).ToListAsync();
        }

        #endregion

        #region Material Dependencies

        public async Task<string> CreateMaterialDependencyAsync(int materialId, int prerequisiteMaterialId, string relationshipType = "prerequisite")
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

            return await (from mr in context.MaterialRelationships
                          join m in context.Materials on int.Parse(mr.RelatedEntityId) equals m.Id
                          where mr.MaterialId == materialId &&
                                mr.RelatedEntityType == "Material" &&
                                mr.RelationshipType == "prerequisite"
                          select m).ToListAsync();
        }

        public async Task<IEnumerable<Material>> GetMaterialDependentsAsync(int materialId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await (from mr in context.MaterialRelationships
                          join m in context.Materials on mr.MaterialId equals m.Id
                          where mr.RelatedEntityType == "Material" &&
                                mr.RelatedEntityId == materialId.ToString() &&
                                mr.RelationshipType == "prerequisite"
                          select m).ToListAsync();
        }

        #endregion
    }
}