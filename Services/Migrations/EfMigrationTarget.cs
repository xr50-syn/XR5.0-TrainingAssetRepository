using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using XR50TrainingAssetRepo.Data;

namespace XR50TrainingAssetRepo.Services.Migrations
{
    public sealed class EfMigrationTargetFactory : IMigrationTargetFactory
    {
        public const string TrainingHistoryTable = "__EFMigrationsHistory";

        private readonly string _baseConnectionString;
        private readonly ServerVersion _serverVersion;

        public EfMigrationTargetFactory(IConfiguration configuration)
        {
            _baseConnectionString = XR50DatabaseSettings.BaseConnectionString(configuration);
            _serverVersion = XR50ServerVersion.Resolve(configuration);
        }

        public IMigrationTarget CreateTraining(string databaseName)
        {
            var options = new DbContextOptionsBuilder<XR50TrainingContext>()
                .UseMySql(TenantConnectionString.ForDatabase(_baseConnectionString, databaseName), _serverVersion)
                .Options;

            // Options-only constructor: no tenant resolution, the database is chosen here.
            return new EfMigrationTarget(new XR50TrainingContext(options), databaseName, TrainingHistoryTable);
        }

        public IMigrationTarget CreateRegistry(string databaseName)
        {
            var options = XR50RegistryContext.BuildOptions(
                TenantConnectionString.ForDatabase(_baseConnectionString, databaseName), _serverVersion);

            return new EfMigrationTarget(new XR50RegistryContext(options), databaseName, XR50RegistryContext.HistoryTable);
        }
    }

    public sealed class EfMigrationTarget : IMigrationTarget
    {
        private readonly DbContext _context;

        public EfMigrationTarget(DbContext context, string databaseName, string historyTable)
        {
            _context = context;
            DatabaseName = databaseName;
            HistoryTable = historyTable;
            KnownMigrationIds = context.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
            BaselineMigrationId = KnownMigrationIds.Count > 0
                ? KnownMigrationIds[0]
                : throw new InvalidOperationException($"{context.GetType().Name} has no migrations");
            ModelTables = context.Model.GetEntityTypes()
                .Select(entityType => entityType.GetTableName())
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public string DatabaseName { get; }

        public string HistoryTable { get; }

        public IReadOnlyList<string> KnownMigrationIds { get; }

        public string BaselineMigrationId { get; }

        public IReadOnlyCollection<string> ModelTables { get; }

        public async Task<IReadOnlyList<string>> GetAppliedAsync(CancellationToken cancellationToken) =>
            (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

        public async Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken) =>
            (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        public Task MigrateAsync(string? targetMigration, CancellationToken cancellationToken) =>
            _context.GetService<IMigrator>().MigrateAsync(targetMigration, cancellationToken);

        public async Task StampAsync(string migrationId, CancellationToken cancellationToken)
        {
            // Use EF's own history repository so the table and row are exactly what MigrateAsync expects.
            var history = _context.GetService<IHistoryRepository>();
            if (!await history.ExistsAsync(cancellationToken))
            {
                await _context.Database.ExecuteSqlRawAsync(history.GetCreateScript(), cancellationToken);
            }

            await _context.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migrationId, ProductInfo.GetVersion())), cancellationToken);
        }

        public async Task ClearHistoryAsync(CancellationToken cancellationToken)
        {
            var history = _context.GetService<IHistoryRepository>();
            if (!await history.ExistsAsync(cancellationToken))
            {
                return;
            }

            foreach (var row in await history.GetAppliedMigrationsAsync(cancellationToken))
            {
                await _context.Database.ExecuteSqlRawAsync(history.GetDeleteScript(row.MigrationId), cancellationToken);
            }
        }

        public ValueTask DisposeAsync() => _context.DisposeAsync();
    }
}
