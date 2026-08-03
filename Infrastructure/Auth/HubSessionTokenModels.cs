namespace XR50TrainingAssetRepo.Infrastructure.Auth
{
    /// <summary>Wire shape of the Hub decrypt API response (camelCase JSON).</summary>
    public class HubDecryptResponse
    {
        public bool Valid { get; set; }
        public string? Reason { get; set; }
        public HubClaims? Claims { get; set; }
    }

    public class HubClaims
    {
        public int Version { get; set; }
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public Guid SessionId { get; set; }
        public Guid ApplicationId { get; set; }
        public HubUser User { get; set; } = new();
        public long IssuedAt { get; set; }
        public long ExpiresAt { get; set; }
    }

    public class HubUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? SkillLevel { get; set; }
    }

    public enum HubDecryptOutcome
    {
        /// <summary>Token decrypted to valid: true; Claims is populated.</summary>
        Valid,

        /// <summary>Token decrypted to valid: false (MALFORMED | EXPIRED | SESSION_INACTIVE).</summary>
        Invalid,

        /// <summary>The Hub rejected our shared secret (HTTP 401) - our misconfiguration, fail closed.</summary>
        SecretRejected,

        /// <summary>The Hub could not be reached or returned a server error; surfaced as 503.</summary>
        Unavailable,
    }

    public class HubDecryptResult
    {
        public HubDecryptOutcome Outcome { get; init; }
        public string? Reason { get; init; }
        public HubClaims? Claims { get; init; }

        public static HubDecryptResult ValidToken(HubClaims claims) =>
            new() { Outcome = HubDecryptOutcome.Valid, Claims = claims };

        public static HubDecryptResult InvalidToken(string? reason) =>
            new() { Outcome = HubDecryptOutcome.Invalid, Reason = reason };

        public static HubDecryptResult SecretRejected() =>
            new() { Outcome = HubDecryptOutcome.SecretRejected };

        public static HubDecryptResult Unavailable() =>
            new() { Outcome = HubDecryptOutcome.Unavailable };
    }

    /// <summary>
    /// What the local registry and tenant database know about a Hub identity: the tenant the
    /// Hub tenantId maps to, the matched local user, and the roles derived from our own tables.
    /// All fields are null/false when nothing matches - authentication still succeeds, tenant
    /// and role policies then fail closed.
    /// </summary>
    public record HubLocalIdentity(
        string? TenantName,
        string? LocalUserName,
        bool IsTenantAdmin,
        bool IsSystemAdmin)
    {
        public static readonly HubLocalIdentity Unmapped = new(null, null, false, false);
    }
}
