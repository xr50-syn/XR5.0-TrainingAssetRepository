using Microsoft.EntityFrameworkCore;

namespace XR50TrainingAssetRepo.Data
{
    /// <summary>
    /// The MySQL/MariaDB server version EF Core generates SQL for when it cannot, or must not,
    /// connect to detect it: migration authoring (<c>dotnet ef</c>), the schema migrator and the
    /// hermetic model-drift test. Pinned to the MariaDB major the stack ships
    /// (<c>docker-compose.yaml</c>) and overridable through <c>Database:ServerVersion</c>.
    /// </summary>
    public static class XR50ServerVersion
    {
        public const string ConfigurationKey = "Database:ServerVersion";
        public const string Default = "10.11.0-mariadb";

        public static ServerVersion Resolve(IConfiguration? configuration) =>
            ServerVersion.Parse(configuration?[ConfigurationKey] ?? Default);
    }
}
