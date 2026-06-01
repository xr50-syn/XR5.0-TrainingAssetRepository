using System.Net.Http.Headers;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Verifies POST /ai-assistant/{id}/documents creates the uploaded file as a real tenant Asset and
/// attaches it to the assistant as a TRACKED document (visible in /documents), rather than pushing
/// it straight to the collection untracked. Hermetic: InMemory DB + MockStorageService; the
/// DataLens submit is unreachable in-fixture, so the endpoint returns "partial" with the asset still
/// created and attached.
/// </summary>
public class AIAssistantAddDocumentTests : IClassFixture<WebApplicationFixture>
{
    private readonly HttpClient _client;

    public AIAssistantAddDocumentTests(WebApplicationFixture factory)
    {
        _client = factory.CreateClient();
    }

    private static readonly byte[] MinimalPdf = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    [Fact]
    public async Task UploadDocument_CreatesAndAttaches_AsTrackedDocument()
    {
        var tenant = WebApplicationFixture.TestTenant;

        // Create an AI Assistant material (explicit collection avoids the tenant-default lookup).
        var create = await _client.PostAsJsonAsync($"/api/{tenant}/materials", new
        {
            name = "Doc-add target",
            type = "ai_assistant",
            collectionName = "docs_add_collection"
        });
        create.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateMaterialResponse>();
        var matId = created!.id;

        // Upload a document to the material.
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(MinimalPdf);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "added-doc.pdf");

        var upload = await _client.PostAsync($"/api/{tenant}/ai-assistant/{matId}/documents", form);
        upload.IsSuccessStatusCode.Should().BeTrue($"upload should succeed, got {upload.StatusCode}");

        using var body = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var root = body.RootElement;
        // success, or partial when the DataLens submit is unreachable (fixture) — never a hard failure.
        root.GetProperty("status").GetString().Should().BeOneOf("success", "partial");
        root.GetProperty("assetId").GetString().Should().NotBeNullOrEmpty("an Asset must have been created");

        // The new document must be a TRACKED asset of the assistant (listed in /documents).
        var docsResp = await _client.GetAsync($"/api/{tenant}/ai-assistant/{matId}/documents");
        docsResp.IsSuccessStatusCode.Should().BeTrue();
        var docsJson = await docsResp.Content.ReadAsStringAsync();
        docsJson.Should().Contain("added-doc.pdf",
            "the uploaded file must become a tracked asset listed in the assistant's documents");
    }
}
