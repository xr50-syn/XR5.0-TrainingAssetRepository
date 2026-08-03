using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory that leaves the real authentication wiring in place (scheme selector +
/// HubSessionTokenAuthenticationHandler) and swaps only the outward-facing pieces: the decrypt
/// client and the local identity enricher become programmable fakes, so tests drive the full
/// Hub authentication path without a network or a MySQL registry.
/// </summary>
public class HubAuthWebApplicationFixture : WebApplicationFactory<Program>
{
    public const string TestTenant = WebApplicationFixture.TestTenant;
    public const string DevelopmentToken = "dev-token-fixed";

    private readonly string _databaseName = $"TestDatabase_{Guid.NewGuid()}";

    public FakeHubSessionTokenService TokenService { get; } = new();
    public FakeHubIdentityEnricher Enricher { get; } = new();

    protected virtual string EnvironmentName => "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbName = _databaseName;

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=test_db;",
                ["BaseDatabaseName"] = "test_db",
                ["IAM:Issuer"] = "test-issuer",
                ["IAM:Audience"] = "test-audience",
                ["IAM:AllowAnonymousInDevelopment"] = "false",
                ["Storage:Type"] = "InMemory",
                ["XR50Hub:BaseUrl"] = "https://hub.test",
                ["XR50Hub:SharedSecret"] = "test-secret",
                ["XR50Hub:DevelopmentToken"] = DevelopmentToken,
                ["XR50Hub:CacheSeconds"] = "1",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            WebApplicationFixture.ConfigureHermeticServices(services, dbName);

            // Replace the typed decrypt client and the DB-backed enricher with fakes; the
            // authentication schemes registered in Program.cs stay untouched.
            var tokenServiceDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IHubSessionTokenService));
            if (tokenServiceDescriptor != null)
            {
                services.Remove(tokenServiceDescriptor);
            }
            services.AddSingleton<IHubSessionTokenService>(TokenService);

            var enricherDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IHubIdentityEnricher));
            if (enricherDescriptor != null)
            {
                services.Remove(enricherDescriptor);
            }
            services.AddSingleton<IHubIdentityEnricher>(Enricher);
        });

        builder.UseEnvironment(EnvironmentName);
    }
}

/// <summary>Same fixture hosted as Production: JWT bearer is absent and the development token
/// must NOT short-circuit, so unknown tokens go through the (fake) decrypt flow.</summary>
public class HubAuthProductionFixture : HubAuthWebApplicationFixture
{
    protected override string EnvironmentName => "Production";
}

/// <summary>Programmable stand-in for the Hub decrypt API client.</summary>
public class FakeHubSessionTokenService : IHubSessionTokenService
{
    private readonly Dictionary<string, HubDecryptResult> _results = new();

    public int DecryptCallCount { get; private set; }

    public void SetResult(string token, HubDecryptResult result) => _results[token] = result;

    public Task<HubDecryptResult> DecryptAsync(string token, CancellationToken cancellationToken = default)
    {
        DecryptCallCount++;
        return Task.FromResult(_results.TryGetValue(token, out var result)
            ? result
            : HubDecryptResult.InvalidToken("MALFORMED"));
    }
}

/// <summary>Programmable stand-in for the registry/tenant-DB identity enricher, keyed by the
/// Hub tenantId claim.</summary>
public class FakeHubIdentityEnricher : IHubIdentityEnricher
{
    private readonly Dictionary<Guid, HubLocalIdentity> _identities = new();

    public void SetIdentity(Guid hubTenantId, HubLocalIdentity identity) => _identities[hubTenantId] = identity;

    public Task<HubLocalIdentity> ResolveAsync(HubClaims claims, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_identities.TryGetValue(claims.TenantId, out var identity)
            ? identity
            : HubLocalIdentity.Unmapped);
    }
}
