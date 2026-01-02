using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for simple material types without subcomponents:
    /// PDF, Unity, Chatbot, MQTT_Template, Default
    /// </summary>
    public class SimpleMaterialService : ISimpleMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<SimpleMaterialService> _logger;

        public SimpleMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<SimpleMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region PDF Material

        public async Task<IEnumerable<PDFMaterial>> GetAllPDFAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<PDFMaterial>().ToListAsync();
        }

        public async Task<PDFMaterial?> GetPDFByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<PDFMaterial>().FirstOrDefaultAsync(p => p.id == id);
        }

        public async Task<PDFMaterial> CreatePDFAsync(PDFMaterial pdf)
        {
            using var context = _dbContextFactory.CreateDbContext();
            pdf.Created_at = DateTime.UtcNow;
            pdf.Updated_at = DateTime.UtcNow;
            pdf.Type = MaterialType.PDF;
            context.Materials.Add(pdf);
            await context.SaveChangesAsync();
            _logger.LogInformation("Created PDF material: {Name} with ID: {Id}", pdf.Name, pdf.id);
            return pdf;
        }

        public async Task<PDFMaterial> UpdatePDFAsync(PDFMaterial pdf)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var existing = await context.Materials.OfType<PDFMaterial>().FirstOrDefaultAsync(p => p.id == pdf.id);
            if (existing == null)
                throw new KeyNotFoundException($"PDF material {pdf.id} not found");

            existing.Name = pdf.Name;
            existing.Description = pdf.Description;
            existing.Updated_at = DateTime.UtcNow;
            if (pdf.AssetId.HasValue) existing.AssetId = pdf.AssetId;

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated PDF material: {Id} ({Name})", pdf.id, pdf.Name);
            return existing;
        }

        public async Task<bool> DeletePDFAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var pdf = await context.Materials.OfType<PDFMaterial>().FirstOrDefaultAsync(p => p.id == id);
            if (pdf == null) return false;

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id || (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);
            context.Materials.Remove(pdf);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted PDF material: {Id}", id);
            return true;
        }

        #endregion

        #region Unity Material

        public async Task<IEnumerable<UnityMaterial>> GetAllUnityAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<UnityMaterial>().ToListAsync();
        }

        public async Task<UnityMaterial?> GetUnityByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<UnityMaterial>().FirstOrDefaultAsync(u => u.id == id);
        }

        public async Task<UnityMaterial> CreateUnityAsync(UnityMaterial unity)
        {
            using var context = _dbContextFactory.CreateDbContext();
            unity.Created_at = DateTime.UtcNow;
            unity.Updated_at = DateTime.UtcNow;
            unity.Type = MaterialType.Unity;
            context.Materials.Add(unity);
            await context.SaveChangesAsync();
            _logger.LogInformation("Created Unity material: {Name} with ID: {Id}", unity.Name, unity.id);
            return unity;
        }

        public async Task<UnityMaterial> UpdateUnityAsync(UnityMaterial unity)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var existing = await context.Materials.OfType<UnityMaterial>().FirstOrDefaultAsync(u => u.id == unity.id);
            if (existing == null)
                throw new KeyNotFoundException($"Unity material {unity.id} not found");

            existing.Name = unity.Name;
            existing.Description = unity.Description;
            existing.UnityVersion = unity.UnityVersion;
            existing.UnityBuildTarget = unity.UnityBuildTarget;
            existing.UnitySceneName = unity.UnitySceneName;
            existing.UnityJson = unity.UnityJson;
            existing.Updated_at = DateTime.UtcNow;
            if (unity.AssetId.HasValue) existing.AssetId = unity.AssetId;

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated Unity material: {Id} ({Name})", unity.id, unity.Name);
            return existing;
        }

        public async Task<UnityMaterial> UpdateUnityConfigAsync(int unityId, string? version = null, string? buildTarget = null, string? sceneName = null, int? assetId = null, string? unityJson = null)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var unity = await context.Materials.OfType<UnityMaterial>().FirstOrDefaultAsync(u => u.id == unityId);
            if (unity == null)
                throw new KeyNotFoundException($"Unity material {unityId} not found");

            if (version != null) unity.UnityVersion = version;
            if (buildTarget != null) unity.UnityBuildTarget = buildTarget;
            if (sceneName != null) unity.UnitySceneName = sceneName;
            if (assetId.HasValue) unity.AssetId = assetId;
            if (unityJson != null) unity.UnityJson = unityJson;
            unity.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated Unity config for material {Id}", unityId);
            return unity;
        }

        public async Task<bool> DeleteUnityAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var unity = await context.Materials.OfType<UnityMaterial>().FirstOrDefaultAsync(u => u.id == id);
            if (unity == null) return false;

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id || (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);
            context.Materials.Remove(unity);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted Unity material: {Id}", id);
            return true;
        }

        #endregion

        #region Chatbot Material

        public async Task<IEnumerable<ChatbotMaterial>> GetAllChatbotAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<ChatbotMaterial>().ToListAsync();
        }

        public async Task<ChatbotMaterial?> GetChatbotByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<ChatbotMaterial>().FirstOrDefaultAsync(c => c.id == id);
        }

        public async Task<ChatbotMaterial> CreateChatbotAsync(ChatbotMaterial chatbot)
        {
            using var context = _dbContextFactory.CreateDbContext();
            chatbot.Created_at = DateTime.UtcNow;
            chatbot.Updated_at = DateTime.UtcNow;
            chatbot.Type = MaterialType.Chatbot;
            context.Materials.Add(chatbot);
            await context.SaveChangesAsync();
            _logger.LogInformation("Created Chatbot material: {Name} with ID: {Id}", chatbot.Name, chatbot.id);
            return chatbot;
        }

        public async Task<ChatbotMaterial> UpdateChatbotAsync(ChatbotMaterial chatbot)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var existing = await context.Materials.OfType<ChatbotMaterial>().FirstOrDefaultAsync(c => c.id == chatbot.id);
            if (existing == null)
                throw new KeyNotFoundException($"Chatbot material {chatbot.id} not found");

            existing.Name = chatbot.Name;
            existing.Description = chatbot.Description;
            existing.ChatbotConfig = chatbot.ChatbotConfig;
            existing.ChatbotModel = chatbot.ChatbotModel;
            existing.ChatbotPrompt = chatbot.ChatbotPrompt;
            existing.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated Chatbot material: {Id} ({Name})", chatbot.id, chatbot.Name);
            return existing;
        }

        public async Task<ChatbotMaterial> UpdateChatbotConfigAsync(int chatbotId, string config, string? model = null, string? prompt = null)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var chatbot = await context.Materials.OfType<ChatbotMaterial>().FirstOrDefaultAsync(c => c.id == chatbotId);
            if (chatbot == null)
                throw new KeyNotFoundException($"Chatbot material {chatbotId} not found");

            chatbot.ChatbotConfig = config;
            if (model != null) chatbot.ChatbotModel = model;
            if (prompt != null) chatbot.ChatbotPrompt = prompt;
            chatbot.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated Chatbot config for material {Id}", chatbotId);
            return chatbot;
        }

        public async Task<bool> DeleteChatbotAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var chatbot = await context.Materials.OfType<ChatbotMaterial>().FirstOrDefaultAsync(c => c.id == id);
            if (chatbot == null) return false;

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id || (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);
            context.Materials.Remove(chatbot);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted Chatbot material: {Id}", id);
            return true;
        }

        #endregion

        #region MQTT Template Material

        public async Task<IEnumerable<MQTT_TemplateMaterial>> GetAllMQTTAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<MQTT_TemplateMaterial>().ToListAsync();
        }

        public async Task<MQTT_TemplateMaterial?> GetMQTTByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<MQTT_TemplateMaterial>().FirstOrDefaultAsync(m => m.id == id);
        }

        public async Task<MQTT_TemplateMaterial> CreateMQTTAsync(MQTT_TemplateMaterial mqtt)
        {
            using var context = _dbContextFactory.CreateDbContext();
            mqtt.Created_at = DateTime.UtcNow;
            mqtt.Updated_at = DateTime.UtcNow;
            mqtt.Type = MaterialType.MQTT_Template;
            context.Materials.Add(mqtt);
            await context.SaveChangesAsync();
            _logger.LogInformation("Created MQTT Template material: {Name} with ID: {Id}", mqtt.Name, mqtt.id);
            return mqtt;
        }

        public async Task<MQTT_TemplateMaterial> UpdateMQTTAsync(MQTT_TemplateMaterial mqtt)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var existing = await context.Materials.OfType<MQTT_TemplateMaterial>().FirstOrDefaultAsync(m => m.id == mqtt.id);
            if (existing == null)
                throw new KeyNotFoundException($"MQTT Template material {mqtt.id} not found");

            existing.Name = mqtt.Name;
            existing.Description = mqtt.Description;
            existing.message_type = mqtt.message_type;
            existing.message_text = mqtt.message_text;
            existing.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated MQTT Template material: {Id} ({Name})", mqtt.id, mqtt.Name);
            return existing;
        }

        public async Task<MQTT_TemplateMaterial> UpdateMQTTTemplateAsync(int templateId, string messageType, string messageText)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var mqtt = await context.Materials.OfType<MQTT_TemplateMaterial>().FirstOrDefaultAsync(m => m.id == templateId);
            if (mqtt == null)
                throw new KeyNotFoundException($"MQTT Template material {templateId} not found");

            mqtt.message_type = messageType;
            mqtt.message_text = messageText;
            mqtt.Updated_at = DateTime.UtcNow;

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated MQTT Template for material {Id}", templateId);
            return mqtt;
        }

        public async Task<bool> DeleteMQTTAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var mqtt = await context.Materials.OfType<MQTT_TemplateMaterial>().FirstOrDefaultAsync(m => m.id == id);
            if (mqtt == null) return false;

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id || (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);
            context.Materials.Remove(mqtt);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted MQTT Template material: {Id}", id);
            return true;
        }

        #endregion

        #region Default Material

        public async Task<IEnumerable<DefaultMaterial>> GetAllDefaultAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<DefaultMaterial>().ToListAsync();
        }

        public async Task<DefaultMaterial?> GetDefaultByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials.OfType<DefaultMaterial>().FirstOrDefaultAsync(d => d.id == id);
        }

        public async Task<DefaultMaterial> CreateDefaultAsync(DefaultMaterial defaultMat)
        {
            using var context = _dbContextFactory.CreateDbContext();
            defaultMat.Created_at = DateTime.UtcNow;
            defaultMat.Updated_at = DateTime.UtcNow;
            defaultMat.Type = MaterialType.Default;
            context.Materials.Add(defaultMat);
            await context.SaveChangesAsync();
            _logger.LogInformation("Created Default material: {Name} with ID: {Id}", defaultMat.Name, defaultMat.id);
            return defaultMat;
        }

        public async Task<DefaultMaterial> UpdateDefaultAsync(DefaultMaterial defaultMat)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var existing = await context.Materials.OfType<DefaultMaterial>().FirstOrDefaultAsync(d => d.id == defaultMat.id);
            if (existing == null)
                throw new KeyNotFoundException($"Default material {defaultMat.id} not found");

            existing.Name = defaultMat.Name;
            existing.Description = defaultMat.Description;
            existing.Updated_at = DateTime.UtcNow;
            if (defaultMat.AssetId.HasValue) existing.AssetId = defaultMat.AssetId;

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated Default material: {Id} ({Name})", defaultMat.id, defaultMat.Name);
            return existing;
        }

        public async Task<bool> DeleteDefaultAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var defaultMat = await context.Materials.OfType<DefaultMaterial>().FirstOrDefaultAsync(d => d.id == id);
            if (defaultMat == null) return false;

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id || (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);
            context.Materials.Remove(defaultMat);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted Default material: {Id}", id);
            return true;
        }

        #endregion

        #region Asset Operations

        public async Task<bool> AssignAssetToPDFAsync(int pdfId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var pdf = await context.Materials.OfType<PDFMaterial>().FirstOrDefaultAsync(p => p.id == pdfId);
            if (pdf == null) return false;
            pdf.AssetId = assetId;
            pdf.Updated_at = DateTime.UtcNow;
            await context.SaveChangesAsync();
            _logger.LogInformation("Assigned asset {AssetId} to PDF material {PdfId}", assetId, pdfId);
            return true;
        }

        public async Task<bool> AssignAssetToUnityAsync(int unityId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var unity = await context.Materials.OfType<UnityMaterial>().FirstOrDefaultAsync(u => u.id == unityId);
            if (unity == null) return false;
            unity.AssetId = assetId;
            unity.Updated_at = DateTime.UtcNow;
            await context.SaveChangesAsync();
            _logger.LogInformation("Assigned asset {AssetId} to Unity material {UnityId}", assetId, unityId);
            return true;
        }

        public async Task<bool> AssignAssetToDefaultAsync(int defaultId, int assetId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var defaultMat = await context.Materials.OfType<DefaultMaterial>().FirstOrDefaultAsync(d => d.id == defaultId);
            if (defaultMat == null) return false;
            defaultMat.AssetId = assetId;
            defaultMat.Updated_at = DateTime.UtcNow;
            await context.SaveChangesAsync();
            _logger.LogInformation("Assigned asset {AssetId} to Default material {DefaultId}", assetId, defaultId);
            return true;
        }

        public async Task<bool> RemoveAssetFromPDFAsync(int pdfId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var pdf = await context.Materials.OfType<PDFMaterial>().FirstOrDefaultAsync(p => p.id == pdfId);
            if (pdf == null) return false;
            pdf.AssetId = null;
            pdf.Updated_at = DateTime.UtcNow;
            await context.SaveChangesAsync();
            _logger.LogInformation("Removed asset from PDF material {PdfId}", pdfId);
            return true;
        }

        public async Task<bool> RemoveAssetFromUnityAsync(int unityId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var unity = await context.Materials.OfType<UnityMaterial>().FirstOrDefaultAsync(u => u.id == unityId);
            if (unity == null) return false;
            unity.AssetId = null;
            unity.Updated_at = DateTime.UtcNow;
            await context.SaveChangesAsync();
            _logger.LogInformation("Removed asset from Unity material {UnityId}", unityId);
            return true;
        }

        public async Task<bool> RemoveAssetFromDefaultAsync(int defaultId)
        {
            using var context = _dbContextFactory.CreateDbContext();
            var defaultMat = await context.Materials.OfType<DefaultMaterial>().FirstOrDefaultAsync(d => d.id == defaultId);
            if (defaultMat == null) return false;
            defaultMat.AssetId = null;
            defaultMat.Updated_at = DateTime.UtcNow;
            await context.SaveChangesAsync();
            _logger.LogInformation("Removed asset from Default material {DefaultId}", defaultId);
            return true;
        }

        #endregion
    }
}
