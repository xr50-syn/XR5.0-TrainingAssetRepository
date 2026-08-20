namespace XR50TrainingAssetRepo.Services.Migrations
{
    public sealed class XR50SchemaMigrator : IXR50SchemaMigrator
    {
        public const string MigrateOnStartupKey = "Database:MigrateOnStartup";
        public const string TolerateTenantFailuresKey = "Database:TolerateTenantMigrationFailures";
        public const string LockTimeoutKey = "Database:LockTimeoutSeconds";
        private const string TenantSchemaLikePattern = "xr50\\_tenant\\_%";

        // Table name EF convention gave the Group entity before the model followed the deployed
        // schema; only ever present in EF-convention databases.
        private const string EfConventionGroupTable = "Group";

        private readonly ISchemaInspector _inspector;
        private readonly IMigrationTargetFactory _targets;
        private readonly ILegacySchemaReconciler _reconciler;
        private readonly ILogger<XR50SchemaMigrator> _logger;
        private readonly string _baseDatabase;
        private readonly TimeSpan _lockTimeout;

        public XR50SchemaMigrator(
            IConfiguration configuration,
            ISchemaInspector inspector,
            IMigrationTargetFactory targets,
            ILegacySchemaReconciler reconciler,
            ILogger<XR50SchemaMigrator> logger)
        {
            _inspector = inspector;
            _targets = targets;
            _reconciler = reconciler;
            _logger = logger;
            _baseDatabase = XR50DatabaseSettings.BaseDatabaseName(configuration);
            _lockTimeout = TimeSpan.FromSeconds(configuration.GetValue(LockTimeoutKey, 120));
        }

        private enum TargetKind { Registry, Training }

        public async Task<IReadOnlyList<MigrationRunResult>> MigrateCentralAsync(MigrateOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= MigrateOptions.Default;
            var results = new List<MigrationRunResult>
            {
                await RunAsync(TargetKind.Registry, _baseDatabase, null, options, cancellationToken)
            };

            if (results[0].Succeeded)
            {
                results.Add(await RunAsync(TargetKind.Training, _baseDatabase, null, options, cancellationToken));
            }

            return results;
        }

        public Task<MigrationRunResult> MigrateTenantAsync(string tenantName, MigrateOptions? options = null, CancellationToken cancellationToken = default) =>
            RunAsync(TargetKind.Training, XR50TenantDatabase.SchemaFor(tenantName), tenantName, options ?? MigrateOptions.Default, cancellationToken);

        public async Task<MigrationRunReport> MigrateAllAsync(MigrateOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= MigrateOptions.Default;
            var results = new List<MigrationRunResult>(await MigrateCentralAsync(options, cancellationToken));
            if (results.Any(r => !r.Succeeded))
            {
                _logger.LogError("Central database migration failed; tenant databases were not attempted");
                return new MigrationRunReport(results, Array.Empty<string>(), succeeded: false);
            }

            var tenants = await _inspector.ListRegisteredTenantsAsync(cancellationToken);
            var expectedSchemas = new List<string>();
            foreach (var tenant in tenants)
            {
                var database = XR50TenantDatabase.SchemaFor(tenant.TenantName);
                expectedSchemas.Add(database);

                if (!string.Equals(tenant.DatabaseName, database, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Registry row for tenant {TenantName} names database {RegisteredDatabase} but the derived name is {DerivedDatabase}; using the derived name",
                        tenant.TenantName, tenant.DatabaseName, database);
                }

                if (!tenant.IsActive)
                {
                    _logger.LogInformation("Skipping inactive tenant {TenantName}", tenant.TenantName);
                    continue;
                }

                results.Add(await RunAsync(TargetKind.Training, database, tenant.TenantName, options, cancellationToken));
            }

            var orphans = await FindOrphanSchemasAsync(expectedSchemas, cancellationToken);
            foreach (var orphan in orphans)
            {
                _logger.LogWarning("Schema {Schema} looks like a tenant database but no registry row points at it; left untouched", orphan);
            }

            var tenantFailures = results.Skip(2).Count(r => !r.Succeeded);
            var succeeded = tenantFailures == 0 || options.TolerateTenantFailures;
            if (tenantFailures > 0)
            {
                _logger.LogError("{Count} tenant database(s) failed to migrate (tolerated: {Tolerated})", tenantFailures, options.TolerateTenantFailures);
            }

            return new MigrationRunReport(results, orphans, succeeded);
        }

        public async Task<IReadOnlyList<MigrationTargetStatus>> GetStatusAsync(string? tenantName = null, CancellationToken cancellationToken = default)
        {
            if (tenantName is not null)
            {
                return new[] { await StatusAsync(TargetKind.Training, XR50TenantDatabase.SchemaFor(tenantName), tenantName, cancellationToken) };
            }

            var statuses = new List<MigrationTargetStatus>
            {
                await StatusAsync(TargetKind.Registry, _baseDatabase, null, cancellationToken),
                await StatusAsync(TargetKind.Training, _baseDatabase, null, cancellationToken)
            };

            foreach (var tenant in await _inspector.ListRegisteredTenantsAsync(cancellationToken))
            {
                if (tenant.IsActive)
                {
                    statuses.Add(await StatusAsync(TargetKind.Training, XR50TenantDatabase.SchemaFor(tenant.TenantName), tenant.TenantName, cancellationToken));
                }
            }

            return statuses;
        }

        private static string TargetName(TargetKind kind, string database, string? tenantName) =>
            kind == TargetKind.Registry ? $"registry@{database}"
            : tenantName is null ? $"training@{database}"
            : $"tenant:{tenantName}@{database}";

        private async Task<MigrationRunResult> RunAsync(TargetKind kind, string database, string? tenantName, MigrateOptions options, CancellationToken cancellationToken)
        {
            var name = TargetName(kind, database, tenantName);
            var state = SchemaState.Unknown;
            try
            {
                if (!await _inspector.DatabaseExistsAsync(database, cancellationToken))
                {
                    return MigrationRunResult.Failure(name, database, SchemaState.Missing, $"Database {database} does not exist");
                }

                await using var target = kind == TargetKind.Registry ? _targets.CreateRegistry(database) : _targets.CreateTraining(database);
                await using var @lock = await _inspector.AcquireLockAsync(database, _lockTimeout, cancellationToken);

                if (options.TargetMigration is not null &&
                    string.CompareOrdinal(options.TargetMigration, target.BaselineMigrationId) < 0)
                {
                    throw new SchemaMigrationException(
                        $"Target migration {options.TargetMigration} predates the Baseline {target.BaselineMigrationId}; the schema cannot be taken below the Baseline");
                }

                var classification = await ClassifyAsync(kind, target, cancellationToken);
                state = classification.State;
                _logger.LogInformation("Migration target {Target}: state {State}", name, state);

                switch (state)
                {
                    case SchemaState.Empty:
                    case SchemaState.Managed:
                        return new MigrationRunResult(name, database, state, true, false, await ApplyAsync(target, options.TargetMigration, cancellationToken), null);

                    case SchemaState.LegacyRawDdl:
                        RequireAdoption(options, name, state);
                        await AdoptRawDdlAsync(kind, target, classification, cancellationToken);
                        return new MigrationRunResult(name, database, state, true, true, await ApplyAsync(target, options.TargetMigration, cancellationToken), null);

                    case SchemaState.LegacyEfConvention:
                        RequireAdoption(options, name, state);
                        await AdoptEfConventionAsync(kind, target, classification, cancellationToken);
                        return new MigrationRunResult(name, database, state, true, true, await ApplyAsync(target, options.TargetMigration, cancellationToken), null);

                    default:
                        throw new SchemaMigrationException(
                            $"Database {database} is in an unrecognised state ({classification.Detail}); inspect it and migrate by hand",
                            manualInterventionRequired: true);
                }
            }
            catch (SchemaMigrationException ex)
            {
                _logger.LogError(ex, "Migration target {Target} failed", name);
                return MigrationRunResult.Failure(name, database, state, ex.Message, ex.ManualInterventionRequired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Migration target {Target} failed", name);
                return MigrationRunResult.Failure(name, database, state, ex.Message);
            }
        }

        private static void RequireAdoption(MigrateOptions options, string name, SchemaState state)
        {
            if (!options.AdoptLegacy)
            {
                throw new SchemaMigrationException($"{name} is in state {state} and legacy adoption is disabled", manualInterventionRequired: true);
            }
        }

        private async Task<IReadOnlyList<string>> ApplyAsync(IMigrationTarget target, string? targetMigration, CancellationToken cancellationToken)
        {
            var pending = await target.GetPendingAsync(cancellationToken);
            var applying = targetMigration is null
                ? pending
                : pending.Where(id => string.CompareOrdinal(id, targetMigration) <= 0).ToList();

            if (applying.Count > 0 || targetMigration is not null)
            {
                _logger.LogInformation("Applying {Count} migration(s) to {Database}: {Migrations}", applying.Count, target.DatabaseName, string.Join(", ", applying));
                await target.MigrateAsync(targetMigration, cancellationToken);
            }

            return applying;
        }

        private async Task AdoptRawDdlAsync(TargetKind kind, IMigrationTarget target, Classification classification, CancellationToken cancellationToken)
        {
            _logger.LogWarning("Adopting legacy database {Database}: reconciling schema and stamping {Baseline}", target.DatabaseName, target.BaselineMigrationId);

            if (kind == TargetKind.Registry)
            {
                await _reconciler.ReconcileRegistryAsync(target.DatabaseName, cancellationToken);
            }
            else
            {
                await _reconciler.ReconcileTrainingAsync(target.DatabaseName, cancellationToken);
            }

            var tables = (await _inspector.ListTablesAsync(target.DatabaseName, cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var missing = target.ModelTables.Where(t => !tables.Contains(t)).ToList();
            if (missing.Count > 0)
            {
                throw new SchemaMigrationException(
                    $"After reconciling {target.DatabaseName} these model tables are still missing: {string.Join(", ", missing)}",
                    manualInterventionRequired: true);
            }

            // A crash between reconcile and stamp re-enters here; never stamp twice.
            if (classification.History?.Contains(target.BaselineMigrationId) != true)
            {
                await target.StampAsync(target.BaselineMigrationId, cancellationToken);
            }
        }

        private async Task AdoptEfConventionAsync(TargetKind kind, IMigrationTarget target, Classification classification, CancellationToken cancellationToken)
        {
            if (kind == TargetKind.Registry)
            {
                throw new SchemaMigrationException(
                    $"Registry history in {target.DatabaseName} holds unknown migration ids; inspect {target.HistoryTable} by hand",
                    manualInterventionRequired: true);
            }

            var present = classification.Tables
                .Where(t => target.ModelTables.Contains(t) || t == EfConventionGroupTable)
                .ToList();

            var populated = new List<string>();
            foreach (var table in present)
            {
                if (await _inspector.CountRowsAsync(target.DatabaseName, table, cancellationToken) > 0)
                {
                    populated.Add(table);
                }
            }

            if (populated.Count > 0)
            {
                throw new SchemaMigrationException(
                    $"Database {target.DatabaseName} has EF-convention tables with data ({string.Join(", ", populated)}); " +
                    "they cannot be rebuilt automatically. Move the data out or drop the tables by hand, then run the migration again.",
                    manualInterventionRequired: true);
            }

            var foreign = classification.Tables
                .Where(t => !present.Contains(t) && t != target.HistoryTable && !IsOtherContextTable(kind, t))
                .ToList();
            if (foreign.Count > 0)
            {
                _logger.LogWarning("Database {Database} has tables outside the model that are left untouched: {Tables}", target.DatabaseName, string.Join(", ", foreign));
            }

            _logger.LogWarning("Adopting EF-convention database {Database}: dropping {Count} empty tables and rebuilding from the Baseline", target.DatabaseName, present.Count);
            await _inspector.DropTablesAsync(target.DatabaseName, present, cancellationToken);
            await target.ClearHistoryAsync(cancellationToken);
        }

        private static bool IsOtherContextTable(TargetKind kind, string table) =>
            kind == TargetKind.Training && (table == Data.XR50RegistryContext.TableName || table == Data.XR50RegistryContext.HistoryTable);

        private sealed record Classification(SchemaState State, string Detail, IReadOnlyCollection<string> Tables, IReadOnlyList<string>? History);

        private async Task<Classification> ClassifyAsync(TargetKind kind, IMigrationTarget target, CancellationToken cancellationToken)
        {
            var database = target.DatabaseName;
            var tables = (await _inspector.ListTablesAsync(database, cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var history = await _inspector.ReadHistoryAsync(database, target.HistoryTable, cancellationToken);
            var modelPresent = target.ModelTables.Where(tables.Contains).ToList();

            if (history is { Count: > 0 })
            {
                var unknown = history.Where(id => !target.KnownMigrationIds.Contains(id)).ToList();
                if (unknown.Count > 0)
                {
                    var state = kind == TargetKind.Training ? SchemaState.LegacyEfConvention : SchemaState.Unknown;
                    return new Classification(state, $"history holds unknown migration(s) {string.Join(", ", unknown)}", tables, history);
                }

                return history.Contains(target.BaselineMigrationId)
                    ? new Classification(SchemaState.Managed, "history holds the Baseline", tables, history)
                    : new Classification(SchemaState.Unknown, "history has known ids but no Baseline", tables, history);
            }

            if (modelPresent.Count == 0)
            {
                return new Classification(SchemaState.Empty, "no model tables", tables, history);
            }

            if (kind == TargetKind.Registry)
            {
                return new Classification(SchemaState.LegacyRawDdl, "registry table exists without history", tables, history);
            }

            var efConvention = tables.Contains(EfConventionGroupTable)
                || await ColumnIsAsync(database, "Assets", "Description", "longtext", cancellationToken)
                || await ColumnIsAsync(database, "Materials", "Description", "longtext", cancellationToken);
            var rawDdl = tables.Contains("Groups")
                || await ColumnIsAsync(database, "Materials", "Discriminator", "varchar(50)", cancellationToken)
                || await ColumnIsAsync(database, "Assets", "Description", "varchar(1000)", cancellationToken);

            if (rawDdl && !efConvention)
            {
                return new Classification(SchemaState.LegacyRawDdl, "hand-written DDL fingerprint", tables, history);
            }

            if (efConvention && !rawDdl)
            {
                return new Classification(SchemaState.LegacyEfConvention, "EF-convention fingerprint without history", tables, history);
            }

            return new Classification(SchemaState.Unknown, $"{modelPresent.Count} model tables present, fingerprint inconclusive", tables, history);
        }

        private async Task<bool> ColumnIsAsync(string database, string table, string column, string type, CancellationToken cancellationToken) =>
            string.Equals(await _inspector.GetColumnTypeAsync(database, table, column, cancellationToken), type, StringComparison.OrdinalIgnoreCase);

        private async Task<MigrationTargetStatus> StatusAsync(TargetKind kind, string database, string? tenantName, CancellationToken cancellationToken)
        {
            var name = TargetName(kind, database, tenantName);
            try
            {
                if (!await _inspector.DatabaseExistsAsync(database, cancellationToken))
                {
                    return new MigrationTargetStatus(name, database, SchemaState.Missing, Array.Empty<string>(), Array.Empty<string>(), null);
                }

                await using var target = kind == TargetKind.Registry ? _targets.CreateRegistry(database) : _targets.CreateTraining(database);
                var classification = await ClassifyAsync(kind, target, cancellationToken);

                if (classification.State == SchemaState.Managed)
                {
                    return new MigrationTargetStatus(name, database, classification.State,
                        await target.GetAppliedAsync(cancellationToken), await target.GetPendingAsync(cancellationToken), null);
                }

                return new MigrationTargetStatus(name, database, classification.State,
                    classification.History ?? Array.Empty<string>(), target.KnownMigrationIds, classification.Detail);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not read migration status of {Target}", name);
                return new MigrationTargetStatus(name, database, SchemaState.Unknown, Array.Empty<string>(), Array.Empty<string>(), ex.Message);
            }
        }

        private async Task<IReadOnlyList<string>> FindOrphanSchemasAsync(IReadOnlyCollection<string> expected, CancellationToken cancellationToken)
        {
            var schemas = await _inspector.ListSchemasLikeAsync(TenantSchemaLikePattern, cancellationToken);
            var comparer = await _inspector.LowerCaseTableNamesAsync(cancellationToken)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var known = new HashSet<string>(expected, comparer);
            return schemas.Where(s => !known.Contains(s)).ToList();
        }
    }
}
