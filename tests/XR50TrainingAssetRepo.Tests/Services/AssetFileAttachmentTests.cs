using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Regression tests for attaching a file to an existing reference-only asset.
///
/// This is the second path that writes file content, and it used to skip hashing entirely. Content
/// that arrived this way kept a null hash, so it was invisible to deduplication - re-uploading the
/// same bytes created a second asset and a second copy of the file - and it landed on a
/// filename-keyed storage path where it could collide with another asset.
/// </summary>
public class AssetFileAttachmentTests : IClassFixture<AssetFileAttachmentTests.SingletonStorageFixture>
{
    private readonly SingletonStorageFixture _factory;
    private readonly HttpClient _client;
    private const string TenantName = WebApplicationFixture.TestTenant;

    public AssetFileAttachmentTests(SingletonStorageFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AttachingAFile_HashesItSoLaterDuplicateUploadsAreDetected()
    {
        var content = Bytes("content that arrives through the attach path");
        var assetId = SeedReferenceOnlyAsset("attached.pdf");

        var attach = await AttachAsync(assetId, "attached.pdf", content);
        attach.StatusCode.Should().Be(HttpStatusCode.OK);

        HashOfAsset(assetId).Should().Be(HashOf(content), "attaching a file has to hash it");
        _factory.Storage.ReadStored(TenantName, HashOf(content)).Should().Equal(content,
            "the attached file belongs on a content-addressed key");

        // The bug in full: this upload used to create a second asset and a second copy.
        var duplicate = await UploadAsync("same-bytes-different-name.pdf", "duplicate", content);
        duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync(duplicate);
        body.Reused.Should().BeTrue();
        body.Id.Should().Be(assetId.ToString());
    }

    [Fact]
    public async Task AttachingContentAnotherAssetAlreadyHolds_IsRejected()
    {
        var content = Bytes("content uploaded first and attached second");
        var created = await ReadResponseAsync(await UploadAsync("first.pdf", "the original", content));

        var targetId = SeedReferenceOnlyAsset("second.pdf");
        var attach = await AttachAsync(targetId, "second.pdf", content);

        attach.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "attaching targets one specific asset, so there is no duplicate to silently reuse");
        (await attach.Content.ReadAsStringAsync()).Should().Contain(created.Id);

        // The rejected attach must not have disturbed the asset that legitimately owns the content.
        _factory.Storage.ReadStored(TenantName, HashOf(content)).Should().Equal(content);
        HashOfAsset(targetId).Should().BeNull("the rejected attach leaves the target untouched");
    }

    /// <summary>
    /// Seeds an asset with no URL or Src - the reference-only shape the attach endpoint accepts.
    /// The id is left to the store so it cannot collide with ids handed out by uploads.
    /// </summary>
    private int SeedReferenceOnlyAsset(string filename)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();
        using var context = contextFactory.CreateDbContext();
        var asset = new Asset
        {
            Filename = filename,
            Filetype = "pdf",
            Type = AssetType.PDF,
            AiAvailable = "notready"
        };
        context.Assets.Add(asset);
        context.SaveChanges();
        return asset.Id;
    }

    private string? HashOfAsset(int assetId)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();
        using var context = contextFactory.CreateDbContext();
        return context.Assets.AsNoTracking().Single(a => a.Id == assetId).ContentHash;
    }

    private static byte[] Bytes(string body) => Encoding.UTF8.GetBytes("%PDF-1.7\n" + body);

    private static string HashOf(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private async Task<HttpResponseMessage> AttachAsync(int assetId, string filename, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", filename);
        return await _client.PostAsync($"/api/{TenantName}/assets/{assetId}/upload", form);
    }

    private async Task<HttpResponseMessage> UploadAsync(string filename, string description, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "File", filename);
        form.Add(new StringContent(description), "Description");
        return await _client.PostAsync($"/api/{TenantName}/assets", form);
    }

    private static async Task<TestAssetResponse> ReadResponseAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TestAssetResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    private sealed record TestAssetResponse(string Id, string Filename, bool Reused);

    public sealed class SingletonStorageFixture : WebApplicationFixture
    {
        public MockStorageService Storage { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.Single(service => service.ServiceType == typeof(IStorageService));
                services.Remove(descriptor);
                services.AddSingleton<IStorageService>(Storage);
            });
        }
    }
}
