namespace XR50TrainingAssetRepo.Services.Migrations
{
    /// <summary>
    /// One DbContext aimed at one database: the EF Core side of the migrator, behind an
    /// interface so the orchestration can be tested without a server.
    /// </summary>
    public interface IMigrationTarget : IAsyncDisposable
    {
        string DatabaseName { get; }

        string HistoryTable { get; }

        /// <summary>Migration ids in the assembly, oldest first.</summary>
        IReadOnlyList<string> KnownMigrationIds { get; }

        /// <summary>The first migration; legacy databases are adopted by stamping it.</summary>
        string BaselineMigrationId { get; }

        /// <summary>Table names of this context's model.</summary>
        IReadOnlyCollection<string> ModelTables { get; }

        Task<IReadOnlyList<string>> GetAppliedAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken);

        /// <param name="targetMigration">A migration id, or <c>null</c> for the latest.</param>
        Task MigrateAsync(string? targetMigration, CancellationToken cancellationToken);

        /// <summary>Records a migration as applied without executing it (creating the history table if needed).</summary>
        Task StampAsync(string migrationId, CancellationToken cancellationToken);

        Task ClearHistoryAsync(CancellationToken cancellationToken);
    }

    public interface IMigrationTargetFactory
    {
        IMigrationTarget CreateTraining(string databaseName);

        IMigrationTarget CreateRegistry(string databaseName);
    }
}
