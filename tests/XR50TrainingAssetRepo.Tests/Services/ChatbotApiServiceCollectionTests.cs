using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Exercises the real <see cref="ChatbotApiService"/> HTTP logic for DataLens collection handling
/// through a programmable fake handler (no MySQL/MinIO/DataLens needed).
///
/// Reproduces a partner report: "submitting PDFs for processing throws Bad Gateway, which goes away
/// if I manually create the collection". The hypothesis under test is that DataLens (behind a
/// reverse proxy) does NOT always answer the existence-check GET with a clean 404 when a collection
/// is missing -- a cold/unavailable upstream returns 502 Bad Gateway. EnsureCollectionExistsAsync
/// only auto-creates on exactly 404, so any other status aborts and the collection is never created.
/// </summary>
public class ChatbotApiServiceCollectionTests
{
    private const string Collection = "demo";
    private const string ExistsPath = "/api/v1/collections/demo";
    private const string CreatePath = "/api/v1/collections";

    private static ChatbotApiService CreateService(FakeHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatbotApi:BaseUrl"] = "http://datalens.test",
            })
            .Build();

        return new ChatbotApiService(
            new HttpClient(handler),
            NullLogger<ChatbotApiService>.Instance,
            config);
    }

    [Fact]
    public async Task EnsureCollection_WhenMissing_AutoCreatesViaPost()
    {
        // Happy path: GET says 404 (missing), so the service POSTs to create it.
        var handler = new FakeHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == ExistsPath)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == CreatePath)
                return new HttpResponseMessage(HttpStatusCode.Created);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        var service = CreateService(handler);

        var result = await service.EnsureCollectionExistsAsync(Collection);

        result.Should().BeTrue();
        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.Path == CreatePath,
            "a missing (404) collection must trigger creation");
    }

    [Fact]
    public async Task EnsureCollection_WhenExistenceCheckReturnsBadGateway_ThrowsAndNeverCreates()
    {
        // Partner repro: the existence GET comes back 502 (gateway), not 404. The service treats any
        // non-404 as fatal and throws WITHOUT attempting creation -- so the collection stays missing
        // and the subsequent document submit fails. Manually creating the collection (GET -> 200)
        // makes this branch a no-op, which is why the partner's workaround works.
        var handler = new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("Bad Gateway"),
            });
        var service = CreateService(handler);

        var act = async () => await service.EnsureCollectionExistsAsync(Collection);

        await act.Should().ThrowAsync<ChatbotApiException>().WithMessage("*check collection: BadGateway*");
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Post,
            "the service aborts on a non-404 existence check instead of attempting creation");
    }

    [Fact]
    public async Task EnsureCollection_WhenCreateReturnsBadGateway_Throws()
    {
        // Alternate repro: existence check is a clean 404, but the create POST itself 502s.
        var handler = new FakeHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == ExistsPath)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("Bad Gateway"),
            };
        });
        var service = CreateService(handler);

        var act = async () => await service.EnsureCollectionExistsAsync(Collection);

        await act.Should().ThrowAsync<ChatbotApiException>().WithMessage("*create collection: BadGateway*");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<(HttpMethod Method, string Path)> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(_responder(request));
        }
    }
}
