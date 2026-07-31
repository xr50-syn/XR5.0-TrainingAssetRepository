using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Services;

public class AssetCreationDeduplicationTests : IClassFixture<AssetCreationDeduplicationTests.SingletonStorageFixture>
{
    private readonly SingletonStorageFixture _factory;
    private readonly HttpClient _client;
    private const string TenantName = WebApplicationFixture.TestTenant;

    public AssetCreationDeduplicationTests(SingletonStorageFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UploadingIdenticalContent_ReusesExistingAssetAndStoredFile()
    {
        var content = "%PDF-1.7\nidentical test document"u8.ToArray();

        var firstResponse = await UploadAsync("first.pdf", "first description", content);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var first = await ReadResponseAsync(firstResponse);
        first.Reused.Should().BeFalse();

        var secondResponse = await UploadAsync("renamed.pdf", "different description", content);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await ReadResponseAsync(secondResponse);
        second.Reused.Should().BeTrue();
        second.Id.Should().Be(first.Id);
        second.Filename.Should().Be("first.pdf");

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>();
        using var context = contextFactory.CreateDbContext();
        context.Assets.Should().ContainSingle();

        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        var statistics = await storage.GetStorageStatisticsAsync(TenantName);
        statistics.TotalFiles.Should().Be(1);
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
        private readonly MockStorageService _storage = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.Single(service => service.ServiceType == typeof(IStorageService));
                services.Remove(descriptor);
                services.AddSingleton<IStorageService>(_storage);
            });
        }
    }
}
