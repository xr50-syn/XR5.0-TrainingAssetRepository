using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace XR50TrainingAssetRepo.Data
{
    /// <summary>
    /// Design-time factories used by the EF Core tools (<c>dotnet ef migrations add ...</c>).
    /// They never connect: the server version is pinned through <see cref="XR50ServerVersion"/>
    /// so migrations can be authored on a machine without a database.
    /// </summary>
    public class XR50TrainingContextFactory : IDesignTimeDbContextFactory<XR50TrainingContext>
    {
        public XR50TrainingContext CreateDbContext(string[] args)
        {
            var configuration = DesignTimeConfiguration.Build();
            var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
            optionsBuilder.UseMySql(
                DesignTimeConfiguration.ConnectionString(configuration),
                XR50ServerVersion.Resolve(configuration));

            // Use the options-only constructor: no tenant resolution at design time.
            return new XR50TrainingContext(optionsBuilder.Options);
        }
    }

    public class XR50RegistryContextFactory : IDesignTimeDbContextFactory<XR50RegistryContext>
    {
        public XR50RegistryContext CreateDbContext(string[] args)
        {
            var configuration = DesignTimeConfiguration.Build();
            return new XR50RegistryContext(XR50RegistryContext.BuildOptions(
                DesignTimeConfiguration.ConnectionString(configuration),
                XR50ServerVersion.Resolve(configuration)));
        }
    }

    internal static class DesignTimeConfiguration
    {
        public static IConfiguration Build() =>
            new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

        // The tools only need a syntactically valid connection string to build a model; the
        // placeholder is never dialed because the server version is pinned, not detected.
        public static string ConnectionString(IConfiguration configuration) =>
            configuration.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("DEFAULT_CONNECTION_STRING")
            ?? "Server=localhost;Database=magical_library;User=root;Password=root;";
    }
}
