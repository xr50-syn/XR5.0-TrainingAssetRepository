using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace XR50TrainingAssetRepo.Data
{
    /// <summary>
    /// Design-time factory for XR50TrainingContext.
    /// Used by EF Core tools (migrations, scaffolding) when a DbContext
    /// has multiple constructors or requires services not available at design time.
    /// </summary>
    public class XR50TrainingContextFactory : IDesignTimeDbContextFactory<XR50TrainingContext>
    {
        public XR50TrainingContext CreateDbContext(string[] args)
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Get connection string from configuration or use a default for design-time
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? Environment.GetEnvironmentVariable("DEFAULT_CONNECTION_STRING")
                ?? "Server=localhost;Database=magical_library;User=root;Password=root;";

            var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
            optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

            // Use the simpler constructor that only requires DbContextOptions
            return new XR50TrainingContext(optionsBuilder.Options);
        }
    }
}
