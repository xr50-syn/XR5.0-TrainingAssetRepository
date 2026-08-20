# CLAUDE.md - XR5.0 Training Asset Repository

## Skills (Slash Commands)

Use these skills for common tasks:

| Skill | Command | Description |
|-------|---------|-------------|
| Material Type | `/material-type` | Guide for creating or updating material types (TPH entities) |
| Run Tests | `/run-tests` | Run functional tests against a running XR5.0 API server |
| API Probe | `/api-probe` | Targeted curl probes for behavior too narrow for any Jest scope (Layer 4 of the Autonomous Test Loop) |
| AI Assistant Probe | `/ai-assistant-probe` | Drive the AI Assistant -> DataLens ingestion + status pipeline end-to-end against the running stack, with direct DataLens cross-checks |

## Project Overview

This is the **XR5.0 Training Asset Repository**, a research prototype developed for the Horizon Europe XR5.0 project (Grant Agreement No. 101135209). It provides multi-tenant, cloud-agnostic storage for Extended Reality (XR) training materials.

**Status**: Research prototype. JWT authentication + RBAC are enforced (see Authentication & Authorization below); the production identity provider is TBD (Keycloak is the development stand-in), and local password storage / IAM user provisioning are deferred until the platform's IAM server is decided.

### Technology Stack
- **Framework**: ASP.NET Core 8.0
- **ORM**: Entity Framework Core 8.0 with Pomelo MySQL provider
- **Database**: MySQL/MariaDB (PostgreSQL migration planned)
- **Storage**: AWS S3, OwnCloud, MinIO (abstracted via IStorageService)
- **API Docs**: Swagger/OpenAPI

## Architecture

### Multi-Tenancy Pattern: Database-per-Tenant

```
Admin Database (xr50_repository)
├── Tenants table (configurations)
├── Global users
└── System config

Tenant Databases (xr50_tenant_{name})
├── Assets
├── Materials (with TPH inheritance)
├── TrainingPrograms
├── LearningPaths
└── Associations
```

**Key Components:**
- `IXR50TenantDbContextFactory` - Creates tenant-specific DbContext instances
- `XR50TenantManagementService` - Handles tenant provisioning
- `XR50MigrationService` - Applies migrations to tenant databases

### Storage Abstraction

All storage operations go through `IStorageService`:

```csharp
public interface IStorageService
{
    Task<bool> CreateTenantStorageAsync(string tenantName, XR50Tenant tenant);
    Task<string> UploadFileAsync(string tenantName, string fileName, IFormFile file);
    Task<Stream> DownloadFileAsync(string tenantName, string fileName);
    Task<string> GetDownloadUrlAsync(string tenantName, string fileName, TimeSpan? expiration = null);
    Task<bool> DeleteFileAsync(string tenantName, string fileName);
    Task<string> CreateShareAsync(string tenantName, XR50Tenant tenant, Asset asset);
    bool SupportsSharing();
    string GetStorageType();
}
```

**Implementations:**
- `S3StorageServiceImplementation` - AWS S3 and MinIO
- `OwnCloudStorageServiceImplementation` - OwnCloud/WebDAV

### Authentication & Authorization

JWT Bearer (OIDC discovery) + policy-based RBAC with tenant binding. The building blocks live in
`Infrastructure/Auth/` (`IamOptions`, `ClaimsPrincipalExtensions`, requirements/handlers); policies
are registered in `Program.cs`.

**Policies** (attributes AND together — controller-level `TenantMember` + action-level `TenantAdmin`):

| Policy | Grants | Applied to |
|---|---|---|
| `TenantMember` | Token's tenant claim matches the `{tenantName}` route segment (system admins exempt) | Controller-level on all tenant-scoped controllers; learner-facing POSTs (quiz submit, chat, mark-complete) |
| `TenantAdmin` | Tenant match + role in `IAM:TenantAdminRoles` | Content-management mutations (create/update/delete of materials, assets, users, programs, paths, ingestion config) |
| `SystemAdmin` | Role in `IAM:SystemAdminRoles` | Tenant provisioning/deletion, list-all-tenants, troubleshooting controller |
| *(fallback)* | Any authenticated principal | Every endpoint without an explicit attribute (`FallbackPolicy`). `/health` and `/api/test` are `[AllowAnonymous]` |

**Provider-agnostic claims**: the production IAM is TBD, so claim names/role values are configured
under `IAM` (`TenantClaim`, `RoleClaim`, `TenantAdminRoles`, `SystemAdminRoles`); defaults match the
bundled Keycloak realm (`keycloak-config/xr50-realm.json`). Extract user identity with
`User.GetUserId()` (`ClaimsPrincipalExtensions`) — do not hand-roll claim fallback chains.

**Development bypass**: `IAM:AllowAnonymousInDevelopment=true` + Development environment allows
anonymous requests (evaluated in one place: `XR50AuthorizationHandler`). `NO_AUTH=true` Jest runs
require the stack started with `IAM_ALLOW_ANONYMOUS=true`; the compose default is `false`, so a
default sandbox run enforces auth (authenticate against the bundled Keycloak: realm users
`testuser`/`admin`/`tenantadmin` are tenant-scoped to `test_company`; `sysadmin` holds
`systemadmin` and is what the Jest harness uses by default, since per-run test tenants
require the tenant-binding exemption).

**Hermetic tests** authenticate via `TestAuthHandler` (`tests/.../Fixtures/TestAuthHandler.cs`):
default principal is a systemadmin; override per request with `X-Test-User`/`X-Test-Roles`/
`X-Test-Tenant`, or `X-Test-Anonymous: true` for 401 paths. See `Integration/AuthorizationTests.cs`.

**Deferred** until the platform IAM is decided: local `User.Password` hashing/removal, IAM user
provisioning, and wiring the `TenantAdmins` DB table into authorization (roles come from the token only).

## Directory Structure

```
/
├── Controllers/           # REST API endpoints
│   ├── XR50AssetController.cs
│   ├── XR50MaterialsController.cs
│   ├── XR50TenantController.cs
│   ├── XR50TrainingProgramController.cs
│   ├── XR50LearningPathController.cs
│   └── XR50UserController.cs
├── Services/              # Business logic
│   ├── XR50AssetService.cs
│   ├── XR50MaterialsService.cs
│   ├── XR50TenantManagementService.cs
│   ├── S3StorageServiceImplementation.cs
│   └── OwnCloudStorageServiceImplementation.cs
├── Models/                # Entity models
│   ├── Asset.cs
│   ├── Material.cs        # Base class + derived types
│   ├── TrainingProgram.cs
│   ├── LearningPath.cs
│   └── DTOs/              # Request/Response objects
├── Data/                  # EF Core configuration
│   ├── XR50TrainingContext.cs
│   └── Migrations/
├── tests/                 # Test suites
└── Program.cs             # Service registration
```

## Coding Conventions

### Naming

- **Controllers**: `XR50{Entity}Controller` (e.g., `XR50AssetController`)
- **Services**: `{Entity}Service` with `I{Entity}Service` interface
- **DTOs**: `Create{Entity}Request`, `{Entity}Response`, `Update{Entity}Request`
- **Route naming**: lowercase with hyphens for tenant names

### Controller Pattern

Controllers are tenant-scoped via route parameter:

```csharp
[Route("api/{tenantName}/[controller]")]
[ApiController]
public class materialsController : ControllerBase
{
    private readonly IMaterialService _materialService;
    private readonly ILogger<materialsController> _logger;

    public materialsController(
        IMaterialService materialService,
        ILogger<materialsController> logger)
    {
        _materialService = materialService;
        _logger = logger;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Material>> GetMaterial(string tenantName, int id)
    {
        _logger.LogInformation("Getting material {Id} for tenant: {TenantName}", id, tenantName);

        var material = await _materialService.GetMaterialAsync(id);
        if (material == null)
        {
            _logger.LogWarning("Material {Id} not found in tenant: {TenantName}", id, tenantName);
            return NotFound();
        }

        return material;
    }
}
```

### Error Handling Pattern

Use structured try-catch with appropriate HTTP responses:

```csharp
try
{
    var result = await _service.PerformOperationAsync();
    return Ok(result);
}
catch (ArgumentException ex)
{
    _logger.LogWarning("Invalid request for tenant {TenantName}: {Message}", tenantName, ex.Message);
    return BadRequest(new { Error = ex.Message });
}
catch (KeyNotFoundException ex)
{
    _logger.LogWarning("Resource not found: {Message}", ex.Message);
    return NotFound(new { Error = ex.Message });
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error in operation for tenant: {TenantName}", tenantName);
    return StatusCode(500, new { Error = "Internal server error" });
}
```

### Logging Pattern

Use structured logging with semantic parameters:

```csharp
// Information level for operations
_logger.LogInformation("Creating material '{Name}' with {MaterialCount} materials for tenant: {TenantName}",
    request.Name, request.Materials.Count, tenantName);

// Warning level for expected failures
_logger.LogWarning("Material {Id} not found in tenant: {TenantName}", id, tenantName);

// Error level with exception
_logger.LogError(ex, "Failed to create material {Name} - Transaction rolled back", material.Name);

// Debug level for detailed diagnostics
_logger.LogDebug("Read {BytesRead} bytes from stream. Magic bytes: {MagicBytes}",
    bytesRead, string.Join(" ", buffer.Take(12).Select(b => $"0x{b:X2}")));
```

**Do NOT use emojis in log messages or comments.**

### Service Pattern with Transactions

```csharp
public async Task<Material> CreateMaterialAsyncComplete(Material material)
{
    using var context = _dbContextFactory.CreateDbContext();
    using var transaction = await context.Database.BeginTransactionAsync();
    
    try
    {
        material.Created_at = DateTime.UtcNow;
        material.Updated_at = DateTime.UtcNow;
        SetMaterialTypeFromClass(material);

        context.Materials.Add(material);
        await context.SaveChangesAsync();

        // Process child entities...
        
        await transaction.CommitAsync();
        return material;
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        _logger.LogError(ex, "Failed to create material {Name} - Transaction rolled back", material.Name);
        throw;
    }
}
```

## Entity Patterns

### Material Type Hierarchy (TPH - Table Per Hierarchy)

Base class with discriminator:

```csharp
public class Material
{
    public int id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public MaterialType Type { get; set; }  // Discriminator
    public DateTime? Created_at { get; set; }
    public DateTime? Updated_at { get; set; }
    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }
}

// Derived types
public class VideoMaterial : Material
{
    public List<VideoTimestamp>? VideoTimestamps { get; set; }
}

public class WorkflowMaterial : Material
{
    public List<WorkflowStep>? WorkflowSteps { get; set; }
}

public class ChecklistMaterial : Material
{
    public List<ChecklistEntry>? ChecklistEntries { get; set; }
}

// Generic external chatbot — the thin "/ask"-style proxy
public class ChatbotMaterial : Material
{
    public string? ChatbotConfig { get; set; }
    public string? ChatbotModel { get; set; }
    public string? ChatbotPrompt { get; set; }
}

// DataLens-backed RAG assistant (document Q&A; multi-asset, status tracking, sessions)
public class AIAssistantMaterial : Material
{
    public string? CollectionName { get; set; }       // DataLens collection (defaults to its own aiassist_{id})
    public string AIAssistantStatus { get; set; }     // notready | process | ready
    public string? AIAssistantAssetIds { get; set; }  // JSON array of asset IDs
}

// INNOV "LLM Engine" RAG chatbot (pilot-scoped; per-tenant connection)
public class InnovChatbotMaterial : Material
{
    public string? Pilot { get; set; }                // falls back to tenant InnovChatbotDefaultPilot
    public string InnovStatus { get; set; }           // notready | process | ready
    public string? InnovAssetIds { get; set; }        // JSON array of asset IDs
    public string? ExpertiseLevel { get; set; }       // beginner | intermediate | expert
}
```

> The chatbot-family types are **separate material types** behind a shared `IChatbotProvider` seam
> (`Services/Chatbot/`), not one type with a provider flag. Conversation/ingestion endpoints live
> under dedicated controllers (`/chat`, `/ai-assistant`, `/innov-chatbot`); the generic Chat API
> routes to the DataLens **inference** endpoint against the tenant's `DefaultAICollection`. See the
> [`/material-type`](.claude/skills/material-type.md) skill (External API Integration) and
> `docs/api/chat-api.md`.

**MaterialType enum:**
```csharp
// Models/Material.cs — the enum is named `Type` (commonly aliased: `using MaterialType = XR50TrainingAssetRepo.Models.Type`).
// Order matters: the value is persisted as the ordinal int in `Materials.Type`, so APPEND new values at the end.
public enum Type
{
    Image, Video, PDF, Unity, Chatbot, Questionnaire, Checklist, Workflow,
    MQTT_Template, Answers, Quiz, Default, AIAssistant, InnovChatbot
}
```

**Setting type from class:**
```csharp
private void SetMaterialTypeFromClass(Material material)
{
    material.Type = material switch
    {
        VideoMaterial => MaterialType.Video,
        ImageMaterial => MaterialType.Image,
        ChecklistMaterial => MaterialType.Checklist,
        WorkflowMaterial => MaterialType.Workflow,
        PDFMaterial => MaterialType.PDF,
        UnityMaterial => MaterialType.Unity,
        ChatbotMaterial => MaterialType.Chatbot,
        QuestionnaireMaterial => MaterialType.Questionnaire,
        MQTT_TemplateMaterial => MaterialType.MQTT_Template,
        QuizMaterial => MaterialType.Quiz,
        AIAssistantMaterial => MaterialType.AIAssistant,
        InnovChatbotMaterial => MaterialType.InnovChatbot,
        DefaultMaterial => MaterialType.Default,
        _ => MaterialType.Default
    };
}
```

### Asset Type Detection (Magic Bytes)

Assets use binary stream detection for security:

```csharp
private async Task<(string filetype, AssetType assetType)> DetectFileTypeFromStream(Stream stream)
{
    var buffer = new byte[12];
    stream.Seek(0, SeekOrigin.Begin);
    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

    // PDF: %PDF (0x25 0x50 0x44 0x46)
    if (buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46)
        return ("pdf", AssetType.PDF);

    // PNG: 0x89 0x50 0x4E 0x47
    if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
        return ("png", AssetType.Image);

    // JPEG: 0xFF 0xD8 0xFF
    if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
        return ("jpg", AssetType.Image);

    // ... more signatures
}
```

**Supported signatures:**
- Images: PNG, JPEG, GIF, BMP, WebP
- Videos: MP4, MOV, AVI, WebM
- Documents: PDF
- Unity: UnityFS, UnityWeb bundles

### Relationship Patterns

**Many-to-Many:**
- TrainingProgram ↔ LearningPath
- LearningPath ↔ Material
- Material ↔ Material (parent-child hierarchy)

**Circular Reference Prevention:**
```csharp
Task<bool> WouldCreateCircularReferenceAsync(int parentMaterialId, int childMaterialId);
```

## API Response Patterns

### Success Response

```csharp
return Ok(new CreateMaterialResponse
{
    Status = "success",
    Message = "Material created",
    id = material.id,
    Name = material.Name,
    Type = material.Type.ToString(),
    Created_at = material.Created_at
});
```

### Error Response

```csharp
return BadRequest(new { Error = "Validation failed", Details = ex.Message });
return NotFound(new { Error = $"Material with ID {id} not found" });
return StatusCode(500, new { Error = "Internal server error" });
```

### CreatedAtAction Pattern

```csharp
return CreatedAtAction(
    nameof(GetTrainingProgram),
    new { tenantName, id = result.id },
    result);
```

## Dependency Injection

### Service Registration (Program.cs)

```csharp
// Core services
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IMaterialService, MaterialService>();
builder.Services.AddScoped<ITrainingProgramService, TrainingProgramService>();

// Tenant management
builder.Services.AddScoped<IXR50TenantManagementService, XR50TenantManagementService>();
builder.Services.AddScoped<IXR50TenantDbContextFactory, XR50TenantDbContextFactory>();

// Storage (configured by environment)
var storageType = builder.Configuration.GetValue<string>("Storage__Type") ?? "OwnCloud";
if (storageType.Equals("S3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<IStorageService, S3StorageServiceImplementation>();
else
    builder.Services.AddScoped<IStorageService, OwnCloudStorageServiceImplementation>();
```

### Factory Pattern for Tenant DbContext

```csharp
public interface IXR50TenantDbContextFactory
{
    XR50TrainingContext CreateDbContext();
}

// Usage in service
public class MaterialService : IMaterialService
{
    private readonly IXR50TenantDbContextFactory _dbContextFactory;

    public async Task<Material?> GetMaterialAsync(int id)
    {
        using var context = _dbContextFactory.CreateDbContext();
        return await context.Materials.FindAsync(id);
    }
}
```

## Testing

### Autonomous Test Loop (for AI agents)

When making code changes, verify them in four layers, fail-fast. A failure at any layer stops the chain — don't run later layers with broken earlier ones.

1. **Layer 1 — Build**: `dotnet build` from the repo root.
2. **Layer 2 — Hermetic xUnit**: `dotnet test` from the repo root. The xUnit suite uses `WebApplicationFactory<Program>` with EF Core InMemory + a `MockStorageService` ([tests/XR50TrainingAssetRepo.Tests/Fixtures/WebApplicationFixture.cs](tests/XR50TrainingAssetRepo.Tests/Fixtures/WebApplicationFixture.cs)) — no MySQL, no MinIO, no Keycloak required. ~10–20s.
3. **Layer 3 — Scoped Jest**: probe `curl -s http://localhost:5286/health` first. If it doesn't return 200, **stop** and tell the user to run `docker-compose --profile sandbox up -d`. Do NOT claim a change is verified when Layer 3 was skipped due to a missing stack. If the health probe returns 200, run the `npm run test:<scope>` matching the change (table below).
4. **Layer 4 — Targeted probe**: if the change is too narrow for any Jest scope (e.g., a one-off bug fix in a specific code path) or the user asks for a quick targeted check, invoke the `/api-probe` skill at [.claude/skills/api-probe/SKILL.md](.claude/skills/api-probe/SKILL.md). Same `/health` gate applies. Probes are for the gaps — don't substitute them for Jest scopes that exist.

#### Scope routing for Layer 3

| Change touches | Run |
|---|---|
| AI Assistant materials, AiStatusSync | `npm run test:ai-assistant` |
| Chat API / INNOV chatbot (no dedicated Jest scope yet) | `npm run test:ai-assistant` for regression, then `/api-probe` for the chat/innov path |
| Material/Asset CRUD, magic-byte detection, type hierarchy | `npm run test:materials` + `test:hierarchy` |
| Tenant provisioning, MigrationService | `npm run test:tenant` |
| Storage backends (S3/OwnCloud) | `npm run test:storage` |
| Training programs, learning paths | `npm run test:programs` + `test:hierarchy` |
| User CRUD | `npm run test:users` |
| Auth/Keycloak wiring in Program.cs | `npm run test:auth` + `test:health` |
| Cross-cutting (Program.cs DI, middleware) or unclear | `npm run test:health` and ask the user before running more |

If a change spans multiple areas, run the union of scopes. If you're not sure which scope to pick, ask once rather than running the wrong tests silently.

### Test Structure

```
tests/
├── XR50TrainingAssetRepo.Tests/                    # xUnit (.NET) — hermetic, no infra needed
│   ├── XR50TrainingAssetRepo.Tests.csproj
│   ├── GlobalUsings.cs
│   ├── Fixtures/
│   │   └── WebApplicationFixture.cs                # WebApplicationFactory<Program> + InMemory DB + MockStorageService
│   ├── Factories/
│   │   └── MaterialFactory.cs                      # Material payload builders
│   ├── Smoke/
│   │   └── HealthCheckTests.cs                     # /api/test, /swagger, materials/programs/learningpaths reachability
│   ├── Integration/
│   │   └── SubcomponentRelatedMaterialsTests.cs    # Checklist/workflow/video subcomponent material relationships
│   └── Services/
│       ├── AIAssistantMaterialUpdateTests.cs       # AI Assistant material update flow
│       └── InnovChatbotMaterialTests.cs            # INNOV chatbot ingest/status + chat (fake provider)
├── functional/                                      # Jest — requires running stack on http://localhost:5286
│   ├── config.js                                    # Environment configuration
│   ├── setup.js                                     # Global setup (health + tenant)
│   ├── teardown.js                                  # Global cleanup
│   ├── testSequencer.js                             # Enforces alphabetical suite order
│   ├── jest.config.js
│   ├── package.json
│   ├── helpers/
│   │   ├── api-client.js                            # Axios-based HTTP client with optional Keycloak auth
│   │   └── test-data.js                             # Tenant/material/program/user payload generators
│   └── suites/
│       ├── 01-health.test.js                        # API health + Swagger
│       ├── 02-auth.test.js                          # Keycloak auth (skippable with NO_AUTH=true)
│       ├── 03-tenant.test.js                        # Tenant CRUD
│       ├── 04-storage.test.js                       # Storage validation
│       ├── 05-materials.test.js                     # Material CRUD
│       ├── 06-hierarchy.test.js                     # Material parent/child relationships
│       ├── 07-programs.test.js                      # Training programs + learning paths
│       ├── 08-users.test.js                         # User CRUD
│       └── 09-ai-assistant.test.js                  # AI Assistant material flows
├── multi_framework_tests.js                         # Multi-framework integration scratchpad
└── test_configuration.json                          # Legacy test config
```

xUnit tests are **hermetic** (InMemory DB + MockStorageService). `dotnet test` needs no external infra. Jest tests require the docker-compose `sandbox` profile to be up.

### Running Tests

#### Unit Tests (.NET)

```bash
# Run all unit tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal
```

#### Functional Tests (Jest)

```bash
# Install dependencies (first time only)
cd tests/functional && npm install

# Run all functional tests
cd tests/functional && npm test

# Run with verbose output
npm run test:verbose

# Run specific test suites
npm run test:health        # Health checks only
npm run test:auth          # Authentication tests
npm run test:tenant        # Tenant operations
npm run test:storage       # Storage validation
npm run test:materials     # Material CRUD
npm run test:hierarchy     # Material relationships
npm run test:programs      # Training programs
npm run test:users         # User management
npm run test:ai-assistant  # AI Assistant material flows
```

### Functional Test Configuration

Tests are configured via environment variables:

```bash
# API Configuration
API_URL=http://localhost:5286          # API base URL

# Authentication (optional - use NO_AUTH=true to skip)
KEYCLOAK_URL=http://localhost:8180     # Keycloak server
KEYCLOAK_REALM=xr50                    # Keycloak realm
KEYCLOAK_CLIENT=xr50-training-app      # Client ID
TEST_USER=testuser                     # Test credentials
TEST_PASSWORD=testuser123

# Storage Configuration
STORAGE_TYPE=S3                        # S3 or OwnCloud
S3_BUCKET=xr50-test                    # S3 bucket name
S3_REGION=eu-west-1                    # AWS region
S3_ENDPOINT=http://minio:9000          # MinIO endpoint (optional)

# Test Options
EXISTING_TENANT=my-tenant              # Use existing tenant (skip creation)
NO_AUTH=true                           # Skip authentication (requires the stack started with IAM_ALLOW_ANONYMOUS=true)
DEBUG=true                             # Enable verbose logging
SKIP_CLEANUP=true                      # Don't delete test resources
```

**Example: Running tests against local development:**

```bash
cd tests/functional
NO_AUTH=true EXISTING_TENANT=dev-tenant npm test
```

**Example: Running tests with MinIO:**

```bash
cd tests/functional
STORAGE_TYPE=S3 S3_ENDPOINT=http://localhost:9000 npm test
```

### Test Data Generators

The `test-data.js` helper provides factory functions:

```javascript
const testData = require('./helpers/test-data');

// Materials
testData.createVideoMaterial()        // Video with path, duration, resolution
testData.createChecklistMaterial()    // Checklist with entries
testData.createWorkflowMaterial()     // Workflow with steps
testData.createChatbotMaterial()      // Chatbot configuration

// Users
testData.createTestUser()             // Regular user
testData.createAdminUser()            // Admin user

// Tenants
testData.createS3Tenant()             // S3 storage tenant
testData.createOwnCloudTenant()       // OwnCloud storage tenant
testData.createMinioTenant()          // MinIO (S3-compatible) tenant

// Training
testData.createTrainingProgram()      // Basic program
testData.createProgramWithPaths()     // Program with learning paths
```

### API Client

The `api-client.js` provides authenticated HTTP methods:

```javascript
const apiClient = require('./helpers/api-client');

// Authenticate (optional with NO_AUTH=true)
await apiClient.authenticate(username, password);

// CRUD operations
await apiClient.createMaterial({ name: 'Test', type: 'Video' });
await apiClient.getMaterial(id);
await apiClient.updateMaterial(id, { name: 'Updated' });
await apiClient.deleteMaterial(id);

// File uploads
await apiClient.uploadFile(url, filePath);
await apiClient.uploadBuffer(url, buffer, filename);
```

### Writing New Tests

Follow the existing test patterns:

```javascript
describe('Feature Name', () => {
  beforeAll(async () => {
    // Authenticate if needed
    await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
  });

  afterAll(async () => {
    // Cleanup created resources
    if (createdId && !config.SKIP_CLEANUP) {
      await apiClient.deleteResource(createdId);
    }
  });

  test('should do something', async () => {
    const response = await apiClient.createSomething(data);

    // Accept multiple valid status codes
    expect([200, 201]).toContain(response.status);

    if (response.status === 201) {
      expect(response.data).toHaveProperty('id');
    }
  });
});
```

## Common Tasks

### Adding a New Material Type

1. Create derived class in `Models/Material.cs`:
```csharp
public class NewMaterial : Material
{
    public string? NewProperty { get; set; }
    public List<NewChild>? Children { get; set; }
}
```

2. Append to the `Type` enum (`Models/Material.cs`) — **at the end** (it's stored as an ordinal int)
3. Add a case to `SetMaterialTypeFromClass()` (`Services/Materials/MaterialService.cs`)
4. Wire the controller dispatch points (`XR50MaterialsController`): both create switches,
   `ValidMaterialTypes`, the `GetCompleteMaterialDetails` switch + a `GetXDetails` method,
   `GetLowercaseType()`, and the `ParseMaterialFromJson` factory
5. Add tenant-DB columns/side-tables in `XR50ManualTableCreator` + an idempotent ALTER migration wired
   into `CreateAllTablesAsync` and `XR50MigrationService` — tenant tables are created by the manual
   table creator, **not** `dotnet ef migrations`
6. Register the service in `Program.cs`; add a hermetic xUnit test + a `MaterialFactory` builder

**Authoritative step-by-step: the [`/material-type`](.claude/skills/material-type.md) skill** (covers
every dispatch point, the MySQL 64-char identifier limit, and the external-API/provider pattern).

### Adding a New Storage Backend

1. Implement `IStorageService` interface
2. Add configuration section in appsettings
3. Add registration logic in Program.cs

### Adding a New Controller

1. Create controller with tenant route:
```csharp
[Route("api/{tenantName}/[controller]")]
[ApiController]
[ApiExplorerSettings(GroupName = "newgroup")]
public class NewController : ControllerBase
```

2. Add Swagger doc group in Program.cs
3. Add DocInclusionPredicate case

## Common Pitfalls

### 1. Forgetting DbContext Disposal
Always use `using`:
```csharp
using var context = _dbContextFactory.CreateDbContext();
```

### 2. Not Resetting Stream Position
After reading stream for magic bytes detection:
```csharp
stream.Seek(0, SeekOrigin.Begin);
```

### 3. Missing Transaction Rollback
Always wrap complex operations:
```csharp
using var transaction = await context.Database.BeginTransactionAsync();
try { ... await transaction.CommitAsync(); }
catch { await transaction.RollbackAsync(); throw; }
```

### 4. Circular Reference in Material Hierarchy
Always check before creating parent-child relationships:
```csharp
if (await WouldCreateCircularReferenceAsync(parentId, childId))
    throw new InvalidOperationException("Would create circular reference");
```

### 5. Tenant Isolation
Never share DbContext between tenants - always use factory pattern.

## Environment Variables

```bash
# Database
ConnectionStrings__DefaultConnection=Server=localhost;Database=xr50_repository;User=root;Password=...

# Storage
STORAGE_TYPE=S3|OwnCloud
AWS_ACCESS_KEY_ID=...
AWS_SECRET_ACCESS_KEY=...
AWS_REGION=eu-west-1
S3_BUCKET_PREFIX=xr50

# OwnCloud
OWNCLOUD_URL=http://localhost:8080
OWNCLOUD_USERNAME=admin
OWNCLOUD_PASSWORD=...
```

## Quick Reference

| Pattern | Location | Example |
|---------|----------|---------|
| Tenant route | Controllers | `[Route("api/{tenantName}/[controller]")]` |
| Error response | Controllers | `return BadRequest(new { Error = message })` |
| Logging | Services | `_logger.LogInformation("Message {Param}", value)` |
| Transaction | Services | `using var transaction = await context.Database.BeginTransactionAsync()` |
| Material type | MaterialService | `SetMaterialTypeFromClass(material)` |
| File detection | AssetService | `DetectFileTypeFromStream(stream)` |
| Factory | Services | `using var context = _dbContextFactory.CreateDbContext()` |
