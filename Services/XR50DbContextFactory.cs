using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Services
{
    public interface IXR50TenantDbContextFactory
    {
        XR50TrainingContext CreateDbContext();
        XR50TrainingContext CreateAdminDbContext();
    }

    public class XR50TenantDbContextFactory : IXR50TenantDbContextFactory
    {
        private readonly IXR50TenantService _tenantService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<XR50TenantDbContextFactory> _logger;

        public XR50TenantDbContextFactory(
            IXR50TenantService tenantService,
            IConfiguration configuration,
            ILogger<XR50TenantDbContextFactory> logger)
        {
            _tenantService = tenantService;
            _configuration = configuration;
            _logger = logger;
        }

        public XR50TrainingContext CreateDbContext()
        {
            try
            {
                var currentTenant = _tenantService.GetCurrentTenant();
                var connectionString = GetTenantConnectionString(currentTenant);

                _logger.LogInformation(" DbContext Factory - Creating context for tenant: {TenantName}", currentTenant);
                _logger.LogInformation("Using connection: {ConnectionString}", connectionString.Replace("Password=", "Password=***"));

                var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
                optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

                // Enable detailed logging in development
                if (_configuration.GetValue<string>("Environment") == "Development")
                {
                    optionsBuilder.EnableSensitiveDataLogging();
                    optionsBuilder.EnableDetailedErrors();
                }

                return new XR50TrainingContext(optionsBuilder.Options, _tenantService, _configuration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating tenant DbContext");
                throw;
            }
        }

        public XR50TrainingContext CreateAdminDbContext()
        {
            try
            {
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");

                _logger.LogInformation(" DbContext Factory - Creating ADMIN context for magical_library");
                _logger.LogInformation("Using admin connection: {ConnectionString}", baseConnectionString.Replace("Password=", "Password=***"));

                var optionsBuilder = new DbContextOptionsBuilder<XR50TrainingContext>();
                optionsBuilder.UseMySql(baseConnectionString, ServerVersion.AutoDetect(baseConnectionString));

                // Enable detailed logging in development
                if (_configuration.GetValue<string>("Environment") == "Development")
                {
                    optionsBuilder.EnableSensitiveDataLogging();
                    optionsBuilder.EnableDetailedErrors();
                }

                return new XR50TrainingContext(optionsBuilder.Options, _tenantService, _configuration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating admin DbContext");
                throw;
            }
        }

        private string GetTenantConnectionString(string tenantName)
        {
            var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
            var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

            if (tenantName == "default" || string.IsNullOrEmpty(tenantName))
            {
                _logger.LogInformation("Using default database for tenant: {TenantName}", tenantName);
                return baseConnectionString;
            }
            var tenantDatabase = _tenantService.GetTenantSchema(tenantName);
            var tenantConnectionString = TenantConnectionString.ForDatabase(baseConnectionString, tenantDatabase);

            _logger.LogInformation(" Switching to tenant database: {TenantDatabase} for tenant: {TenantName}", tenantDatabase, tenantName);

            return tenantConnectionString;
        }
    }
}