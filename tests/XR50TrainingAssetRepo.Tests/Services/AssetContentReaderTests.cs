using Microsoft.Extensions.Logging.Abstractions;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// AssetContentReader decides whether an asset's bytes come from storage or from its URL: an
/// uploaded asset (storage key) and a legacy row whose file sits under its filename are read from
/// storage; an asset with no stored file is reference-only and yields null.
/// </summary>
public class AssetContentReaderTests
{
    private const string Tenant = "test_tenant";

    private static async Task<MockStorageService> StorageWithAsync(string key, byte[] bytes)
    {
        var storage = new MockStorageService();
        await storage.UploadFileAsync(Tenant, key, new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", key));
        return storage;
    }

    private static AssetContentReader ReaderOver(MockStorageService storage) =>
        new(storage, new FixedTenantService(Tenant), NullLogger<AssetContentReader>.Instance);

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task UploadedAsset_IsReadFromStorageByItsKey()
    {
        var bytes = "%PDF stored"u8.ToArray();
        var reader = ReaderOver(await StorageWithAsync("content-hash", bytes));

        var stream = await reader.OpenStoredContentAsync(new Asset { Id = 1, Filename = "doc.pdf", StorageKey = "content-hash", URL = "http://unreachable/doc.pdf" });

        stream.Should().NotBeNull();
        (await ReadAllAsync(stream!)).Should().Equal(bytes);
    }

    [Fact]
    public async Task LegacyAssetWithoutKey_IsReadFromStorageByFilename()
    {
        var bytes = "%PDF legacy"u8.ToArray();
        var reader = ReaderOver(await StorageWithAsync("legacy.pdf", bytes));

        var stream = await reader.OpenStoredContentAsync(new Asset { Id = 2, Filename = "legacy.pdf" });

        stream.Should().NotBeNull();
        (await ReadAllAsync(stream!)).Should().Equal(bytes);
    }

    [Fact]
    public async Task ReferenceOnlyAsset_ReturnsNull()
    {
        var reader = ReaderOver(new MockStorageService());

        var stream = await reader.OpenStoredContentAsync(new Asset { Id = 3, Filename = "linked.pdf", URL = "https://example.test/linked.pdf" });

        stream.Should().BeNull();
    }

    private sealed class FixedTenantService(string tenantName) : IXR50TenantService
    {
        public string GetCurrentTenant() => tenantName;
        public Task<bool> ValidateTenantAsync(string name) => Task.FromResult(true);
        public Task<bool> TenantExistsAsync(string name) => Task.FromResult(true);
        public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
        public string GetTenantSchema(string name) => XR50TenantDatabase.SchemaFor(name);
    }
}
