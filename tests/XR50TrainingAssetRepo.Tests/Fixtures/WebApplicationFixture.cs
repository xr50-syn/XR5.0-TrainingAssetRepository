using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Tests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory that configures the test server with in-memory database
/// and mock storage service for testing.
/// </summary>
public class WebApplicationFixture : WebApplicationFactory<Program>
{
    public const string TestTenant = "testTenant";

    // Use a consistent database name per fixture instance for test isolation
    private readonly string _databaseName = $"TestDatabase_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbName = _databaseName;

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Add test-specific configuration
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=test_db;",
                ["BaseDatabaseName"] = "test_db",
                ["IAM:Issuer"] = "test-issuer",
                ["IAM:Audience"] = "test-audience",
                ["Storage:Type"] = "InMemory"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<XR50TrainingContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Remove the DbContext itself
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(XR50TrainingContext));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // Remove the DbContextFactory
            var factoryDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IXR50TenantDbContextFactory));
            if (factoryDescriptor != null)
            {
                services.Remove(factoryDescriptor);
            }

            // Add in-memory database with a consistent name for all requests in this fixture.
            // InMemory has no real transactions; production services call BeginTransactionAsync,
            // so we silence the warning rather than fail every transactional test path.
            services.AddDbContext<XR50TrainingContext>(options =>
            {
                options.UseInMemoryDatabase(dbName)
                       .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            // Register test DbContext factory that returns the in-memory context
            services.AddScoped<IXR50TenantDbContextFactory, TestDbContextFactory>();

            // Replace storage service with mock
            var storageDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IStorageService));
            if (storageDescriptor != null)
            {
                services.Remove(storageDescriptor);
            }
            services.AddScoped<IStorageService, MockStorageService>();
        });

        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Creates an HttpClient configured for the test tenant.
    /// </summary>
    public HttpClient CreateTenantClient(string tenantName = TestTenant)
    {
        var client = CreateClient();
        client.BaseAddress = new Uri($"http://localhost/api/{tenantName}/");
        return client;
    }
}

/// <summary>
/// Test implementation of IXR50TenantDbContextFactory.
/// Creates a fresh context per call so production code's `using var ctx = factory.CreateDbContext()`
/// pattern works correctly. All contexts share the same InMemory database name (set in
/// WebApplicationFixture._databaseName), so data persists across calls within a fixture instance.
/// </summary>
public class TestDbContextFactory : IXR50TenantDbContextFactory
{
    private readonly DbContextOptions<XR50TrainingContext> _options;

    public TestDbContextFactory(DbContextOptions<XR50TrainingContext> options)
    {
        _options = options;
    }

    public XR50TrainingContext CreateDbContext() => new XR50TrainingContext(_options);

    public XR50TrainingContext CreateAdminDbContext() => new XR50TrainingContext(_options);
}

/// <summary>
/// Mock storage service for testing - stores files in memory.
/// </summary>
public class MockStorageService : IStorageService
{
    private readonly Dictionary<string, byte[]> _files = new();
    private readonly Dictionary<string, string> _shares = new();

    public Task<bool> CreateTenantStorageAsync(string tenantName, XR50Tenant tenant) => Task.FromResult(true);

    public Task<bool> DeleteTenantStorageAsync(string tenantName) => Task.FromResult(true);

    public Task<bool> TenantStorageExistsAsync(string tenantName) => Task.FromResult(true);

    public async Task<string> UploadFileAsync(string tenantName, string fileName, IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        _files[$"{tenantName}/{fileName}"] = ms.ToArray();
        return $"http://localhost/files/{tenantName}/{fileName}";
    }

    public Task<Stream> DownloadFileAsync(string tenantName, string fileName)
    {
        var key = $"{tenantName}/{fileName}";
        if (_files.TryGetValue(key, out var data))
        {
            return Task.FromResult<Stream>(new MemoryStream(data));
        }
        throw new FileNotFoundException($"File not found: {key}");
    }

    public Task<string> GetDownloadUrlAsync(string tenantName, string fileName, TimeSpan? expiration = null)
    {
        return Task.FromResult($"http://localhost/files/{tenantName}/{fileName}");
    }

    public Task<bool> DeleteFileAsync(string tenantName, string fileName)
    {
        var key = $"{tenantName}/{fileName}";
        return Task.FromResult(_files.Remove(key));
    }

    public Task<bool> FileExistsAsync(string tenantName, string fileName)
    {
        return Task.FromResult(_files.ContainsKey($"{tenantName}/{fileName}"));
    }

    public Task<long> GetFileSizeAsync(string tenantName, string fileName)
    {
        var key = $"{tenantName}/{fileName}";
        if (_files.TryGetValue(key, out var data))
        {
            return Task.FromResult((long)data.Length);
        }
        return Task.FromResult(0L);
    }

    public Task<string> CreateShareAsync(string tenantName, XR50Tenant tenant, Asset asset)
    {
        var shareId = Guid.NewGuid().ToString();
        _shares[shareId] = $"{tenantName}/{asset.Filename}";
        return Task.FromResult(shareId);
    }

    public Task<bool> DeleteShareAsync(string tenantName, string shareId)
    {
        return Task.FromResult(_shares.Remove(shareId));
    }

    public bool SupportsSharing() => true;

    public Task<StorageStatistics> GetStorageStatisticsAsync(string tenantName)
    {
        var tenantFiles = _files.Where(f => f.Key.StartsWith($"{tenantName}/")).ToList();
        return Task.FromResult(new StorageStatistics
        {
            TenantName = tenantName,
            StorageType = "InMemory",
            TotalFiles = tenantFiles.Count,
            TotalSizeBytes = tenantFiles.Sum(f => f.Value.Length),
            LastCalculated = DateTime.UtcNow
        });
    }

    public string GetStorageType() => "InMemory";
}
