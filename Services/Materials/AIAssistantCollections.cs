namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Naming of the DataLens collection an AI Assistant material owns when no explicit
    /// collectionName is supplied.
    ///
    /// DataLens is reached with one connection for every tenant, and material ids restart at 1 in
    /// each tenant database, so the old per-material name "aiassist_{id}" gave material 10 of every
    /// tenant the same collection: one tenant's assistant answered from another's documents, and
    /// deleting one tenant's asset could drop another tenant's collection. The owned name therefore
    /// carries the tenant.
    ///
    /// Shape: aiassist_{id}_{tenantKey}. The id comes first on purpose: it is all digits, so the
    /// name cannot equal a tenant default collection ("aiassist_default_{tenant}", whose second
    /// segment is not numeric) or a legacy "aiassist_{id}" (no tenant segment), and the id/tenant
    /// split is unambiguous. The tenant key is the sanitized, lowercased tenant name; tenant
    /// creation already refuses names that fold to the same key.
    /// </summary>
    public static class AIAssistantCollections
    {
        public static string OwnCollectionFor(string tenantName, int materialId) =>
            $"aiassist_{materialId}_{XR50TenantDatabase.Sanitize(tenantName).ToLowerInvariant()}";

        /// <summary>
        /// True when <paramref name="collectionName"/> is the collection the repository generated
        /// for this material in this tenant. Only such a collection is the material's to delete: a
        /// legacy "aiassist_{id}" binding may be shared with a same-numbered material of another
        /// tenant, and an explicitly supplied name may be shared on purpose.
        /// </summary>
        public static bool IsOwnCollection(string? collectionName, string tenantName, int materialId) =>
            string.Equals(collectionName, OwnCollectionFor(tenantName, materialId), StringComparison.Ordinal);
    }
}
