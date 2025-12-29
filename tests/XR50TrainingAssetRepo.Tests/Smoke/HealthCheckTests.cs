using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Smoke;

/// <summary>
/// Basic smoke tests to verify the API is running and accessible.
/// </summary>
public class HealthCheckTests : IClassFixture<WebApplicationFixture>
{
    private readonly HttpClient _client;

    public HealthCheckTests(WebApplicationFixture factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TestEndpoint_ReturnsOk()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/api/test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SwaggerEndpoint_IsAccessible_InDevelopment()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/swagger/index.html");

        // Assert - Should be accessible in Development environment
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task MaterialsEndpoint_ReturnsSuccessStatusCode()
    {
        // Arrange
        var client = _client;

        // Act
        var response = await client.GetAsync($"/api/{WebApplicationFixture.TestTenant}/materials");

        // Assert - Should return OK (empty list) or other success code
        response.IsSuccessStatusCode.Should().BeTrue(
            because: $"materials endpoint should be accessible, but got {response.StatusCode}");
    }

    [Fact]
    public async Task TrainingProgramsEndpoint_ReturnsSuccessStatusCode()
    {
        // Arrange & Act
        var response = await _client.GetAsync($"/api/{WebApplicationFixture.TestTenant}/programs");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue(
            because: $"programs endpoint should be accessible, but got {response.StatusCode}");
    }

    [Fact]
    public async Task LearningPathsEndpoint_ReturnsSuccessStatusCode()
    {
        // Arrange & Act
        var response = await _client.GetAsync($"/api/{WebApplicationFixture.TestTenant}/learningpaths");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue(
            because: $"learning paths endpoint should be accessible, but got {response.StatusCode}");
    }
}
