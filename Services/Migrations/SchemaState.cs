namespace XR50TrainingAssetRepo.Services.Migrations
{
    /// <summary>
    /// What the migrator found in a database before acting on it.
    /// </summary>
    public enum SchemaState
    {
        /// <summary>The database does not exist.</summary>
        Missing,

        /// <summary>No tables of this context's model and no applied migrations: migrate from scratch.</summary>
        Empty,

        /// <summary>The migrations history holds the Baseline and only known migration ids.</summary>
        Managed,

        /// <summary>
        /// Tables created by the hand-written CREATE TABLE script that predates migrations; no
        /// history. Adopted by reconciling the schema and stamping the Baseline.
        /// </summary>
        LegacyRawDdl,

        /// <summary>
        /// Tables created by the old boot-time EF migration or EnsureCreated (EF-convention
        /// shape), recognisable by a foreign history id or longtext columns. Adopted by
        /// dropping them only if every one is empty.
        /// </summary>
        LegacyEfConvention,

        /// <summary>Nothing above matched; the migrator refuses to act.</summary>
        Unknown
    }
}
