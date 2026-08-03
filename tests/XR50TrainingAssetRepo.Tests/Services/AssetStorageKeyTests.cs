using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Regression tests for content-addressed storage keys.
///
/// Storage objects used to be keyed on the client-supplied filename, so two assets uploaded under
/// one name shared a single object. That let a second upload silently overwrite the first asset's
/// content, and let deleting either row remove the file the other still pointed at - the deletion
/// guard only protects the row being deleted, so it never fired. Keying on the content hash makes
/// both impossible: the unique index on ContentHash means one row owns exactly one object.
/// </summary>
public class AssetStorageKeyTests : IClassFixture<AssetStorageKeyTests.SingletonStorageFixture>
{
    private readonly SingletonStorageFixture _factory;
    private readonly HttpClient _client;
    private const string TenantName = WebApplicationFixture.TestTenant;

    public AssetStorageKeyTests(SingletonStorageFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SameFilenameDifferentContent_KeepsBothFilesUnderDistinctKeys()
    {
        var original = Bytes("the original file a material depends on");
        var replacement = Bytes("an unrelated file that happens to share a name");

        var first = await ReadResponseAsync(await UploadAsync("report.pdf", "original", original));
        var second = await ReadResponseAsync(await UploadAsync("report.pdf", "unrelated", replacement));

        second.Id.Should().NotBe(first.Id, "different content is not a duplicate");

        // The heart of the bug: one name, two assets, but no longer one shared object.
        var storage = Storage();
        storage.ReadStored(TenantName, HashOf(original)).Should().Equal(original,
            "the first asset's content must survive a same-named upload");
        storage.ReadStored(TenantName, HashOf(replacement)).Should().Equal(replacement);
    }

    [Fact]
    public async Task DeletingSameNamedAsset_LeavesTheOtherAssetsFileIntact()
    {
        var kept = Bytes("content behind the asset a material still uses");
        var deleted = Bytes("content behind the duplicate that gets removed");

        var keptAsset = await ReadResponseAsync(await UploadAsync("manual.pdf", "keep me", kept));
        var deletedAsset = await ReadResponseAsync(await UploadAsync("manual.pdf", "delete me", deleted));

        var response = await _client.DeleteAsync($"/api/{TenantName}/assets/{deletedAsset.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var storage = Storage();
        storage.ReadStored(TenantName, HashOf(deleted)).Should().BeNull("the deleted asset's file is gone");
        storage.ReadStored(TenantName, HashOf(kept)).Should().Equal(kept,
            "deleting a same-named asset must not remove another asset's file");

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();
        using var context = contextFactory.CreateDbContext();
        context.Assets.Any(a => a.Id.ToString() == keptAsset.Id).Should().BeTrue();
    }

    [Fact]
    public async Task RenamingAnAssetThroughPut_DoesNotOrphanItsFile()
    {
        var content = Bytes("content whose asset later gets renamed");
        var created = await ReadResponseAsync(await UploadAsync("before.pdf", "original name", content));

        var put = await _client.PutAsync($"/api/{TenantName}/assets/{created.Id}",
            JsonContent(new
            {
                Id = int.Parse(created.Id),
                Filename = "after-the-rename.pdf",
                Description = "renamed",
                Filetype = "pdf",
                Type = 1,
                URL = created.URL
            }));
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The key was recorded at upload time and a metadata update preserves it, so a rename cannot
        // move the asset off its object.
        Storage().ReadStored(TenantName, HashOf(content)).Should().Equal(content);

        using (var scope = _factory.Services.CreateScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();
            using var context = contextFactory.CreateDbContext();
            var stored = context.Assets.AsNoTracking().Single(a => a.Id.ToString() == created.Id);
            stored.Filename.Should().Be("after-the-rename.pdf", "the display name is free to change");
            stored.StorageKey.Should().Be(HashOf(content), "the key it points at is not");
        }

        var fileInfo = await _client.GetAsync($"/api/{TenantName}/assets/{created.Id}/file-info");
        fileInfo.StatusCode.Should().Be(HttpStatusCode.OK);
        var info = await fileInfo.Content.ReadAsStringAsync();
        info.Should().Contain("\"fileExists\":true", "the renamed asset still resolves to its file");
    }

    [Fact]
    public async Task UploadPreservesTheFriendlyNameForDownloads()
    {
        var content = Bytes("content served under a human-readable name");
        await UploadAsync("quarterly-report.pdf", "friendly name", content);

        Storage().DownloadNameFor(TenantName, HashOf(content))
            .Should().Be("quarterly-report.pdf",
                "the hash key is opaque, so the original name has to travel with the upload");
    }

    private MockStorageService Storage() => _factory.Storage;

    private static byte[] Bytes(string body) => Encoding.UTF8.GetBytes("%PDF-1.7\n" + body);

    private static string HashOf(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

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

    private sealed record TestAssetResponse(string Id, string Filename, string? URL, bool Reused);

    /// <summary>Shares one storage instance across the class so uploads and deletes hit the same store.</summary>
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
