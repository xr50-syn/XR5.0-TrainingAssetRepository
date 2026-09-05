using MySql.Data.MySqlClient;

namespace XR50TrainingAssetRepo.Services
{
    /// <summary>
    /// The one place that answers "which server and which base database is this deployment on".
    /// </summary>
    public static class XR50DatabaseSettings
    {
        public const string ConnectionStringName = "DefaultConnection";
        public const string DefaultBaseDatabaseName = "magical_library";

        public static string BaseConnectionString(IConfiguration configuration) =>
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured");

        /// <summary>
        /// The base database name: the <c>Database</c> of the connection string, which is what the
        /// application actually connects to, falling back to <c>BaseDatabaseName</c>.
        /// </summary>
        public static string BaseDatabaseName(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (!string.IsNullOrEmpty(connectionString))
            {
                var database = new MySqlConnectionStringBuilder(connectionString).Database;
                if (!string.IsNullOrEmpty(database))
                {
                    return database;
                }
            }

            return configuration["BaseDatabaseName"] ?? DefaultBaseDatabaseName;
        }
    }
}
