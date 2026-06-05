namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// Builds tenant-scoped MySQL connection strings by rewriting the target database on a base
    /// connection string.
    ///
    /// This replaces the previous approach of doing a literal <c>connectionString.Replace("database=X", ...)</c>,
    /// which was fragile: the configured connection string uses a lowercase <c>database=</c> key, so the
    /// case-sensitive <c>Replace("Database=...")</c> call sites silently no-opped and the code kept talking to
    /// the admin/base database instead of the tenant database - a tenant-isolation hazard. Using
    /// <see cref="MySql.Data.MySqlClient.MySqlConnectionStringBuilder"/> the database is *set* (keyword/casing
    /// agnostic) rather than string-matched, so the wrong-database fallback cannot happen silently.
    /// </summary>
    public static class TenantConnectionString
    {
        /// <summary>
        /// Returns <paramref name="baseConnectionString"/> retargeted at <paramref name="targetDatabase"/>.
        /// Throws if either argument is missing so misconfiguration fails loudly instead of degrading to the base DB.
        /// </summary>
        public static string ForDatabase(string baseConnectionString, string targetDatabase)
        {
            if (string.IsNullOrEmpty(baseConnectionString))
                throw new ArgumentException("Base connection string is not configured.", nameof(baseConnectionString));
            if (string.IsNullOrWhiteSpace(targetDatabase))
                throw new ArgumentException("Target database name is required.", nameof(targetDatabase));

            var builder = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(baseConnectionString)
            {
                Database = targetDatabase
            };
            return builder.ConnectionString;
        }
    }
}
