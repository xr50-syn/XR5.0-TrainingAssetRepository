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
    }
}
