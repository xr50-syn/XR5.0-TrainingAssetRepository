namespace XR50TrainingAssetRepo.Services.Migrations
{
    public sealed class MigrateOptions
    {
        public static readonly MigrateOptions Default = new();

        /// <summary>Adopt databases in a legacy state instead of refusing them.</summary>
        public bool AdoptLegacy { get; init; } = true;

        /// <summary>
        /// In <see cref="IXR50SchemaMigrator.MigrateAllAsync"/>: a failing tenant does not make the
        /// whole run fail. Central failures always do.
        /// </summary>
        public bool TolerateTenantFailures { get; init; }

        /// <summary>Migrate to this migration id instead of the latest (single target only).</summary>
        public string? TargetMigration { get; init; }
    }

    public sealed record MigrationTargetStatus(
        string Target,
        string DatabaseName,
        SchemaState State,
        IReadOnlyList<string> Applied,
        IReadOnlyList<string> Pending,
        string? Error);

    public sealed record MigrationRunResult(
        string Target,
        string DatabaseName,
        SchemaState StateBefore,
        bool Succeeded,
        bool Adopted,
        IReadOnlyList<string> AppliedNow,
        string? Error,
        bool ManualInterventionRequired = false)
    {
        public static MigrationRunResult Failure(string target, string databaseName, SchemaState state, string error, bool manual = false) =>
            new(target, databaseName, state, false, false, Array.Empty<string>(), error, manual);
    }

    public sealed class MigrationRunReport
    {
        public MigrationRunReport(IReadOnlyList<MigrationRunResult> results, IReadOnlyList<string> orphanSchemas, bool succeeded)
        {
            Results = results;
            OrphanSchemas = orphanSchemas;
            Succeeded = succeeded;
        }

        public IReadOnlyList<MigrationRunResult> Results { get; }

        /// <summary>Schemas named like tenant databases that no registry row points at. Reported, never migrated.</summary>
        public IReadOnlyList<string> OrphanSchemas { get; }

        public bool Succeeded { get; }

        public bool ManualInterventionRequired => Results.Any(r => r.ManualInterventionRequired);
    }

    /// <summary>
    /// A migration step refused to continue. <see cref="ManualInterventionRequired"/> marks the
    /// cases an operator must resolve by hand (unknown schema, data where none was expected).
    /// </summary>
    public sealed class SchemaMigrationException : Exception
    {
        public SchemaMigrationException(string message, bool manualInterventionRequired = false, Exception? inner = null)
            : base(message, inner)
        {
            ManualInterventionRequired = manualInterventionRequired;
        }

        public bool ManualInterventionRequired { get; }
    }
}
