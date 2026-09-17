namespace XR50TrainingAssetRepo.Infrastructure.Auth
{
    /// <summary>
    /// Settings for the XR5.0 Hub session token integration ("External Service Provider" role
    /// of the Hub spec), bound from the "XR50Hub" configuration section. The shared secret and
    /// the development token are provided by the Hub operator out of band and must only arrive
    /// via environment variables / .env files, never via committed configuration.
    /// </summary>
    public class XR50HubOptions
    {
        public const string SectionName = "XR50Hub";

        /// <summary>Base URL of the Hub platform hosting the session-token decrypt API.</summary>
        public string BaseUrl { get; set; } = "https://platform.xr50.eu";

        /// <summary>Shared secret authenticating this service to the Hub decrypt API.</summary>
        public string SharedSecret { get; set; } = "";

        /// <summary>
        /// Fixed development token accepted without calling the decrypt API. Honored only when
        /// the hosting environment is Development; leave empty everywhere else.
        /// </summary>
        public string? DevelopmentToken { get; set; }

        /// <summary>
        /// Upper bound in seconds for caching decrypt results. Also the accepted revocation
        /// latency: a session revoked on the Hub side stays usable here for at most this long.
        /// </summary>
        public int CacheSeconds { get; set; } = 60;

        /// <summary>Timeout in seconds for the decrypt call; it sits on the request hot path.</summary>
        public int TimeoutSeconds { get; set; } = 5;

        /// <summary>
        /// Create a local user row on the first request of a Hub identity whose tenant is mapped
        /// but who has no matching row yet (keyed by the Hub userId). The Hub token carries no
        /// roles, so users must exist on both sides; provisioning them just in time means an
        /// administrator only has to grant roles, never to transcribe user ids by hand. New rows
        /// are always plain members - roles are only ever granted through our own API.
        /// </summary>
        public bool AutoProvisionUsers { get; set; } = true;
    }

    /// <summary>Names shared between the Hub authentication pieces.</summary>
    public static class HubSessionTokenDefaults
    {
        public const string SchemeName = "XR50Hub";
        public const string HeaderName = "HL-Hub-Session-Token";

        public const string SessionIdClaim = "sessionId";
        public const string ApplicationIdClaim = "applicationId";
        public const string SkillLevelClaim = "skillLevel";

        /// <summary>Raw Hub tenant id (GUID) from the token's tenantId claim, emitted even when
        /// no local tenant is mapped. Used for self-service tenant provisioning.</summary>
        public const string HubTenantIdClaim = "hubTenantId";

        /// <summary>HttpContext.Items key the handler uses to tell the challenge step that the
        /// failure was Hub unavailability (503) rather than a rejected token (401).</summary>
        public const string FailureKindItem = "XR50Hub.FailureKind";
        public const string FailureKindUnavailable = "unavailable";

        private const string BearerPrefix = "Bearer ";

        /// <summary>
        /// Reads a Hub session token from the request. The <see cref="HeaderName"/> header is the
        /// spec's transport and wins when present (even if empty, so the handler can reject it).
        /// Otherwise an <c>Authorization: Bearer</c> value is taken as a Hub session token when it
        /// is not JWT-shaped: clients embedded in the Hub frontend (TPAT) attach the session token
        /// as a standard bearer credential. A JWT (three dot-separated segments) is left to the JWT
        /// bearer scheme, both because that is what it is and because forwarding it to the Hub
        /// decrypt API would hand a foreign credential to a third party. Hub session tokens are
        /// opaque and carry no dots.
        /// </summary>
        public static bool TryGetToken(HttpRequest request, out string token)
        {
            if (request.Headers.TryGetValue(HeaderName, out var headerValues))
            {
                token = headerValues.ToString();
                return true;
            }

            var authorization = request.Headers.Authorization.ToString();
            if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var bearer = authorization[BearerPrefix.Length..].Trim();
                if (bearer.Length > 0 && !IsJwtShaped(bearer))
                {
                    token = bearer;
                    return true;
                }
            }

            token = string.Empty;
            return false;
        }

        private static bool IsJwtShaped(string value) => value.Count(c => c == '.') == 2;
    }
}
