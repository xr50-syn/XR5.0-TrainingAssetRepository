using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Services
{
    public interface IHubUserTokenService
    {
        Task<HubDecryptResult> ValidateAsync(string token, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Validates a user's Hub login JWT by presenting it to the Hub's own
    /// <c>GET /api/v1/user/limited-info</c>: a 200 means the Hub accepts the token, and the body
    /// carries the Hub user id (<c>id</c>) and tenant (<c>tenantId</c>). No shared secret is
    /// involved; the Hub validates its own token. The token is a bearer credential: it never
    /// appears in logs, and cache entries are keyed by its SHA-256 hash.
    ///
    /// Results reuse <see cref="HubDecryptResult"/> so the handler maps both Hub credentials onto
    /// one identity path.
    /// </summary>
    public class HubUserTokenService : IHubUserTokenService
    {
        private const string LimitedInfoPath = "api/v1/user/limited-info";

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<HubUserTokenService> _logger;
        private readonly XR50HubOptions _options;

        public HubUserTokenService(
            HttpClient httpClient,
            IMemoryCache cache,
            ILogger<HubUserTokenService> logger,
            IOptions<XR50HubOptions> options)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
            _options = options.Value;

            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
            _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
        }

        public async Task<HubDecryptResult> ValidateAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return HubDecryptResult.InvalidToken("MALFORMED");
            }

            var expiresAt = ReadUnverifiedExpiry(token);
            if (expiresAt.HasValue && expiresAt.Value <= DateTimeOffset.UtcNow)
            {
                // Expired on its face; no need to ask the Hub.
                return HubDecryptResult.InvalidToken("EXPIRED");
            }

            var cacheKey = "hubjwt:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            if (_cache.TryGetValue(cacheKey, out HubDecryptResult? cached) && cached != null)
            {
                return cached;
            }

            var result = await ValidateCoreAsync(token, expiresAt, cancellationToken);

            // Cache definitive answers only; an outage must be re-evaluated on the next request.
            // A valid entry never outlives the token (its exp was read above). Revocation on the
            // Hub side takes effect here within CacheSeconds.
            if (result.Outcome is HubDecryptOutcome.Valid or HubDecryptOutcome.Invalid)
            {
                var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.CacheSeconds));
                if (expiresAt.HasValue)
                {
                    var untilExpiry = expiresAt.Value - DateTimeOffset.UtcNow;
                    ttl = untilExpiry < ttl ? untilExpiry : ttl;
                }

                if (ttl > TimeSpan.Zero)
                {
                    _cache.Set(cacheKey, result, ttl);
                }
            }

            return result;
        }

        private async Task<HubDecryptResult> ValidateCoreAsync(
            string token, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, LimitedInfoPath);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                switch ((int)response.StatusCode)
                {
                    case 200:
                        break;
                    case 401:
                    case 403:
                        _logger.LogDebug("Hub rejected a user token ({StatusCode})", (int)response.StatusCode);
                        return HubDecryptResult.InvalidToken("REJECTED");
                    default:
                        _logger.LogWarning("Hub limited-info API returned unexpected status {StatusCode}", (int)response.StatusCode);
                        return HubDecryptResult.Unavailable();
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                if (!TryReadGuid(root, "id", out var userId))
                {
                    // The Hub accepted the token but we cannot tell who it is: fail closed.
                    _logger.LogWarning("Hub limited-info response has no usable id; rejecting the user token");
                    return HubDecryptResult.InvalidToken("NO_USER_ID");
                }

                // A user without a tenant still authenticates; with no mapped tenant, tenant-scoped
                // endpoints answer 403, exactly as for an unmapped session token.
                TryReadGuid(root, "tenantId", out var tenantId);

                var claims = new HubClaims
                {
                    UserId = userId,
                    TenantId = tenantId,
                    User = new HubUser
                    {
                        Email = ReadString(root, "email"),
                        FirstName = ReadString(root, "firstName"),
                        LastName = ReadString(root, "lastName"),
                        SkillLevel = ReadString(root, "skillLevel"),
                    },
                    ExpiresAt = expiresAt?.ToUnixTimeSeconds() ?? 0,
                };

                _logger.LogDebug("Hub user token validated for user {UserId} in tenant {TenantId}", userId, tenantId);
                return HubDecryptResult.ValidToken(claims);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Hub limited-info API returned an unreadable body");
                return HubDecryptResult.Unavailable();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Hub limited-info API unreachable");
                return HubDecryptResult.Unavailable();
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Hub limited-info API timed out");
                return HubDecryptResult.Unavailable();
            }
        }

        private static bool TryReadGuid(JsonElement root, string name, out Guid value)
        {
            value = Guid.Empty;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
                && Guid.TryParse(property.GetString(), out value);
        }

        private static string? ReadString(JsonElement root, string name) =>
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        /// <summary>
        /// Reads <c>exp</c> without verifying the signature. Only used to avoid calling the Hub for
        /// a token that is expired on its face and to bound caching; the Hub decides validity.
        /// </summary>
        private static DateTimeOffset? ReadUnverifiedExpiry(string token)
        {
            var segments = token.Split('.');
            if (segments.Length != 3)
            {
                return null;
            }

            try
            {
                using var payload = JsonDocument.Parse(HubTokenReader.DecodeSegment(segments[1]));
                return payload.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
            {
                return null;
            }
        }
    }
}
