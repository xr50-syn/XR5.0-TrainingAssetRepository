using XR50TrainingAssetRepo.Tests.Fixtures;
using XR50TrainingAssetRepo.Tests.Factories;

namespace XR50TrainingAssetRepo.Tests.Integration;

/// <summary>
/// Integration tests for the subcomponent related materials feature.
/// Tests that checklist entries, workflow steps, video timestamps,
/// quiz questions, and quiz answers can have related materials assigned.
/// </summary>
public class SubcomponentRelatedMaterialsTests : IClassFixture<WebApplicationFixture>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly WebApplicationFixture _factory;
    private readonly List<int> _createdMaterialIds = new();
    private const string TenantName = WebApplicationFixture.TestTenant;

    public SubcomponentRelatedMaterialsTests(WebApplicationFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Cleanup created materials in reverse order
        foreach (var id in _createdMaterialIds.AsEnumerable().Reverse())
        {
            try
            {
                await _client.DeleteAsync($"/api/{TenantName}/materials/{id}");
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Helper Methods

    private async Task<int> CreateMaterialAsync(object materialRequest)
    {
        var response = await _client.PostAsJsonAsync($"/api/{TenantName}/materials", materialRequest);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var idElement = content.GetProperty("id");
        // API returns ID as string due to custom JSON converter
        var id = idElement.ValueKind == JsonValueKind.String
            ? int.Parse(idElement.GetString()!)
            : idElement.GetInt32();
        _createdMaterialIds.Add(id);
        return id;
    }

    private async Task<JsonElement> GetMaterialDetailAsync(int materialId)
    {
        var response = await _client.GetAsync($"/api/{TenantName}/materials/{materialId}/detail");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<int> CreateRelatedMaterialAsync()
    {
        // Create a simple image material to be used as related material
        var imageRequest = MaterialFactory.CreateImageRequest();
        return await CreateMaterialAsync(imageRequest);
    }

    #endregion

    #region Checklist Entry Tests

    [Fact]
    public async Task POST_ChecklistWithRelatedMaterials_CreatesRelationships()
    {
        // Arrange - Create a material to be used as related
        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var checklistRequest = new
        {
            name = "Checklist with Related Materials",
            description = "Test checklist",
            type = "checklist",
            entries = new[]
            {
                MaterialFactory.CreateChecklistEntryWithRelated(
                    "Entry with related",
                    "This entry has a related material",
                    new List<int> { relatedMaterialId })
            }
        };

        // Act
        var checklistId = await CreateMaterialAsync(checklistRequest);
        var detail = await GetMaterialDetailAsync(checklistId);

        // Assert
        var entries = detail.GetProperty("Config").GetProperty("entries");
        entries.GetArrayLength().Should().Be(1);

        var firstEntry = entries[0];
        firstEntry.TryGetProperty("related", out var related).Should().BeTrue(
            because: "entry should have related materials");
        related.GetArrayLength().Should().Be(1);
        related[0].GetProperty("id").GetString().Should().Be(relatedMaterialId.ToString());
    }

    [Fact]
    public async Task PUT_ChecklistWithRelatedMaterials_UpdatesRelationships()
    {
        // Arrange - Create initial checklist without related materials
        var checklistRequest = MaterialFactory.CreateChecklistRequest("Initial Checklist");
        var checklistId = await CreateMaterialAsync(checklistRequest);

        // Create a related material
        var relatedMaterialId = await CreateRelatedMaterialAsync();

        // Update with related materials
        var updateRequest = new
        {
            name = "Updated Checklist",
            description = "Updated description",
            type = "checklist",
            entries = new[]
            {
                MaterialFactory.CreateChecklistEntryWithRelated(
                    "Updated Entry",
                    "Now has related material",
                    new List<int> { relatedMaterialId })
            }
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/{TenantName}/materials/{checklistId}", updateRequest);
        response.EnsureSuccessStatusCode();

        var detail = await GetMaterialDetailAsync(checklistId);

        // Assert
        var entries = detail.GetProperty("Config").GetProperty("entries");
        var firstEntry = entries[0];
        firstEntry.TryGetProperty("related", out var related).Should().BeTrue();
        related.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GET_ChecklistDetail_ReturnsEntriesWithRelatedMaterials()
    {
        // Arrange
        var relatedMaterialId1 = await CreateRelatedMaterialAsync();
        var relatedMaterialId2 = await CreateRelatedMaterialAsync();

        var checklistRequest = new
        {
            name = "Checklist for GET test",
            description = "Test",
            type = "checklist",
            entries = new[]
            {
                MaterialFactory.CreateChecklistEntryWithRelated(
                    "Entry 1",
                    "First entry",
                    new List<int> { relatedMaterialId1, relatedMaterialId2 }),
                new { text = "Entry 2", description = "No related materials" }
            }
        };

        var checklistId = await CreateMaterialAsync(checklistRequest);

        // Act
        var detail = await GetMaterialDetailAsync(checklistId);

        // Assert
        var entries = detail.GetProperty("Config").GetProperty("entries");
        entries.GetArrayLength().Should().Be(2);

        // First entry should have 2 related materials
        var entry1 = entries[0];
        entry1.TryGetProperty("related", out var related1).Should().BeTrue();
        related1.GetArrayLength().Should().Be(2);

        // Second entry should have no or empty related materials
        var entry2 = entries[1];
        if (entry2.TryGetProperty("related", out var related2))
        {
            related2.GetArrayLength().Should().Be(0);
        }
    }

    #endregion

    #region Workflow Step Tests

    [Fact]
    public async Task POST_WorkflowWithRelatedMaterials_CreatesRelationships()
    {
        // Arrange
        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var workflowRequest = new
        {
            name = "Workflow with Related Materials",
            description = "Test workflow",
            type = "workflow",
            steps = new[]
            {
                MaterialFactory.CreateWorkflowStepWithRelated(
                    "Step with related",
                    "This step has a related material",
                    new List<int> { relatedMaterialId })
            }
        };

        // Act
        var workflowId = await CreateMaterialAsync(workflowRequest);
        var detail = await GetMaterialDetailAsync(workflowId);

        // Assert
        var steps = detail.GetProperty("Config").GetProperty("steps");
        steps.GetArrayLength().Should().Be(1);

        var firstStep = steps[0];
        firstStep.TryGetProperty("related", out var related).Should().BeTrue();
        related.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task PUT_WorkflowWithRelatedMaterials_UpdatesRelationships()
    {
        // Arrange
        var workflowRequest = MaterialFactory.CreateWorkflowRequest("Initial Workflow");
        var workflowId = await CreateMaterialAsync(workflowRequest);

        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var updateRequest = new
        {
            name = "Updated Workflow",
            description = "Updated",
            type = "workflow",
            steps = new[]
            {
                MaterialFactory.CreateWorkflowStepWithRelated(
                    "Updated Step",
                    "Now has related",
                    new List<int> { relatedMaterialId })
            }
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/{TenantName}/materials/{workflowId}", updateRequest);
        response.EnsureSuccessStatusCode();

        var detail = await GetMaterialDetailAsync(workflowId);

        // Assert
        var steps = detail.GetProperty("Config").GetProperty("steps");
        steps[0].TryGetProperty("related", out var related).Should().BeTrue();
        related.GetArrayLength().Should().Be(1);
    }

    #endregion

    #region Video Timestamp Tests

    [Fact]
    public async Task POST_VideoWithRelatedMaterials_CreatesRelationships()
    {
        // Arrange
        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var videoRequest = new
        {
            name = "Video with Related Materials",
            description = "Test video",
            type = "video",
            videoPath = "/videos/test.mp4",
            timestamps = new[]
            {
                MaterialFactory.CreateTimestampWithRelated(
                    "Chapter with related",
                    "0",
                    "Has related material",
                    new List<int> { relatedMaterialId })
            }
        };

        // Act
        var videoId = await CreateMaterialAsync(videoRequest);
        var detail = await GetMaterialDetailAsync(videoId);

        // Assert
        var timestamps = detail.GetProperty("Config").GetProperty("timestamps");
        timestamps.GetArrayLength().Should().Be(1);

        var firstTimestamp = timestamps[0];
        firstTimestamp.TryGetProperty("related", out var related).Should().BeTrue();
        related.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task PUT_VideoWithRelatedMaterials_UpdatesRelationships()
    {
        // Arrange
        var videoRequest = MaterialFactory.CreateVideoRequest("Initial Video");
        var videoId = await CreateMaterialAsync(videoRequest);

        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var updateRequest = new
        {
            name = "Updated Video",
            description = "Updated",
            type = "video",
            videoPath = "/videos/test.mp4",
            timestamps = new[]
            {
                MaterialFactory.CreateTimestampWithRelated(
                    "Updated Chapter",
                    "0",
                    "Now has related",
                    new List<int> { relatedMaterialId })
            }
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/{TenantName}/materials/{videoId}", updateRequest);
        response.EnsureSuccessStatusCode();

        var detail = await GetMaterialDetailAsync(videoId);

        // Assert
        var timestamps = detail.GetProperty("Config").GetProperty("timestamps");
        timestamps[0].TryGetProperty("related", out var related).Should().BeTrue();
        related.GetArrayLength().Should().Be(1);
    }

    #endregion

    #region Quiz Question Tests

    [Fact]
    public async Task POST_QuizWithQuestionRelatedMaterials_CreatesRelationships()
    {
        // Arrange
        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var quizRequest = new
        {
            name = "Quiz with Related Materials",
            description = "Test quiz",
            type = "quiz",
            config = new
            {
                questions = new[]
                {
                    MaterialFactory.CreateQuestionWithRelated(
                        1,
                        "Question with related material",
                        new List<object>
                        {
                            new { text = "Answer 1", correctAnswer = true },
                            new { text = "Answer 2", correctAnswer = false }
                        },
                        new List<int> { relatedMaterialId })
                }
            }
        };

        // Act
        var quizId = await CreateMaterialAsync(quizRequest);
        var detail = await GetMaterialDetailAsync(quizId);

        // Assert
        var questions = detail.GetProperty("Config").GetProperty("Questions");
        questions.GetArrayLength().Should().Be(1);

        var firstQuestion = questions[0];
        firstQuestion.TryGetProperty("related", out var related).Should().BeTrue(
            because: "question should have related materials");
        related.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task PUT_QuizWithQuestionRelatedMaterials_UpdatesRelationships()
    {
        // Arrange
        var quizRequest = MaterialFactory.CreateQuizRequest("Initial Quiz");
        var quizId = await CreateMaterialAsync(quizRequest);

        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var updateRequest = new
        {
            name = "Updated Quiz",
            description = "Updated",
            type = "quiz",
            config = new
            {
                questions = new[]
                {
                    MaterialFactory.CreateQuestionWithRelated(
                        1,
                        "Updated question",
                        new List<object>
                        {
                            new { text = "Answer", correctAnswer = true }
                        },
                        new List<int> { relatedMaterialId })
                }
            }
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/{TenantName}/materials/{quizId}", updateRequest);
        response.EnsureSuccessStatusCode();

        var detail = await GetMaterialDetailAsync(quizId);

        // Assert
        var questions = detail.GetProperty("Config").GetProperty("Questions");
        questions[0].TryGetProperty("related", out var related).Should().BeTrue();
        related.GetArrayLength().Should().Be(1);
    }

    #endregion

    #region Quiz Answer Tests

    [Fact]
    public async Task POST_QuizWithAnswerRelatedMaterials_CreatesRelationships()
    {
        // Arrange
        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var quizRequest = new
        {
            name = "Quiz with Answer Related Materials",
            description = "Test quiz",
            type = "quiz",
            config = new
            {
                questions = new[]
                {
                    new
                    {
                        questionNumber = 1,
                        questionType = "multiple-choice",
                        text = "Question",
                        answers = new[]
                        {
                            MaterialFactory.CreateAnswerWithRelated(
                                "Answer with related",
                                true,
                                new List<int> { relatedMaterialId }),
                            new { text = "Answer without related", correctAnswer = false }
                        }
                    }
                }
            }
        };

        // Act
        var quizId = await CreateMaterialAsync(quizRequest);
        var detail = await GetMaterialDetailAsync(quizId);

        // Assert
        var questions = detail.GetProperty("Config").GetProperty("Questions");
        var answers = questions[0].GetProperty("Answers");
        answers.GetArrayLength().Should().Be(2);

        var firstAnswer = answers[0];
        firstAnswer.TryGetProperty("related", out var related).Should().BeTrue(
            because: "answer should have related materials");
        related.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task PUT_QuizWithAnswerRelatedMaterials_UpdatesRelationships()
    {
        // Arrange
        var quizRequest = MaterialFactory.CreateQuizRequest("Initial Quiz");
        var quizId = await CreateMaterialAsync(quizRequest);

        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var updateRequest = new
        {
            name = "Updated Quiz",
            description = "Updated",
            type = "quiz",
            config = new
            {
                questions = new[]
                {
                    new
                    {
                        questionNumber = 1,
                        questionType = "multiple-choice",
                        text = "Question",
                        answers = new[]
                        {
                            MaterialFactory.CreateAnswerWithRelated(
                                "Updated answer with related",
                                true,
                                new List<int> { relatedMaterialId })
                        }
                    }
                }
            }
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/{TenantName}/materials/{quizId}", updateRequest);
        response.EnsureSuccessStatusCode();

        var detail = await GetMaterialDetailAsync(quizId);

        // Assert
        var questions = detail.GetProperty("Config").GetProperty("Questions");
        var answers = questions[0].GetProperty("Answers");
        answers[0].TryGetProperty("related", out var related).Should().BeTrue();
        related.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GET_QuizDetail_ReturnsAnswersWithRelatedMaterials()
    {
        // Arrange
        var relatedMaterialId = await CreateRelatedMaterialAsync();

        var quizRequest = new
        {
            name = "Quiz for GET test",
            description = "Test",
            type = "quiz",
            config = new
            {
                questions = new[]
                {
                    new
                    {
                        questionNumber = 1,
                        questionType = "multiple-choice",
                        text = "Question",
                        answers = new[]
                        {
                            MaterialFactory.CreateAnswerWithRelated(
                                "Correct answer with help",
                                true,
                                new List<int> { relatedMaterialId }),
                            new { text = "Wrong answer", correctAnswer = false }
                        }
                    }
                }
            }
        };

        var quizId = await CreateMaterialAsync(quizRequest);

        // Act
        var detail = await GetMaterialDetailAsync(quizId);

        // Assert
        var questions = detail.GetProperty("Config").GetProperty("Questions");
        questions.GetArrayLength().Should().Be(1);

        var answers = questions[0].GetProperty("Answers");
        answers.GetArrayLength().Should().Be(2);

        // First answer should have related material
        var answer1 = answers[0];
        answer1.TryGetProperty("related", out var related1).Should().BeTrue();
        related1.GetArrayLength().Should().Be(1);

        // Second answer should have no related materials
        var answer2 = answers[1];
        if (answer2.TryGetProperty("related", out var related2))
        {
            related2.GetArrayLength().Should().Be(0);
        }
    }

    #endregion
}
