using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for simple material types without subcomponents:
    /// PDF, Unity, Chatbot, MQTT_Template, Default
    /// </summary>
    public interface ISimpleMaterialService
    {
        // PDF Material
        Task<IEnumerable<PDFMaterial>> GetAllPDFAsync();
        Task<PDFMaterial?> GetPDFByIdAsync(int id);
        Task<PDFMaterial> CreatePDFAsync(PDFMaterial pdf);
        Task<PDFMaterial> UpdatePDFAsync(PDFMaterial pdf);
        Task<bool> DeletePDFAsync(int id);

        // Unity Material
        Task<IEnumerable<UnityMaterial>> GetAllUnityAsync();
        Task<UnityMaterial?> GetUnityByIdAsync(int id);
        Task<UnityMaterial> CreateUnityAsync(UnityMaterial unity);
        Task<UnityMaterial> UpdateUnityAsync(UnityMaterial unity);
        Task<UnityMaterial> UpdateUnityConfigAsync(int unityId, string? version = null, string? buildTarget = null, string? sceneName = null, int? assetId = null, string? unityJson = null);
        Task<bool> DeleteUnityAsync(int id);

        // Chatbot Material
        Task<IEnumerable<ChatbotMaterial>> GetAllChatbotAsync();
        Task<ChatbotMaterial?> GetChatbotByIdAsync(int id);
        Task<ChatbotMaterial> CreateChatbotAsync(ChatbotMaterial chatbot);
        Task<ChatbotMaterial> UpdateChatbotAsync(ChatbotMaterial chatbot);
        Task<ChatbotMaterial> UpdateChatbotConfigAsync(int chatbotId, string config, string? model = null, string? prompt = null);
        Task<bool> DeleteChatbotAsync(int id);

        // MQTT Template Material
        Task<IEnumerable<MQTT_TemplateMaterial>> GetAllMQTTAsync();
        Task<MQTT_TemplateMaterial?> GetMQTTByIdAsync(int id);
        Task<MQTT_TemplateMaterial> CreateMQTTAsync(MQTT_TemplateMaterial mqtt);
        Task<MQTT_TemplateMaterial> UpdateMQTTAsync(MQTT_TemplateMaterial mqtt);
        Task<MQTT_TemplateMaterial> UpdateMQTTTemplateAsync(int templateId, string messageType, string messageText);
        Task<bool> DeleteMQTTAsync(int id);

        // Default Material
        Task<IEnumerable<DefaultMaterial>> GetAllDefaultAsync();
        Task<DefaultMaterial?> GetDefaultByIdAsync(int id);
        Task<DefaultMaterial> CreateDefaultAsync(DefaultMaterial defaultMat);
        Task<DefaultMaterial> UpdateDefaultAsync(DefaultMaterial defaultMat);
        Task<bool> DeleteDefaultAsync(int id);

        // Asset Operations (for asset-based types)
        Task<bool> AssignAssetToPDFAsync(int pdfId, int assetId);
        Task<bool> AssignAssetToUnityAsync(int unityId, int assetId);
        Task<bool> AssignAssetToDefaultAsync(int defaultId, int assetId);
        Task<bool> RemoveAssetFromPDFAsync(int pdfId);
        Task<bool> RemoveAssetFromUnityAsync(int unityId);
        Task<bool> RemoveAssetFromDefaultAsync(int defaultId);
    }
}
