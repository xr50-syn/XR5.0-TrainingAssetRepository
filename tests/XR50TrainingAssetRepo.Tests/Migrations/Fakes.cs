using XR50TrainingAssetRepo.Services.Migrations;

namespace XR50TrainingAssetRepo.Tests.Migrations
{
    /// <summary>In-memory picture of a server: databases, their tables, history rows, column types and row counts.</summary>
    internal sealed class FakeSchemaInspector : ISchemaInspector
    {
        public Dictionary<string, FakeDatabase> Databases { get; } = new(StringComparer.Ordinal);
        public List<RegisteredTenant> Registry { get; } = new();
        public bool LowerCaseTableNames { get; set; }
        public List<(string Database, string Table)> Dropped { get; } = new();
        public List<string> Locked { get; } = new();
        public List<string> Released { get; } = new();
        public List<string> Log { get; } = new();

        public FakeDatabase Add(string name)
        {
            var db = new FakeDatabase();
            Databases[name] = db;
            return db;
        }

        public Task<bool> DatabaseExistsAsync(string database, CancellationToken ct = default) =>
            Task.FromResult(Databases.ContainsKey(database));

        public Task<IReadOnlyList<string>> ListTablesAsync(string database, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Databases[database].Tables.OrderBy(t => t, StringComparer.Ordinal).ToList());

        public Task<IReadOnlyList<string>> ListSchemasLikeAsync(string likePattern, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Databases.Keys.Where(k => k.StartsWith("xr50_tenant_", StringComparison.Ordinal)).ToList());

        public Task<IReadOnlyList<string>?> ReadHistoryAsync(string database, string historyTable, CancellationToken ct = default)
        {
            var db = Databases[database];
            return Task.FromResult(db.Tables.Contains(historyTable)
                ? (IReadOnlyList<string>?)db.Histories.GetValueOrDefault(historyTable, new List<string>()).ToList()
                : null);
        }

        public Task<long> CountRowsAsync(string database, string table, CancellationToken ct = default) =>
            Task.FromResult(Databases[database].RowCounts.GetValueOrDefault(table));

        public Task<string?> GetColumnTypeAsync(string database, string table, string column, CancellationToken ct = default) =>
            Task.FromResult(Databases[database].ColumnTypes.GetValueOrDefault((table, column)));

        public Task<bool> LowerCaseTableNamesAsync(CancellationToken ct = default) => Task.FromResult(LowerCaseTableNames);

        public Task<IAsyncDisposable> AcquireLockAsync(string database, TimeSpan timeout, CancellationToken ct = default)
        {
            Locked.Add(database);
            Log.Add($"lock:{database}");
            return Task.FromResult<IAsyncDisposable>(new Handle(() => { Released.Add(database); Log.Add($"unlock:{database}"); }));
        }

        public Task DropTablesAsync(string database, IReadOnlyCollection<string> tables, CancellationToken ct = default)
        {
            foreach (var table in tables)
            {
                Dropped.Add((database, table));
                Databases[database].Tables.Remove(table);
            }

            Log.Add($"drop:{database}:{string.Join("+", tables)}");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RegisteredTenant>> ListRegisteredTenantsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RegisteredTenant>>(Registry.ToList());

        private sealed class Handle : IAsyncDisposable
        {
            private readonly Action _onDispose;
            public Handle(Action onDispose) => _onDispose = onDispose;
            public ValueTask DisposeAsync() { _onDispose(); return ValueTask.CompletedTask; }
        }
    }

    internal sealed class FakeDatabase
    {
        public HashSet<string> Tables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Histories { get; } = new(StringComparer.Ordinal);
        public Dictionary<(string Table, string Column), string> ColumnTypes { get; } = new();
        public Dictionary<string, long> RowCounts { get; } = new(StringComparer.Ordinal);

        public FakeDatabase WithTables(params string[] tables) { foreach (var t in tables) Tables.Add(t); return this; }
        public FakeDatabase WithHistory(string historyTable, params string[] ids) { Tables.Add(historyTable); Histories[historyTable] = ids.ToList(); return this; }
        public FakeDatabase WithColumn(string table, string column, string type) { ColumnTypes[(table, column)] = type; return this; }
        public FakeDatabase WithRows(string table, long count) { RowCounts[table] = count; return this; }
    }

    internal sealed class FakeTargetFactory : IMigrationTargetFactory
    {
        public const string TrainingBaseline = "20260820000000_Baseline";
        public const string TrainingSecond = "20260901000000_AddThing";
        public const string RegistryBaseline = "20260820000001_RegistryBaseline";

        public static readonly string[] TrainingModelTables = { "Users", "Groups", "Materials", "Assets", "TrainingPrograms" };
        public static readonly string[] RegistryModelTables = { "XR50TenantRegistry" };

        public FakeSchemaInspector Inspector { get; }
        public List<FakeTarget> Created { get; } = new();
        public Func<string, Exception?> MigrateFailure { get; set; } = _ => null;

        public FakeTargetFactory(FakeSchemaInspector inspector) => Inspector = inspector;

        public IMigrationTarget CreateTraining(string databaseName) =>
            Track(new FakeTarget(this, databaseName, "__EFMigrationsHistory", new[] { TrainingBaseline, TrainingSecond }, TrainingModelTables));

        public IMigrationTarget CreateRegistry(string databaseName) =>
            Track(new FakeTarget(this, databaseName, "__EFMigrationsHistory_Registry", new[] { RegistryBaseline }, RegistryModelTables));

        private FakeTarget Track(FakeTarget target) { Created.Add(target); return target; }

        public FakeTarget? Target(string databaseName, string historyTable) =>
            Created.LastOrDefault(t => t.DatabaseName == databaseName && t.HistoryTable == historyTable);
    }

    internal sealed class FakeTarget : IMigrationTarget
    {
        private readonly FakeTargetFactory _factory;

        public FakeTarget(FakeTargetFactory factory, string databaseName, string historyTable, IReadOnlyList<string> known, IReadOnlyCollection<string> modelTables)
        {
            _factory = factory;
            DatabaseName = databaseName;
            HistoryTable = historyTable;
            KnownMigrationIds = known;
            ModelTables = modelTables;
        }

        public string DatabaseName { get; }
        public string HistoryTable { get; }
        public IReadOnlyList<string> KnownMigrationIds { get; }
        public string BaselineMigrationId => KnownMigrationIds[0];
        public IReadOnlyCollection<string> ModelTables { get; }

        public List<string> Stamped { get; } = new();
        public List<string?> Migrated { get; } = new();
        public int HistoryCleared { get; private set; }
        public bool Disposed { get; private set; }

        private List<string> History
        {
            get
            {
                var db = _factory.Inspector.Databases[DatabaseName];
                if (!db.Histories.TryGetValue(HistoryTable, out var rows))
                {
                    rows = new List<string>();
                    db.Histories[HistoryTable] = rows;
                }

                return rows;
            }
        }

        public Task<IReadOnlyList<string>> GetAppliedAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(History.ToList());

        public Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(KnownMigrationIds.Except(History).ToList());

        public Task MigrateAsync(string? targetMigration, CancellationToken ct)
        {
            var failure = _factory.MigrateFailure(DatabaseName);
            if (failure is not null)
            {
                throw failure;
            }

            Migrated.Add(targetMigration);
            _factory.Inspector.Log.Add($"migrate:{DatabaseName}:{HistoryTable}");
            var db = _factory.Inspector.Databases[DatabaseName];
            db.Tables.Add(HistoryTable);
            foreach (var id in KnownMigrationIds.Where(id => targetMigration is null || string.CompareOrdinal(id, targetMigration) <= 0))
            {
                if (!History.Contains(id)) History.Add(id);
            }

            foreach (var table in ModelTables) db.Tables.Add(table);
            return Task.CompletedTask;
        }

        public Task StampAsync(string migrationId, CancellationToken ct)
        {
            Stamped.Add(migrationId);
            _factory.Inspector.Log.Add($"stamp:{DatabaseName}:{migrationId}");
            _factory.Inspector.Databases[DatabaseName].Tables.Add(HistoryTable);
            History.Add(migrationId);
            return Task.CompletedTask;
        }

        public Task ClearHistoryAsync(CancellationToken ct)
        {
            HistoryCleared++;
            _factory.Inspector.Log.Add($"clearhistory:{DatabaseName}");
            History.Clear();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    internal sealed class FakeReconciler : ILegacySchemaReconciler
    {
        private readonly FakeSchemaInspector _inspector;
        public List<string> TrainingCalls { get; } = new();
        public List<string> RegistryCalls { get; } = new();
        public Exception? Failure { get; set; }

        public FakeReconciler(FakeSchemaInspector inspector) => _inspector = inspector;

        public Task ReconcileTrainingAsync(string databaseName, CancellationToken ct = default)
        {
            TrainingCalls.Add(databaseName);
            _inspector.Log.Add($"reconcile:{databaseName}");
            if (Failure is not null) throw Failure;
            // The real reconciler's CREATE TABLE pass leaves every model table in place.
            foreach (var table in FakeTargetFactory.TrainingModelTables) _inspector.Databases[databaseName].Tables.Add(table);
            return Task.CompletedTask;
        }

        public Task ReconcileRegistryAsync(string databaseName, CancellationToken ct = default)
        {
            RegistryCalls.Add(databaseName);
            _inspector.Log.Add($"reconcile-registry:{databaseName}");
            if (Failure is not null) throw Failure;
            return Task.CompletedTask;
        }
    }
}
