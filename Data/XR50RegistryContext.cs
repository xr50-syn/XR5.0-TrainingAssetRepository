using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Data
{
    /// <summary>
    /// Owns the schema of the central tenant registry in the base database. It has its own
    /// migrations (<c>Migrations/Registry</c>) and its own history table, because the base
    /// database also carries the full tenant schema for the "default" tenant through
    /// <see cref="XR50TrainingContext"/> and the two migration streams must not share a history.
    /// </summary>
    public sealed class XR50RegistryContext : DbContext
    {
        public const string TableName = "XR50TenantRegistry";
        public const string HistoryTable = "__EFMigrationsHistory_Registry";

        public XR50RegistryContext(DbContextOptions<XR50RegistryContext> options) : base(options)
        {
        }

        public DbSet<XR50TenantRegistryEntry> Tenants => Set<XR50TenantRegistryEntry>();

        public static DbContextOptions<XR50RegistryContext> BuildOptions(string connectionString, ServerVersion serverVersion) =>
            new DbContextOptionsBuilder<XR50RegistryContext>()
                .UseMySql(connectionString, serverVersion, mysql => mysql.MigrationsHistoryTable(HistoryTable))
                .Options;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Column shapes are those of the table as created by the pre-migration provisioning
            // code, so that existing deployments adopt the baseline without any ALTER.
            modelBuilder.Entity<XR50TenantRegistryEntry>(e =>
            {
                e.ToTable(TableName);
                e.HasKey(t => t.TenantName);
                e.Property(t => t.TenantName).HasMaxLength(100);
                e.Property(t => t.TenantGroup).HasMaxLength(100);
                e.Property(t => t.Description).HasMaxLength(500);
                e.Property(t => t.StorageType).HasMaxLength(50).HasDefaultValue("OwnCloud");
                e.Property(t => t.TenantDirectory).HasMaxLength(500);
                e.Property(t => t.S3BucketName).HasMaxLength(255);
                e.Property(t => t.S3BucketRegion).HasMaxLength(50);
                e.Property(t => t.S3BucketArn).HasMaxLength(255);
                e.Property(t => t.StorageEndpoint).HasMaxLength(255);
                e.Property(t => t.OwnerName).HasMaxLength(255);
                e.Property(t => t.DefaultAICollection).HasMaxLength(255);
                e.Property(t => t.InnovChatbotBaseUrl).HasMaxLength(500);
                e.Property(t => t.InnovChatbotApiToken).HasMaxLength(1000);
                e.Property(t => t.InnovChatbotDefaultPilot).HasMaxLength(255);
                e.Property(t => t.HubTenantId).HasColumnType("char(36)").HasCharSet("utf8mb4");
                e.Property(t => t.DatabaseName).HasMaxLength(100);
                e.Property(t => t.CreatedAt).HasColumnType("datetime");
                e.Property(t => t.IsActive).HasDefaultValue(true);
                e.HasIndex(t => t.HubTenantId).IsUnique().HasDatabaseName("ux_registry_hub_tenant");
            });
        }
    }
}
