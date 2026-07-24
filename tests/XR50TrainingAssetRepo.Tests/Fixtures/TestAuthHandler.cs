using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XR50TrainingAssetRepo.Tests.Fixtures;

/// <summary>
/// Authentication handler for hermetic tests. Issues a principal built from request headers so
/// individual tests can exercise every authorization branch without a real token:
///   X-Test-Anonymous: true  -> no principal (endpoints behind auth respond 401)
///   X-Test-User             -> preferred_username claim (default: testuser)
///   X-Test-Roles            -> comma-separated role claim values (default: systemadmin,
///                              which passes every policy and bypasses tenant matching,
///                              keeping pre-existing tests working unchanged)
///   X-Test-Tenant           -> tenantName claim (omitted when the header is absent)
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public const string AnonymousHeader = "X-Test-Anonymous";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";
    public const string TenantHeader = "X-Test-Tenant";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.TryGetValue(AnonymousHeader, out var anon) &&
            string.Equals(anon.ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var user = Request.Headers.TryGetValue(UserHeader, out var u) ? u.ToString() : "testuser";
        var roles = Request.Headers.TryGetValue(RolesHeader, out var r) ? r.ToString() : "systemadmin";

        var claims = new List<Claim>
        {
            new("preferred_username", user),
            new(ClaimTypes.Name, user)
        };

        foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim("role", role));
        }

        if (Request.Headers.TryGetValue(TenantHeader, out var tenant))
        {
            claims.Add(new Claim("tenantName", tenant.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
