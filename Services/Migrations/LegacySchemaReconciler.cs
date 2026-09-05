using MySql.Data.MySqlClient;

namespace XR50TrainingAssetRepo.Services.Migrations
{
    /// <summary>
    /// Brings a database provisioned before migrations existed to the exact shape the Baseline
    /// migration describes, so the Baseline can be stamped as applied without executing it.
    /// </summary>
    public interface ILegacySchemaReconciler
    {
        /// <summary>Tenant schema: the frozen CREATE TABLE pass, every legacy in-place migration, then the finishing touches.</summary>
        Task ReconcileTrainingAsync(string databaseName, CancellationToken cancellationToken = default);

        /// <summary>Central registry: add every field introduced after the original table and the Hub tenant index.</summary>
        Task ReconcileRegistryAsync(string databaseName, CancellationToken cancellationToken = default);
    }

    public sealed class LegacySchemaReconciler : ILegacySchemaReconciler
    {
        private readonly string _baseConnectionString;
        private readonly ILogger<LegacySchemaReconciler> _logger;
        private readonly LegacyTenantSchema _legacy;

        public LegacySchemaReconciler(IConfiguration configuration, ILogger<LegacySchemaReconciler> logger)
        {
            _baseConnectionString = XR50DatabaseSettings.BaseConnectionString(configuration);
            _logger = logger;
            _legacy = new LegacyTenantSchema(configuration, logger);
        }

        public async Task ReconcileTrainingAsync(string databaseName, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Reconciling legacy tenant schema in database {Database} before stamping the Baseline", databaseName);

            Require(await _legacy.CreateTablesInDatabaseAsync(databaseName), "CREATE TABLE IF NOT EXISTS pass", databaseName);

            // Same order the routines were introduced in; each is idempotent.
            var steps = new (string Name, Func<string, Task<bool>> Run)[]
            {
                ("AssetType column", _legacy.MigrateAssetTypeColumnAsync),
                ("Asset content hash", _legacy.MigrateAssetContentHashAsync),
                ("Annotations columns", _legacy.MigrateAnnotationsColumnsAsync),
                ("SubcomponentMaterialRelationships table", _legacy.MigrateSubcomponentMaterialRelationshipsTableAsync),
                ("Program assignment ranks", _legacy.MigrateProgramAssignmentRanksAsync),
                ("QuizAnswers table", _legacy.MigrateQuizAnswersTableAsync),
                ("AI assistant columns and sessions", _legacy.MigrateAIAssistantAndAiColumnsAsync),
                ("User material tables", _legacy.MigrateUserMaterialTablesAsync),
                ("Material relationship ranks", _legacy.MigrateMaterialRelationshipRanksAsync),
                ("User material program key", _legacy.MigrateUserMaterialProgramKeyAsync),
                ("Quiz evaluation columns", _legacy.MigrateQuizEvaluationColumnsAsync),
                ("AI assistant collection columns", _legacy.MigrateAIAssistantCollectionColumnsAsync),
                ("AIAssistantMaterialAssetJobs table", _legacy.MigrateAIAssistantMaterialAssetJobsTableAsync),
                ("INNOV chatbot columns", _legacy.MigrateInnovChatbotColumnsAsync),
                ("InnovChatbotMaterialAssetJobs table", _legacy.MigrateInnovChatbotMaterialAssetJobsTableAsync),
            };

            foreach (var (name, run) in steps)
            {
                Require(await run(databaseName), name, databaseName);
            }

            await FinishForBaselineAsync(databaseName, cancellationToken);
        }

        public async Task ReconcileRegistryAsync(string databaseName, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenAsync(databaseName, cancellationToken);

            var columns = new (string Name, string Definition)[]
            {
                ("DefaultAICollection", "varchar(255) NULL"),
                ("InnovChatbotBaseUrl", "varchar(500) NULL"),
                ("InnovChatbotApiToken", "varchar(1000) NULL"),
                ("InnovChatbotDefaultPilot", "varchar(255) NULL"),
                ("HubTenantId", "char(36) NULL")
            };

            foreach (var (name, definition) in columns)
            {
                if (!await ColumnExistsAsync(connection, "XR50TenantRegistry", name, cancellationToken))
                {
                    _logger.LogInformation("Adding {Column} column to XR50TenantRegistry in {Database}", name, databaseName);
                    await ExecuteAsync(connection, $"ALTER TABLE `XR50TenantRegistry` ADD COLUMN `{name}` {definition}", cancellationToken);
                }
            }

            if (!await IndexExistsAsync(connection, "XR50TenantRegistry", "ux_registry_hub_tenant", cancellationToken))
            {
                _logger.LogInformation("Adding unique index ux_registry_hub_tenant to XR50TenantRegistry in {Database}", databaseName);
                await ExecuteAsync(connection, "ALTER TABLE `XR50TenantRegistry` ADD UNIQUE INDEX `ux_registry_hub_tenant` (`HubTenantId`)", cancellationToken);
            }
        }

        /// <summary>
        /// The deltas between the legacy script and the EF model that the legacy routines never
        /// closed. Required child foreign keys become NOT NULL (refusing if any row has none),
        /// columns the model cannot represent as NOT NULL are relaxed, required strings get their
        /// default backfilled. Every step checks INFORMATION_SCHEMA first so re-running is free.
        /// </summary>
        private async Task FinishForBaselineAsync(string databaseName, CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(databaseName, cancellationToken);

            await TightenAsync(connection, databaseName, "ImageAnnotations", "ImageMaterialId", "int(11) NOT NULL", backfill: null, cancellationToken);
            await TightenAsync(connection, databaseName, "QuestionnaireEntries", "QuestionnaireMaterialId", "int(11) NOT NULL", backfill: null, cancellationToken);
            await TightenAsync(connection, databaseName, "QuizQuestions", "QuizMaterialId", "int(11) NOT NULL", backfill: null, cancellationToken);
            await TightenAsync(connection, databaseName, "QuizAnswers", "QuizQuestionId", "int(11) NOT NULL", backfill: null, cancellationToken);
            await TightenAsync(connection, databaseName, "Assets", "AiAvailable", "varchar(20) NOT NULL DEFAULT 'notready'", backfill: "'notready'", cancellationToken);
            await TightenAsync(connection, databaseName, "UserMaterialData", "Data", "json NOT NULL", backfill: "'{}'", cancellationToken);

            // Quiz-only column of the Materials TPH table: EF maps subtype columns as nullable.
            if (await IsNullableAsync(connection, "Materials", "EvaluationMode", cancellationToken) == false)
            {
                _logger.LogInformation("Relaxing Materials.EvaluationMode to NULL DEFAULT 0 in {Database}", databaseName);
                await ExecuteAsync(connection, "ALTER TABLE `Materials` MODIFY `EvaluationMode` tinyint(1) NULL DEFAULT 0", cancellationToken);
            }
        }

        private async Task TightenAsync(MySqlConnection connection, string databaseName, string table, string column, string definition, string? backfill, CancellationToken cancellationToken)
        {
            if (await IsNullableAsync(connection, table, column, cancellationToken) != true)
            {
                return;
            }

            if (backfill is not null)
            {
                var updated = await ExecuteAsync(connection, $"UPDATE `{table}` SET `{column}` = {backfill} WHERE `{column}` IS NULL", cancellationToken);
                if (updated > 0)
                {
                    _logger.LogInformation("Backfilled {Count} NULL {Table}.{Column} values in {Database}", updated, table, column, databaseName);
                }
            }
            else
            {
                await using var count = new MySqlCommand($"SELECT COUNT(*) FROM `{table}` WHERE `{column}` IS NULL", connection);
                var nulls = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));
                if (nulls > 0)
                {
                    throw new SchemaMigrationException(
                        $"{databaseName}.{table}.{column} has {nulls} row(s) with NULL but the schema requires a value; " +
                        "delete or repair those rows, then run the migration again.", manualInterventionRequired: true);
                }
            }

            _logger.LogInformation("Tightening {Table}.{Column} to NOT NULL in {Database}", table, column, databaseName);
            await ExecuteAsync(connection, $"ALTER TABLE `{table}` MODIFY `{column}` {definition}", cancellationToken);
        }

        private static void Require(bool succeeded, string step, string databaseName)
        {
            if (!succeeded)
            {
                throw new SchemaMigrationException($"Legacy reconcile step '{step}' failed for database {databaseName}; see the log");
            }
        }

        private async Task<MySqlConnection> OpenAsync(string databaseName, CancellationToken cancellationToken)
        {
            var connection = new MySqlConnection(TenantConnectionString.ForDatabase(_baseConnectionString, databaseName));
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private static async Task<int> ExecuteAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken)
        {
            await using var command = new MySqlCommand(sql, connection);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <returns><c>true</c>/<c>false</c> for the column's nullability, <c>null</c> when the column does not exist.</returns>
        private static async Task<bool?> IsNullableAsync(MySqlConnection connection, string table, string column, CancellationToken cancellationToken)
        {
            await using var command = new MySqlCommand(
                @"SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column", connection);
            command.Parameters.AddWithValue("@table", table);
            command.Parameters.AddWithValue("@column", column);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? null : string.Equals(Convert.ToString(value), "YES", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<bool> ColumnExistsAsync(MySqlConnection connection, string table, string column, CancellationToken cancellationToken) =>
            await IsNullableAsync(connection, table, column, cancellationToken) is not null;

        private static async Task<bool> IndexExistsAsync(MySqlConnection connection, string table, string index, CancellationToken cancellationToken)
        {
            await using var command = new MySqlCommand(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND INDEX_NAME = @index", connection);
            command.Parameters.AddWithValue("@table", table);
            command.Parameters.AddWithValue("@index", index);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        }
    }
}
