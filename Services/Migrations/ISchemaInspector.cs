namespace XR50TrainingAssetRepo.Services.Migrations
{
    public sealed record RegisteredTenant(string TenantName, string DatabaseName, bool IsActive);

    /// <summary>
    /// Everything the migrator needs to know about the server that EF Core does not tell it:
    /// which databases and tables exist, what shape a column has, the contents of a history
    /// table, and a server-side advisory lock. All lookups compare schema names exactly
    /// (<c>BINARY</c>), because tenant database names keep their case.
    /// </summary>
    public interface ISchemaInspector
    {
        Task<bool> DatabaseExistsAsync(string database, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> ListTablesAsync(string database, CancellationToken cancellationToken = default);

        /// <param name="likePattern">A SQL LIKE pattern; escape literal underscores with a backslash.</param>
        Task<IReadOnlyList<string>> ListSchemasLikeAsync(string likePattern, CancellationToken cancellationToken = default);

        /// <returns>The migration ids in the history table, or <c>null</c> when the table does not exist.</returns>
        Task<IReadOnlyList<string>?> ReadHistoryAsync(string database, string historyTable, CancellationToken cancellationToken = default);

        Task<long> CountRowsAsync(string database, string table, CancellationToken cancellationToken = default);

        /// <returns>The <c>COLUMN_TYPE</c> (e.g. <c>varchar(50)</c>, <c>longtext</c>), or <c>null</c> if absent.</returns>
        Task<string?> GetColumnTypeAsync(string database, string table, string column, CancellationToken cancellationToken = default);

        Task<bool> LowerCaseTableNamesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Takes a server-wide advisory lock for the database; released when the returned handle
        /// is disposed. Throws <see cref="SchemaMigrationException"/> when another migrator holds it.
        /// </summary>
        Task<IAsyncDisposable> AcquireLockAsync(string database, TimeSpan timeout, CancellationToken cancellationToken = default);

        Task DropTablesAsync(string database, IReadOnlyCollection<string> tables, CancellationToken cancellationToken = default);

        /// <returns>Rows of the central registry; empty when the registry table does not exist yet.</returns>
        Task<IReadOnlyList<RegisteredTenant>> ListRegisteredTenantsAsync(CancellationToken cancellationToken = default);
    }
}
