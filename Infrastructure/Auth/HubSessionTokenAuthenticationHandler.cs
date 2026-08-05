using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Infrastructure.Auth
{
    /// <summary>
    /// Authenticates requests carrying an XR5.0 Hub session token (HL-Hub-Session-Token header).
    /// The opaque token is validated through the Hub decrypt API and the returned claims are
    /// projected onto the claim names the authorization handlers already consume
    /// (preferred_username / tenantName / role). The token is a bearer credential and must
    /// never be logged or echoed in responses.
    /// </summary>
    public class HubSessionTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        // Fixed identity of the spec's development token; only reachable in Development.
        private static readonly Guid DevUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        private static readonly Guid DevTenantId = Guid.Parse("976092b0-0ca8-404d-99b8-30a8c755719c");

        private readonly IHubSessionTokenService _tokenService;
        private readonly IHubIdentityEnricher _identityEnricher;
        private readonly IWebHostEnvironment _environment;
        private readonly XR50HubOptions _hubOptions;
        private readonly IamOptions _iamOptions;

        public HubSessionTokenAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IHubSessionTokenService tokenService,
            IHubIdentityEnricher identityEnricher,
            IWebHostEnvironment environment,
            IOptions<XR50HubOptions> hubOptions,
            IOptions<IamOptions> iamOptions)
            : base(options, logger, encoder)
        {
            _tokenService = tokenService;
            _identityEnricher = identityEnricher;
            _environment = environment;
            _hubOptions = hubOptions.Value;
            _iamOptions = iamOptions.Value;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HubSessionTokenDefaults.HeaderName, out var headerValues))
            {
                return AuthenticateResult.NoResult();
            }

            var token = headerValues.ToString();
            if (string.IsNullOrWhiteSpace(token))
            {
                return AuthenticateResult.Fail("Empty Hub session token");
            }

            HubClaims claims;
            if (IsDevelopmentToken(token))
            {
                // The fixed identity still flows through the normal tenant-mapping and role
                // lookup so local development exercises the production code path.
                Logger.LogDebug("Hub development token accepted (Development environment only)");
                claims = CreateDevelopmentClaims();
            }
            else
            {
                var result = await _tokenService.DecryptAsync(token, Context.RequestAborted);
                switch (result.Outcome)
                {
                    case HubDecryptOutcome.Valid:
                        claims = result.Claims!;
                        break;
                    case HubDecryptOutcome.Invalid:
                        Logger.LogDebug("Hub session token rejected: {Reason}", result.Reason ?? "unknown");
                        return AuthenticateResult.Fail($"Hub session token rejected: {result.Reason ?? "invalid"}");
                    case HubDecryptOutcome.SecretRejected:
                        // Our misconfiguration, but the caller's token cannot be proven valid: fail closed.
                        return AuthenticateResult.Fail("Hub integration misconfigured");
                    default:
                        Context.Items[HubSessionTokenDefaults.FailureKindItem] = HubSessionTokenDefaults.FailureKindUnavailable;
                        return AuthenticateResult.Fail("Hub session token service temporarily unavailable");
                }
            }

            var localIdentity = await _identityEnricher.ResolveAsync(claims, Context.RequestAborted);
            var principal = BuildPrincipal(claims, localIdentity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            if (Equals(Context.Items[HubSessionTokenDefaults.FailureKindItem], HubSessionTokenDefaults.FailureKindUnavailable))
            {
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Task.CompletedTask;
            }

            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = HubSessionTokenDefaults.HeaderName;
            return Task.CompletedTask;
        }

        private bool IsDevelopmentToken(string token)
        {
            // Double gate: the environment must be Development AND a token must be configured,
            // so a leaked XR50HUB_DEV_TOKEN cannot authenticate against a production deployment.
            if (!_environment.IsDevelopment() || string.IsNullOrEmpty(_hubOptions.DevelopmentToken))
            {
                return false;
            }

            var provided = Encoding.UTF8.GetBytes(token);
            var expected = Encoding.UTF8.GetBytes(_hubOptions.DevelopmentToken);
            return CryptographicOperations.FixedTimeEquals(provided, expected);
        }

        private static HubClaims CreateDevelopmentClaims()
        {
            return new HubClaims
            {
                Version = 1,
                UserId = DevUserId,
                TenantId = DevTenantId,
                SessionId = Guid.Empty,
                ApplicationId = Guid.Empty,
                User = new HubUser
                {
                    FirstName = "Dev",
                    LastName = "Tester",
                    Email = "dev-test@holo-light.com",
                    SkillLevel = "Advanced",
                },
                IssuedAt = 0,
                ExpiresAt = 0,
            };
        }

        private ClaimsPrincipal BuildPrincipal(HubClaims hubClaims, HubLocalIdentity localIdentity)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, hubClaims.UserId.ToString("D")),
                new("sub", hubClaims.UserId.ToString("D")),
                new(HubSessionTokenDefaults.SessionIdClaim, hubClaims.SessionId.ToString("D")),
                new(HubSessionTokenDefaults.ApplicationIdClaim, hubClaims.ApplicationId.ToString("D")),
                new(HubSessionTokenDefaults.HubTenantIdClaim, hubClaims.TenantId.ToString("D")),
            };

            // preferred_username heads the GetUserId() fallback chain; prefer the matched local
            // user name so progress records keep keying on the same ids as the rest of the app.
            // E-mail-less identities (service accounts) attribute as their Hub userId GUID -
            // the same value the enricher joins on, so provisioned rows and written records agree.
            var userId = localIdentity.LocalUserName;
            if (string.IsNullOrEmpty(userId))
            {
                userId = hubClaims.User.Email;
            }
            if (string.IsNullOrEmpty(userId))
            {
                userId = hubClaims.UserId.ToString("D");
            }
            claims.Add(new Claim("preferred_username", userId));

            if (!string.IsNullOrEmpty(hubClaims.User.Email))
            {
                claims.Add(new Claim("email", hubClaims.User.Email));
            }

            var fullName = $"{hubClaims.User.FirstName} {hubClaims.User.LastName}".Trim();
            if (!string.IsNullOrEmpty(fullName))
            {
                claims.Add(new Claim("name", fullName));
            }

            if (!string.IsNullOrEmpty(hubClaims.User.SkillLevel))
            {
                claims.Add(new Claim(HubSessionTokenDefaults.SkillLevelClaim, hubClaims.User.SkillLevel));
            }

            if (!string.IsNullOrEmpty(localIdentity.TenantName))
            {
                claims.Add(new Claim(_iamOptions.TenantClaim, localIdentity.TenantName));
            }

            if (localIdentity.IsTenantAdmin)
            {
                claims.Add(new Claim(_iamOptions.RoleClaim, "tenantadmin"));
            }

            if (localIdentity.IsSystemAdmin)
            {
                claims.Add(new Claim(_iamOptions.RoleClaim, "systemadmin"));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name, "preferred_username", _iamOptions.RoleClaim);
            return new ClaimsPrincipal(identity);
        }
    }
}
