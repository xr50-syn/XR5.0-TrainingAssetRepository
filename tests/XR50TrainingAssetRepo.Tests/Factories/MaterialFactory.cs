namespace XR50TrainingAssetRepo.Tests.Factories;

/// <summary>
/// Factory for creating test materials with sensible defaults.
/// </summary>
public static class MaterialFactory
{
    /// <summary>
    /// Creates a checklist material with entries, optionally with related materials.
    /// </summary>
    public static object CreateChecklistRequest(
        string? name = null,
        List<object>? entries = null)
    {
        return new
        {
            name = name ?? $"Test Checklist {Guid.NewGuid():N}",
            description = "Test checklist material",
            type = "checklist",
            entries = entries ?? new List<object>
            {
                new { text = "Entry 1", description = "First entry" },
                new { text = "Entry 2", description = "Second entry" },
                new { text = "Entry 3", description = "Third entry" }
            }
        };
    }

    /// <summary>
    /// Creates a checklist entry with related materials.
    /// </summary>
    public static object CreateChecklistEntryWithRelated(string text, string description, List<int> relatedMaterialIds)
    {
        return new
        {
            text,
            description,
            related = relatedMaterialIds.Select(id => new { id }).ToList()
        };
    }

    /// <summary>
    /// Creates a workflow material with steps, optionally with related materials.
    /// </summary>
    public static object CreateWorkflowRequest(
        string? name = null,
        List<object>? steps = null)
    {
        return new
        {
            name = name ?? $"Test Workflow {Guid.NewGuid():N}",
            description = "Test workflow material",
            type = "workflow",
            steps = steps ?? new List<object>
            {
                new { title = "Step 1", content = "First step content" },
                new { title = "Step 2", content = "Second step content" },
                new { title = "Step 3", content = "Third step content" }
            }
        };
    }

    /// <summary>
    /// Creates a workflow step with related materials.
    /// </summary>
    public static object CreateWorkflowStepWithRelated(string title, string content, List<int> relatedMaterialIds)
    {
        return new
        {
            title,
            content,
            related = relatedMaterialIds.Select(id => new { id }).ToList()
        };
    }

    /// <summary>
    /// Creates a video material with timestamps, optionally with related materials.
    /// </summary>
    public static object CreateVideoRequest(
        string? name = null,
        List<object>? timestamps = null)
    {
        return new
        {
            name = name ?? $"Test Video {Guid.NewGuid():N}",
            description = "Test video material",
            type = "video",
            videoPath = "/videos/test.mp4",
            videoDuration = 120,
            timestamps = timestamps ?? new List<object>
            {
                new { title = "Chapter 1", startTime = "0", description = "Introduction" },
                new { title = "Chapter 2", startTime = "60", description = "Main content" },
                new { title = "Chapter 3", startTime = "90", description = "Conclusion" }
            }
        };
    }

    /// <summary>
    /// Creates a video timestamp with related materials.
    /// </summary>
    public static object CreateTimestampWithRelated(string title, string startTime, string description, List<int> relatedMaterialIds)
    {
        return new
        {
            title,
            startTime,
            description,
            related = relatedMaterialIds.Select(id => new { id }).ToList()
        };
    }

    /// <summary>
    /// Creates a quiz material with questions and answers, optionally with related materials.
    /// </summary>
    public static object CreateQuizRequest(
        string? name = null,
        List<object>? questions = null,
        bool evaluationMode = false,
        int? minScore = null)
    {
        return new
        {
            name = name ?? $"Test Quiz {Guid.NewGuid():N}",
            description = "Test quiz material",
            type = "quiz",
            evaluationMode,
            minScore,
            config = new
            {
                questions = questions ?? new List<object>
                {
                    new
                    {
                        questionNumber = 1,
                        questionType = "multiple-choice",
                        text = "What is 2+2?",
                        answers = new List<object>
                        {
                            new { text = "3", correctAnswer = false },
                            new { text = "4", correctAnswer = true },
                            new { text = "5", correctAnswer = false }
                        }
                    },
                    new
                    {
                        questionNumber = 2,
                        questionType = "multiple-choice",
                        text = "What is the capital of France?",
                        answers = new List<object>
                        {
                            new { text = "London", correctAnswer = false },
                            new { text = "Paris", correctAnswer = true },
                            new { text = "Berlin", correctAnswer = false }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Creates a quiz question with related materials.
    /// </summary>
    public static object CreateQuestionWithRelated(
        int questionNumber,
        string text,
        List<object> answers,
        List<int> relatedMaterialIds)
    {
        return new
        {
            questionNumber,
            questionType = "multiple-choice",
            text,
            answers,
            related = relatedMaterialIds.Select(id => new { id }).ToList()
        };
    }

    /// <summary>
    /// Creates a quiz answer with related materials.
    /// </summary>
    public static object CreateAnswerWithRelated(string text, bool correctAnswer, List<int> relatedMaterialIds)
    {
        return new
        {
            text,
            correctAnswer,
            related = relatedMaterialIds.Select(id => new { id }).ToList()
        };
    }

    /// <summary>
    /// Creates a simple image material (for use as related material).
    /// </summary>
    public static object CreateImageRequest(string? name = null)
    {
        return new
        {
            name = name ?? $"Test Image {Guid.NewGuid():N}",
            description = "Test image material",
            type = "image",
            imagePath = "/images/test.png",
            imageWidth = 800,
            imageHeight = 600
        };
    }

    /// <summary>
    /// Creates a simple PDF material (for use as related material).
    /// </summary>
    public static object CreatePdfRequest(string? name = null)
    {
        return new
        {
            name = name ?? $"Test PDF {Guid.NewGuid():N}",
            description = "Test PDF material",
            type = "pdf",
            pdfPath = "/documents/test.pdf",
            pdfPageCount = 10
        };
    }

    /// <summary>
    /// Creates an AI Assistant material with optional asset IDs.
    /// </summary>
    public static object CreateAIAssistantRequest(
        string? name = null,
        List<int>? assetIds = null)
    {
        return new
        {
            name = name ?? $"Test AI Assistant {Guid.NewGuid():N}",
            description = "Test AI assistant material for document Q&A",
            type = "ai_assistant",
            assetIds = assetIds ?? new List<int>()
        };
    }

    /// <summary>
    /// Creates an AI Assistant material with specific asset IDs for testing session behavior.
    /// </summary>
    public static object CreateAIAssistantWithAssetsRequest(
        string name,
        List<int> assetIds)
    {
        return new
        {
            name,
            description = "AI assistant with pre-configured assets",
            type = "ai_assistant",
            assetIds
        };
    }

    /// <summary>
    /// Creates a chatbot material (for comparison with AI Assistant).
    /// </summary>
    public static object CreateChatbotRequest(
        string? name = null,
        string? endpoint = null)
    {
        return new
        {
            name = name ?? $"Test Chatbot {Guid.NewGuid():N}",
            description = "Test chatbot material",
            type = "chatbot",
            chatbotConfig = endpoint ?? "https://api.example.com/chat",
            chatbotModel = "default",
            chatbotPrompt = "You are a helpful assistant."
        };
    }
}
