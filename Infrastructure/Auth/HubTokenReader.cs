using System.Text;
using System.Text.Json;

namespace XR50TrainingAssetRepo.Infrastructure.Auth
{
    /// <summary>The two Hub credentials this service accepts.</summary>
    public enum HubTokenKind
    {
        /// <summary>Opaque session token from the External Service Integration spec, validated
        /// through the Hub decrypt API.</summary>
        SessionToken,

        /// <summary>The user's Hub login JWT (POST /api/v1/auth/authenticate), attached by Hub
        /// frontend screens such as TPAT, validated through GET /api/v1/user/limited-info.</summary>
        UserToken,
    }

    /// <summary>
    /// Decides whether a request carries a Hub credential, and which one. Shared by the scheme
    /// selector and the Hub authentication handler so the two can never disagree.
    ///
    /// - HL-Hub-Session-Token header: a session token. It wins when present, even if empty, so
    ///   the handler can reject it.
    /// - Authorization: Bearer with a non-JWT value: a session token sent as a standard bearer.
    /// - Authorization: Bearer with a JWT: the user's Hub login token. The one exception is
    ///   Development, where a JWT issued by the configured Keycloak realm (IAM:Issuer) belongs to
    ///   the JWT bearer scheme and is never sent to the Hub. Outside Development there is no JWT
    ///   bearer scheme: the Hub is the only identity provider.
    /// </summary>
    public sealed class HubTokenReader
    {
        private const string BearerPrefix = "Bearer ";

        private readonly string? _developmentJwtIssuer;

        public HubTokenReader(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _developmentJwtIssuer = environment.IsDevelopment() ? configuration["IAM:Issuer"] : null;
        }

        public bool TryRead(HttpRequest request, out string token, out HubTokenKind kind)
        {
            if (request.Headers.TryGetValue(HubSessionTokenDefaults.HeaderName, out var headerValues))
            {
                token = headerValues.ToString();
                kind = HubTokenKind.SessionToken;
                return true;
            }

            token = string.Empty;
            kind = HubTokenKind.SessionToken;

            var authorization = request.Headers.Authorization.ToString();
            if (!authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var bearer = authorization[BearerPrefix.Length..].Trim();
            if (bearer.Length == 0)
            {
                return false;
            }

            if (!IsJwtShaped(bearer))
            {
                // Hub session tokens are opaque and carry no dots.
                token = bearer;
                kind = HubTokenKind.SessionToken;
                return true;
            }

            if (!string.IsNullOrEmpty(_developmentJwtIssuer)
                && string.Equals(ReadUnverifiedIssuer(bearer), _developmentJwtIssuer, StringComparison.Ordinal))
            {
                return false;
            }

            token = bearer;
            kind = HubTokenKind.UserToken;
            return true;
        }

        private static bool IsJwtShaped(string value) => value.Count(c => c == '.') == 2;

        /// <summary>
        /// Reads the <c>iss</c> claim without validating anything. Used only to route a request to
        /// the right scheme; the chosen scheme does the actual validation.
        /// </summary>
        private static string? ReadUnverifiedIssuer(string jwt)
        {
            try
            {
                using var payload = JsonDocument.Parse(DecodeSegment(jwt.Split('.')[1]));
                return payload.RootElement.TryGetProperty("iss", out var iss) && iss.ValueKind == JsonValueKind.String
                    ? iss.GetString()
                    : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Decodes a base64url JWT segment.</summary>
        internal static byte[] DecodeSegment(string segment)
        {
            var base64 = segment.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            return Convert.FromBase64String(base64);
        }
    }
}
