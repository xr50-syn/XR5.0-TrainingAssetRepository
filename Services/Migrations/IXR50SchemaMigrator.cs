namespace XR50TrainingAssetRepo.Services.Migrations
{
    /// <summary>
    /// Applies the committed EF Core migrations to the central database (registry and training
    /// schema) and to every tenant database, adopting databases provisioned before migrations
    /// existed. The single code path behind startup, the <c>migrate</c> CLI verb, tenant
    /// creation and the troubleshooting endpoints.
    /// </summary>
    public interface IXR50SchemaMigrator
    {
        /// <summary>Registry then training schema on the base database, in that order.</summary>
        Task<IReadOnlyList<MigrationRunResult>> MigrateCentralAsync(MigrateOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// One tenant database, named through <c>XR50TenantDatabase.SchemaFor</c>. The tenant
        /// does not have to be registered yet (tenant creation migrates before it registers),
        /// but the database must exist.
        /// </summary>
        Task<MigrationRunResult> MigrateTenantAsync(string tenantName, MigrateOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>Central first, then every active registered tenant; orphan schemas are reported.</summary>
        Task<MigrationRunReport> MigrateAllAsync(MigrateOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>Read-only: state and applied/pending ids for one tenant, or for central plus every tenant.</summary>
        Task<IReadOnlyList<MigrationTargetStatus>> GetStatusAsync(string? tenantName = null, CancellationToken cancellationToken = default);
    }
}
