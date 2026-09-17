using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Controllers;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Tests.Controllers;

/// <summary>
/// Unit tests for how TenantsController treats a tenant that does not exist. DeleteTenant: a
/// tenant with neither a registry row nor a database is a 404 and nothing is deleted, while a
/// registered tenant and a half-provisioned one (database present, no registry row) are both
/// still deleted. GetTenant: the service's not-found ArgumentException is a 404, not a 500.
/// </summary>
public class TenantNotFoundTests
{
    /// <summary>
    /// Mirrors the real service: GetTenantAsync throws ArgumentException for a tenant that has
    /// no active registry row instead of returning null.
    /// </summary>
    private sealed class StubTenantManagementService : IXR50TenantManagementService
    {
        public HashSet<string> RegisteredTenants { get; } = new();
        public List<string> Deleted { get; } = new();

        public Task<XR50Tenant> GetTenantAsync(string tenantName) =>
            RegisteredTenants.Contains(tenantName)
                ? Task.FromResult(new XR50Tenant { TenantName = tenantName })
                : throw new ArgumentException($"Tenant '{tenantName}' not found");

        public Task DeleteTenantAsync(string tenantName)
        {
            Deleted.Add(tenantName);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<XR50Tenant>> GetAllTenantsAsync() => Task.FromResult(Enumerable.Empty<XR50Tenant>());
        public Task<XR50Tenant?> GetTenantByHubTenantIdAsync(Guid hubTenantId) => Task.FromResult<XR50Tenant?>(null);
        public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
        public Task<XR50Tenant> UpdateTenantAsync(string tenantName, XR50Tenant tenant) => Task.FromResult(tenant);
        public Task GrantTenantAdminAsync(string tenantName, string tenantDatabaseName, string userName, string? userEmail) => Task.CompletedTask;
        public Task<User> GetOwnerUserAsync(string ownerName, string tenantName) => Task.FromResult<User>(null!);
        public Task DeleteTenantCompletelyAsync(string tenantName) => DeleteTenantAsync(tenantName);
    }

    private sealed class StubTenantService : IXR50TenantService
    {
        /// <summary>Tenant names whose database is present in MySQL.</summary>
        public HashSet<string> Databases { get; } = new();

        public string GetCurrentTenant() => "default";
        public Task<bool> ValidateTenantAsync(string tenantName) => TenantExistsAsync(tenantName);
        public Task<bool> TenantExistsAsync(string tenantName) => Task.FromResult(Databases.Contains(tenantName));
        public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
        public string GetTenantSchema(string tenantName) => XR50TenantDatabase.SchemaFor(tenantName);
    }

    // DeleteTenant never touches storage, so the storage dependency is left null.
    private static TenantsController CreateController(
        StubTenantManagementService service, StubTenantService tenantService) =>
        new(service, tenantService, null!, Options.Create(new IamOptions()), NullLogger<TenantsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task UnknownTenant_Returns404_AndDeletesNothing()
    {
        var service = new StubTenantManagementService();
        var controller = CreateController(service, new StubTenantService());

        var result = await controller.DeleteTenant("nonexistent");

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        service.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredTenant_IsDeleted()
    {
        var service = new StubTenantManagementService { RegisteredTenants = { "acme" } };
        var controller = CreateController(service, new StubTenantService { Databases = { "acme" } });

        var result = await controller.DeleteTenant("acme");

        result.Should().BeOfType<OkObjectResult>();
        service.Deleted.Should().Equal("acme");
    }

    [Fact]
    public async Task HalfProvisionedTenant_WithDatabaseButNoRegistryRow_IsStillDeleted()
    {
        var service = new StubTenantManagementService();
        var controller = CreateController(service, new StubTenantService { Databases = { "orphan" } });

        var result = await controller.DeleteTenant("orphan");

        result.Should().BeOfType<OkObjectResult>();
        service.Deleted.Should().Equal("orphan");
    }

    [Fact]
    public async Task GetUnknownTenant_Returns404()
    {
        var controller = CreateController(new StubTenantManagementService(), new StubTenantService());

        var result = await controller.GetTenant("nonexistent");

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}
