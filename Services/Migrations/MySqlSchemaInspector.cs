using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

namespace XR50TrainingAssetRepo.Services.Migrations
{
    public sealed class MySqlSchemaInspector : ISchemaInspector
    {
        private const int MySqlErrorNoSuchTable = 1146;

        private readonly string _baseConnectionString;
        private readonly ILogger<MySqlSchemaInspector> _logger;

        public MySqlSchemaInspector(IConfiguration configuration, ILogger<MySqlSchemaInspector> logger)
        {
            _baseConnectionString = XR50DatabaseSettings.BaseConnectionString(configuration);
            _logger = logger;
        }

        public async Task<bool> DatabaseExistsAsync(string database, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenBaseAsync(cancellationToken);
            var schemaPredicate = await UsesFoldedIdentifiersAsync(connection, cancellationToken)
                ? "LOWER(SCHEMA_NAME) = LOWER(@db)"
                : "BINARY SCHEMA_NAME = @db";
            await using var command = new MySqlCommand(
                $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE {schemaPredicate}", connection);
            command.Parameters.AddWithValue("@db", database);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        }

        public async Task<IReadOnlyList<string>> ListTablesAsync(string database, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenBaseAsync(cancellationToken);
            var schemaPredicate = await UsesFoldedIdentifiersAsync(connection, cancellationToken)
                ? "LOWER(TABLE_SCHEMA) = LOWER(@db)"
                : "BINARY TABLE_SCHEMA = @db";
            await using var command = new MySqlCommand(
                $@"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
                   WHERE {schemaPredicate} AND TABLE_TYPE = 'BASE TABLE'
                   ORDER BY TABLE_NAME", connection);
            command.Parameters.AddWithValue("@db", database);
            return await ReadStringsAsync(command, cancellationToken);
        }

        public async Task<IReadOnlyList<string>> ListSchemasLikeAsync(string likePattern, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenBaseAsync(cancellationToken);
            await using var command = new MySqlCommand(
                "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME LIKE @pattern ORDER BY SCHEMA_NAME", connection);
            command.Parameters.AddWithValue("@pattern", likePattern);
            return await ReadStringsAsync(command, cancellationToken);
        }

        public async Task<IReadOnlyList<string>?> ReadHistoryAsync(string database, string historyTable, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenBaseAsync(cancellationToken);
            await using var command = new MySqlCommand(
                $"SELECT MigrationId FROM {Quote(database)}.{Quote(historyTable)} ORDER BY MigrationId", connection);
            try
            {
                return await ReadStringsAsync(command, cancellationToken);
            }
            catch (MySqlException ex) when (ex.Number == MySqlErrorNoSuchTable)
            {
                return null;
            }
        }

        public async Task<long> CountRowsAsync(string database, string table, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenBaseAsync(cancellationToken);
            await using var command = new MySqlCommand($"SELECT COUNT(*) FROM {Quote(database)}.{Quote(table)}", connection);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        public async Task<string?> GetColumnTypeAsync(string database, string table, string column, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenBaseAsync(cancellationToken);
            var schemaPredicate = await UsesFoldedIdentifiersAsync(connection, cancellationToken)
                ? "LOWER(TABLE_SCHEMA) = LOWER(@db)"
                : "BINARY TABLE_SCHEMA = @db";
            await using var command = new MySqlCommand(
                $@"SELECT COLUMN_TYPE FROM INFORMATION_SCHEMA.COLUMNS
                   WHERE {schemaPredicate} AND TABLE_NAME = @table AND COLUMN_NAME = @column", connection);
            command.Parameters.AddWithValue("@db", database);
            command.Parameters.AddWithValue("@table", table);
            command.Parameters.AddWithValue("@column", column);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? null : Convert.ToString(value);
        }

        public async Task<bool> LowerCaseTableNamesAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenBaseAsync(cancellationToken);
            return await UsesFoldedIdentifiersAsync(connection, cancellationToken);
        }

        public async Task<IAsyncDisposable> AcquireLockAsync(string database, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            // GET_LOCK names are capped at 64 characters and database names alone may reach that,
            // so lock on a digest. Fold the input when the server folds database identifiers, or
            // two spellings of the same physical schema would receive different locks. The lock
            // lives on this connection and dies with it.
            var connection = await OpenBaseAsync(cancellationToken);
            try
            {
                var lockDatabase = await UsesFoldedIdentifiersAsync(connection, cancellationToken)
                    ? database.ToLowerInvariant()
                    : database;
                var lockName = "xr50mig:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(lockDatabase))).ToLowerInvariant();
                await using var command = new MySqlCommand("SELECT GET_LOCK(@name, @timeout)", connection);
                command.Parameters.AddWithValue("@name", lockName);
                command.Parameters.AddWithValue("@timeout", (int)timeout.TotalSeconds);
                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (result is null or DBNull || Convert.ToInt32(result) != 1)
                {
                    throw new SchemaMigrationException(
                        $"Another migration is already running against database {database} (lock not acquired within {timeout.TotalSeconds:0}s)");
                }

                return new AdvisoryLock(connection, lockName, database, _logger);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        public async Task DropTablesAsync(string database, IReadOnlyCollection<string> tables, CancellationToken cancellationToken = default)
        {
            if (tables.Count == 0)
            {
                return;
            }

            await using var connection = await OpenAsync(database, cancellationToken);
            await Execute(connection, "SET FOREIGN_KEY_CHECKS = 0", cancellationToken);
            try
            {
                foreach (var table in tables)
                {
                    _logger.LogWarning("Dropping table {Table} in database {Database}", table, database);
                    await Execute(connection, $"DROP TABLE IF EXISTS {Quote(table)}", cancellationToken);
                }
            }
            finally
            {
                await Execute(connection, "SET FOREIGN_KEY_CHECKS = 1", cancellationToken);
            }
        }

        public async Task<IReadOnlyList<RegisteredTenant>> ListRegisteredTenantsAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenBaseAsync(cancellationToken);
            await using var command = new MySqlCommand(
                $"SELECT TenantName, DatabaseName, IsActive FROM {Quote(Data.XR50RegistryContext.TableName)} ORDER BY TenantName", connection);
            var tenants = new List<RegisteredTenant>();
            try
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    tenants.Add(new RegisteredTenant(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
                }
            }
            catch (MySqlException ex) when (ex.Number == MySqlErrorNoSuchTable)
            {
                return Array.Empty<RegisteredTenant>();
            }

            return tenants;
        }

        private Task<MySqlConnection> OpenBaseAsync(CancellationToken cancellationToken) =>
            OpenAsync(null, cancellationToken);

        private async Task<MySqlConnection> OpenAsync(string? database, CancellationToken cancellationToken)
        {
            var connectionString = database is null
                ? _baseConnectionString
                : TenantConnectionString.ForDatabase(_baseConnectionString, database);
            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private static async Task Execute(MySqlConnection connection, string sql, CancellationToken cancellationToken)
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<IReadOnlyList<string>> ReadStringsAsync(MySqlCommand command, CancellationToken cancellationToken)
        {
            var values = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        private static async Task<bool> UsesFoldedIdentifiersAsync(MySqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new MySqlCommand("SELECT @@lower_case_table_names", connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
        }

        private static string Quote(string identifier) => "`" + identifier.Replace("`", "``") + "`";

        private sealed class AdvisoryLock : IAsyncDisposable
        {
            private readonly MySqlConnection _connection;
            private readonly string _lockName;
            private readonly string _database;
            private readonly ILogger _logger;

            public AdvisoryLock(MySqlConnection connection, string lockName, string database, ILogger logger)
            {
                _connection = connection;
                _lockName = lockName;
                _database = database;
                _logger = logger;
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await using var command = new MySqlCommand("SELECT RELEASE_LOCK(@name)", _connection);
                    command.Parameters.AddWithValue("@name", _lockName);
                    await command.ExecuteScalarAsync();
                }
                catch (Exception ex)
                {
                    // Closing the connection releases the lock anyway.
                    _logger.LogDebug(ex, "RELEASE_LOCK failed for database {Database}; the connection is being closed", _database);
                }
                finally
                {
                    await _connection.DisposeAsync();
                }
            }
        }
    }
}
