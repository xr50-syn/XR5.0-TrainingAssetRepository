using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Services
{
    public interface IHubSessionTokenService
    {
        Task<HubDecryptResult> DecryptAsync(string token, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Client for the XR5.0 Hub session-token decrypt API. The session token is a bearer
    /// credential: it must never appear in logs, URLs, or cache keys (cache entries are keyed
    /// by a SHA-256 hash of the token).
    /// </summary>
    public class HubSessionTokenService : IHubSessionTokenService
    {
        private const string DecryptPath = "api/v1/session-token/decrypt";
        private const string SecretHeaderName = "hl-hub-external-service-secret";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<HubSessionTokenService> _logger;
        private readonly XR50HubOptions _options;

        public HubSessionTokenService(
            HttpClient httpClient,
            IMemoryCache cache,
            ILogger<HubSessionTokenService> logger,
            IOptions<XR50HubOptions> options)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
            _options = options.Value;

            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
            _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
            if (!string.IsNullOrEmpty(_options.SharedSecret))
            {
                _httpClient.DefaultRequestHeaders.Add(SecretHeaderName, _options.SharedSecret);
            }
        }

        public async Task<HubDecryptResult> DecryptAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return HubDecryptResult.InvalidToken("MALFORMED");
            }

            var cacheKey = "hubtok:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            if (_cache.TryGetValue(cacheKey, out HubDecryptResult? cached) && cached != null)
            {
                // A cached Valid entry must never outlive the token itself.
                if (cached.Outcome != HubDecryptOutcome.Valid || !IsExpired(cached.Claims))
                {
                    return cached;
                }

                _cache.Remove(cacheKey);
            }

            var result = await DecryptCoreAsync(token, cancellationToken);

            // Only definitive 200 answers are cached; a rejected secret or an outage must be
            // re-evaluated on the next request. Revoked sessions (SESSION_INACTIVE) stay usable
            // for at most CacheSeconds - the accepted revocation latency for skipping a network
            // hop on every request.
            if (result.Outcome == HubDecryptOutcome.Valid || result.Outcome == HubDecryptOutcome.Invalid)
            {
                var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.CacheSeconds));
                if (result.Outcome == HubDecryptOutcome.Valid && result.Claims != null && result.Claims.ExpiresAt > 0)
                {
                    var untilExpiry = DateTimeOffset.FromUnixTimeSeconds(result.Claims.ExpiresAt) - DateTimeOffset.UtcNow;
                    if (untilExpiry <= TimeSpan.Zero)
                    {
                        return HubDecryptResult.InvalidToken("EXPIRED");
                    }

                    ttl = untilExpiry < ttl ? untilExpiry : ttl;
                }

                _cache.Set(cacheKey, result, ttl);
            }

            return result;
        }

        private async Task<HubDecryptResult> DecryptCoreAsync(string token, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.PostAsync(
                    DecryptPath,
                    new StringContent(JsonSerializer.Serialize(new { token }, JsonOptions), Encoding.UTF8, "application/json"),
                    cancellationToken);

                switch ((int)response.StatusCode)
                {
                    case 200:
                        break;
                    case 401:
                        _logger.LogError("Hub decrypt API rejected the external-service secret; check XR50Hub:SharedSecret");
                        return HubDecryptResult.SecretRejected();
                    case 400:
                        // We always send a token, so a 400 means the Hub could not use it.
                        _logger.LogDebug("Hub decrypt API returned 400 for a session token");
                        return HubDecryptResult.InvalidToken("MALFORMED");
                    default:
                        _logger.LogWarning("Hub decrypt API returned unexpected status {StatusCode}", (int)response.StatusCode);
                        return HubDecryptResult.Unavailable();
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var decrypted = JsonSerializer.Deserialize<HubDecryptResponse>(body, JsonOptions);
                if (decrypted == null)
                {
                    _logger.LogWarning("Hub decrypt API returned an empty response body");
                    return HubDecryptResult.Unavailable();
                }

                if (!decrypted.Valid || decrypted.Claims == null)
                {
                    _logger.LogDebug("Hub session token rejected: {Reason}", decrypted.Reason ?? "unknown");
                    return HubDecryptResult.InvalidToken(decrypted.Reason);
                }

                _logger.LogDebug("Hub session token decrypted for user {UserId} in tenant {TenantId}",
                    decrypted.Claims.UserId, decrypted.Claims.TenantId);
                return HubDecryptResult.ValidToken(decrypted.Claims);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Hub decrypt API returned an unparseable response");
                return HubDecryptResult.Unavailable();
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException)
                && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Hub decrypt API unreachable: {Error}", ex.Message);
                return HubDecryptResult.Unavailable();
            }
        }

        private static bool IsExpired(HubClaims? claims)
        {
            return claims != null
                && claims.ExpiresAt > 0
                && DateTimeOffset.FromUnixTimeSeconds(claims.ExpiresAt) <= DateTimeOffset.UtcNow;
        }
    }
}
