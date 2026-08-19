using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XR50TrainingAssetRepo.Controllers;
using XR50TrainingAssetRepo.Infrastructure.Auth;
using XR50TrainingAssetRepo.Models.DTOs;

namespace XR50TrainingAssetRepo.Tests.Controllers;

/// <summary>
/// Unit tests for the self-service tenant-provisioning rules in TenantsController.CreateTenant:
/// a Hub-authenticated caller is force-bound to their own Hub tenant id, cannot mint a system
/// admin through the owner flag, becomes tenant admin of the tenant they created, and cannot
/// create a second tenant for the same Hub tenant. System admins keep full control.
/// </summary>
public class TenantSelfServiceCreationTests
{
    private static readonly Guid CallerHubTenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherHubTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class StubTenantManagementService : IXR50TenantManagementService
    {
        public XR50Tenant? ExistingMappedTenant { get; set; }
        public XR50Tenant? CreatedTenant { get; private set; }
        public (string TenantName, string Db, string UserName, string? Email)? Granted { get; private set; }

        public Task<IEnumerable<XR50Tenant>> GetAllTenantsAsync() => Task.FromResult(Enumerable.Empty<XR50Tenant>());
        public Task<XR50Tenant> GetTenantAsync(string tenantName) => Task.FromResult(CreatedTenant!);
        public Task<XR50Tenant?> GetTenantByHubTenantIdAsync(Guid hubTenantId) => Task.FromResult(ExistingMappedTenant);

        public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant)
        {
            CreatedTenant = tenant;
            tenant.TenantSchema = XR50TenantDatabase.SchemaFor(tenant.TenantName);
            return Task.FromResult(tenant);
        }

        public Task<XR50Tenant> UpdateTenantAsync(string tenantName, XR50Tenant tenant) => Task.FromResult(tenant);

        public Task GrantTenantAdminAsync(string tenantName, string tenantDatabaseName, string userName, string? userEmail)
        {
            Granted = (tenantName, tenantDatabaseName, userName, userEmail);
            return Task.CompletedTask;
        }

        public Task<User> GetOwnerUserAsync(string ownerName, string tenantName) => Task.FromResult<User>(null!);
        public Task DeleteTenantAsync(string tenantName) => Task.CompletedTask;
        public Task DeleteTenantCompletelyAsync(string tenantName) => Task.CompletedTask;
    }

    private sealed class StubTenantService : IXR50TenantService
    {
        /// <summary>Simulates the derived tenant database already existing in MySQL.</summary>
        public bool TenantDatabaseExists { get; set; }

        public string GetCurrentTenant() => "default";
        public Task<bool> ValidateTenantAsync(string tenantName) => Task.FromResult(TenantDatabaseExists);
        public Task<bool> TenantExistsAsync(string tenantName) => Task.FromResult(TenantDatabaseExists);
        public Task<XR50Tenant> CreateTenantAsync(XR50Tenant tenant) => Task.FromResult(tenant);
        public string GetTenantSchema(string tenantName) => XR50TenantDatabase.SchemaFor(tenantName);
    }

    private sealed class StubStorageService : IStorageService
    {
        public Task<bool> CreateTenantStorageAsync(string tenantName, XR50Tenant tenant) => Task.FromResult(true);
        public Task<bool> DeleteTenantStorageAsync(string tenantName) => Task.FromResult(true);
        public Task<bool> TenantStorageExistsAsync(string tenantName) => Task.FromResult(true);
        public Task<string> UploadFileAsync(string tenantName, string fileName, IFormFile file, string? downloadFileName = null) => throw new NotSupportedException();
        public Task<Stream> DownloadFileAsync(string tenantName, string fileName) => throw new NotSupportedException();
        public Task<string> GetDownloadUrlAsync(string tenantName, string fileName, TimeSpan? expiration = null) => throw new NotSupportedException();
        public Task<bool> DeleteFileAsync(string tenantName, string fileName) => Task.FromResult(true);
        public Task<bool> FileExistsAsync(string tenantName, string fileName) => Task.FromResult(false);
        public Task<long> GetFileSizeAsync(string tenantName, string fileName) => Task.FromResult(0L);
        public Task<string> CreateShareAsync(string tenantName, XR50Tenant tenant, Asset asset) => throw new NotSupportedException();
        public Task<bool> DeleteShareAsync(string tenantName, string shareId) => Task.FromResult(true);
        public bool SupportsSharing() => false;
        public Task<StorageStatistics> GetStorageStatisticsAsync(string tenantName) => throw new NotSupportedException();
        public string GetStorageType() => "OwnCloud";
    }

    private static TenantsController CreateController(
        StubTenantManagementService service, ClaimsPrincipal user, StubTenantService? tenantService = null)
    {
        var controller = new TenantsController(
            service,
            tenantService ?? new StubTenantService(),
            new StubStorageService(),
            Options.Create(new IamOptions()),
            NullLogger<TenantsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user },
            },
        };
        return controller;
    }

    private static ClaimsPrincipal HubPrincipal(Guid hubTenantId, string email = "creator@example.com", string? role = null)
    {
        var claims = new List<Claim>
        {
            new("preferred_username", email),
            new("email", email),
            new(HubSessionTokenDefaults.HubTenantIdClaim, hubTenantId.ToString("D")),
        };
        if (role != null)
        {
            claims.Add(new Claim("role", role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, HubSessionTokenDefaults.SchemeName));
    }

    /// <summary>Mirrors what the handler emits for an e-mail-less service account: the Hub
    /// userId GUID as preferred_username and no email claim.</summary>
    private static ClaimsPrincipal ServiceAccountPrincipal(Guid hubTenantId, Guid hubUserId)
    {
        var claims = new List<Claim>
        {
            new("preferred_username", hubUserId.ToString("D")),
            new(HubSessionTokenDefaults.HubTenantIdClaim, hubTenantId.ToString("D")),
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, HubSessionTokenDefaults.SchemeName));
    }

    private static CreateTenantRequest Request(
        Guid? requestedHubTenantId = null, bool ownerAdmin = false, string tenantName = "selfservice") => new()
    {
        TenantName = tenantName,
        StorageType = "OwnCloud",
        OwnCloudConfig = new OwnCloudConfigurationRequest { TenantDirectory = "selfservice-files" },
        HubTenantId = requestedHubTenantId,
        Owner = new UserRequest { UserName = "owner", Password = "pw", Admin = ownerAdmin },
    };

    [Fact]
    public async Task SelfService_BindsCallersOwnHubTenantId_IgnoringRequestedValue()
    {
        var service = new StubTenantManagementService();
        var controller = CreateController(service, HubPrincipal(CallerHubTenantId));

        // The caller tries to claim a different Hub tenant's GUID.
        var result = await controller.CreateTenant(Request(requestedHubTenantId: OtherHubTenantId));

        result.Result.Should().BeOfType<OkObjectResult>();
        service.CreatedTenant!.HubTenantId.Should().Be(CallerHubTenantId);
    }

    [Fact]
    public async Task SelfService_CannotMintSystemAdmin_ThroughOwnerFlag()
    {
        var service = new StubTenantManagementService();
        var controller = CreateController(service, HubPrincipal(CallerHubTenantId));

        await controller.CreateTenant(Request(ownerAdmin: true));

        service.CreatedTenant!.Owner!.admin.Should().BeFalse();
    }

    [Fact]
    public async Task SelfService_GrantsCreatorTenantAdmin_OnTheirNewTenant()
    {
        var service = new StubTenantManagementService();
        var controller = CreateController(service, HubPrincipal(CallerHubTenantId, "ada@pilot.eu"));

        await controller.CreateTenant(Request());

        service.Granted.Should().NotBeNull();
        service.Granted!.Value.TenantName.Should().Be("selfservice");
        service.Granted.Value.UserName.Should().Be("ada@pilot.eu");
        service.Granted.Value.Email.Should().Be("ada@pilot.eu");
    }

    [Fact]
    public async Task SelfService_EmaillessServiceAccount_IsGrantedByHubUserIdGuid()
    {
        var hubUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var service = new StubTenantManagementService();
        var controller = CreateController(service, ServiceAccountPrincipal(CallerHubTenantId, hubUserId));

        await controller.CreateTenant(Request());

        service.Granted.Should().NotBeNull();
        service.Granted!.Value.UserName.Should().Be(hubUserId.ToString("D"));
        service.Granted.Value.Email.Should().BeNull();
    }

    [Fact]
    public async Task SelfService_SecondTenantForSameHubTenant_Returns409()
    {
        var service = new StubTenantManagementService
        {
            ExistingMappedTenant = new XR50Tenant { TenantName = "already-there", HubTenantId = CallerHubTenantId },
        };
        var controller = CreateController(service, HubPrincipal(CallerHubTenantId));

        var result = await controller.CreateTenant(Request());

        var problem = result.Result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        service.CreatedTenant.Should().BeNull();
    }

    [Fact]
    public async Task UuidTenantName_IsAccepted()
    {
        var service = new StubTenantManagementService();
        var controller = CreateController(service, HubPrincipal(CallerHubTenantId));

        var result = await controller.CreateTenant(
            Request(tenantName: "3f2b1a90-7c4d-4e5f-9a6b-8d7c6e5f4a3b"));

        result.Result.Should().BeOfType<OkObjectResult>();
        service.CreatedTenant!.TenantName.Should().Be("3f2b1a90-7c4d-4e5f-9a6b-8d7c6e5f4a3b");
    }

    [Fact]
    public async Task TenantName_WithOtherSpecialCharacters_IsStillRejected()
    {
        var service = new StubTenantManagementService();
        var controller = CreateController(service, HubPrincipal(CallerHubTenantId));

        var result = await controller.CreateTenant(Request(tenantName: "foo bar"));

        var problem = result.Result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        service.CreatedTenant.Should().BeNull();
    }

    [Fact]
    public async Task TenantName_WhoseDerivedDatabaseAlreadyExists_Returns409()
    {
        // "foo-bar" and "foo_bar" both derive xr50_tenant_foo_bar; with the database already
        // present, creation must refuse instead of silently attaching to the existing data.
        var service = new StubTenantManagementService();
        var tenantService = new StubTenantService { TenantDatabaseExists = true };
        var controller = CreateController(service, HubPrincipal(CallerHubTenantId), tenantService);

        var result = await controller.CreateTenant(Request(tenantName: "foo-bar"));

        var problem = result.Result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        service.CreatedTenant.Should().BeNull();
    }

    [Fact]
    public async Task SystemAdmin_KeepsRequestedHubTenantId_AndOwnerAdminFlag()
    {
        var service = new StubTenantManagementService();
        // Hub-authenticated system admin (Users.admin in the mapped tenant DB): full control.
        var controller = CreateController(service, HubPrincipal(CallerHubTenantId, role: "systemadmin"));

        await controller.CreateTenant(Request(requestedHubTenantId: OtherHubTenantId, ownerAdmin: true));

        service.CreatedTenant!.HubTenantId.Should().Be(OtherHubTenantId);
        service.CreatedTenant.Owner!.admin.Should().BeTrue();
        service.Granted.Should().BeNull("system admins are not auto-granted tenant admin");
    }
}
