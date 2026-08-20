# Material Type Creation/Update Skill

Use this skill when creating a new material type or updating an existing one in the XR5.0 Training Asset Repository.

## Overview

Materials use **TPH (Table Per Hierarchy)** inheritance with a `Discriminator` column. Each material type is a class that inherits from `Material`.

## Files to Modify

When creating/updating a material type, these files need changes:

| File | Purpose |
|------|---------|
| `Models/Material.cs` | Entity class and `Type` enum value (append at the end — see warning in step 1) |
| `Data/XR50DbContext.cs` | TPH discriminator, property/column mappings, side-table relationships+indexes |
| `Services/Materials/MaterialService.cs` | `SetMaterialTypeFromClass()` switch (maps class → enum) |
| `Services/Materials/I{Name}MaterialService.cs` | Service interface |
| `Services/Materials/{Name}MaterialService.cs` | Service implementation |
| `Controllers/XR50MaterialsController.cs` | **Several** dispatch points — see step 6 (it's easy to miss one) |
| `Services/XR50ManualTableCreator.cs` | Tenant-DB columns + side tables + idempotent ALTER migration |
| `Services/XR50MigrationService.cs` | Wire the new migration into per-tenant provisioning |
| `Program.cs` | DI service registration (+ Swagger group if a new controller) |
| `tests/.../Factories/MaterialFactory.cs` | Test payload builder |
| `tests/.../Services/{Name}MaterialTests.cs` | Hermetic xUnit test (InMemory — see Testing) |

**External-API-backed types** (RAG/chatbot, e.g. `ai_assistant`, `innov_chatbot`) additionally touch:
`Models/XR50Tenant.cs` + `Models/DTOs/XR50TenantDtos.cs` + `Controllers/XR50TenantController.cs` +
`Services/XR50TenantManagementService.cs` (per-tenant connection config), a `Services/Chatbot/`
provider, a dedicated controller, and a per-(material, asset) job table. See
[Materials with External API Integration](#materials-with-external-api-integration).

## Step-by-Step Guide

### 1. Add to MaterialType Enum (`Models/Material.cs`)

```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Type
{
    // ... existing types ...
    [EnumMember(Value = "new_type")]  // Use snake_case for JSON serialization
    NewType
}
```

> ⚠️ **Append new enum values at the END.** The `Type` column stores the enum's **ordinal int**
> (`Materials.Type` is `int`). Inserting a value in the middle shifts every later value's number and
> silently corrupts existing rows. Always add to the end.
>
> Note: `JsonStringEnumConverter` does **not** honor `[EnumMember]` — direct entity serialization
> emits the C# name (e.g. `"NewType"`/camelCased), while detail responses go through
> `GetLowercaseType()` which returns the snake_case value (see step 6). Add your type there too.

### 2. Create Material Class (`Models/Material.cs`)

```csharp
/// <summary>
/// Description of the new material type.
/// </summary>
public class NewTypeMaterial : Material
{
    // Type-specific properties
    public string? SomeProperty { get; set; }
    public int? SomeNumericProperty { get; set; }

    // For related entities (one-to-many)
    public List<ChildEntity>? Children { get; set; }

    public NewTypeMaterial()
    {
        Children = new List<ChildEntity>();
        Type = Type.NewType;  // Set discriminator
    }
}
```

### 3. Update DbContext (`Data/XR50DbContext.cs`)

**Add discriminator value:**
```csharp
modelBuilder.Entity<Material>()
    .HasDiscriminator<string>("Discriminator")
    // ... existing values ...
    .HasValue<NewTypeMaterial>("NewTypeMaterial");
```

**Add property mappings:**
```csharp
modelBuilder.Entity<NewTypeMaterial>()
    .Property(m => m.SomeProperty)
    .HasColumnName("SomeProperty");
```

**Add relationships (if needed):**
```csharp
modelBuilder.Entity<NewTypeMaterial>()
    .HasMany(m => m.Children)
    .WithOne()
    .HasForeignKey("NewTypeMaterialId")
    .OnDelete(DeleteBehavior.Cascade);
```

### 4. Create Service Interface (`Services/Materials/INewTypeMaterialService.cs`)

```csharp
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Services.Materials
{
    public interface INewTypeMaterialService
    {
        // CRUD Operations
        Task<IEnumerable<NewTypeMaterial>> GetAllAsync();
        Task<NewTypeMaterial?> GetByIdAsync(int id);
        Task<NewTypeMaterial> CreateAsync(NewTypeMaterial material);
        Task<NewTypeMaterial> UpdateAsync(NewTypeMaterial material);
        Task<bool> DeleteAsync(int id);

        // Type-specific operations (if needed)
        Task<NewTypeMaterial> CreateWithChildrenAsync(NewTypeMaterial material, List<ChildEntity> children);
    }
}
```

### 5. Create Service Implementation (`Services/Materials/NewTypeMaterialService.cs`)

```csharp
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    public class NewTypeMaterialService : INewTypeMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<NewTypeMaterialService> _logger;

        public NewTypeMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<NewTypeMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        public async Task<IEnumerable<NewTypeMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<NewTypeMaterial>()
                .ToListAsync();
        }

        public async Task<NewTypeMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<NewTypeMaterial>()
                .FirstOrDefaultAsync(m => m.id == id);
        }

        public async Task<NewTypeMaterial> CreateAsync(NewTypeMaterial material)
        {
            using var context = _dbContextFactory.CreateDbContext();

            material.Created_at = DateTime.UtcNow;
            material.Updated_at = DateTime.UtcNow;
            material.Type = MaterialType.NewType;

            context.Materials.Add(material);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created NewType material: {Name} with ID: {Id}",
                material.Name, material.id);

            return material;
        }

        public async Task<NewTypeMaterial> UpdateAsync(NewTypeMaterial material)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var existing = await context.Materials
                .OfType<NewTypeMaterial>()
                .FirstOrDefaultAsync(m => m.id == material.id);

            if (existing == null)
            {
                throw new KeyNotFoundException($"NewType material {material.id} not found");
            }

            // Preserve immutable fields
            material.Created_at = existing.Created_at;
            material.Unique_id = existing.Unique_id;
            material.Type = MaterialType.NewType;
            material.Updated_at = DateTime.UtcNow;

            context.Entry(existing).CurrentValues.SetValues(material);
            await context.SaveChangesAsync();

            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var material = await context.Materials
                .OfType<NewTypeMaterial>()
                .FirstOrDefaultAsync(m => m.id == id);

            if (material == null) return false;

            // Clean up relationships
            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            context.Materials.Remove(material);
            await context.SaveChangesAsync();

            return true;
        }
    }
}
```

### 6. Update XR50MaterialsController

**Add service injection:**
```csharp
private readonly INewTypeMaterialService _newTypeMaterialService;

public materialsController(
    // ... existing services ...
    INewTypeMaterialService newTypeMaterialService,
    // ...
)
{
    _newTypeMaterialService = newTypeMaterialService;
}
```

**Add to GetMaterialDetails switch:**
```csharp
MaterialType.NewType => await GetNewTypeDetails(id),
```

**Add GetNewTypeDetails method:**
```csharp
private async Task<object?> GetNewTypeDetails(int materialId)
{
    var material = await _newTypeMaterialService.GetByIdAsync(materialId);
    if (material == null) return null;

    var related = await GetRelatedMaterialsAsync(materialId);

    return new
    {
        id = material.id.ToString(),
        Name = material.Name,
        Description = material.Description,
        Type = GetLowercaseType(material.Type),
        Unique_id = material.Unique_id,
        Created_at = material.Created_at,
        Updated_at = material.Updated_at,
        SomeProperty = material.SomeProperty,
        Related = related
    };
}
```

**Add to the create-type switch — there are TWO (don't miss the second):**
- `PostMaterial` (multipart/form-data path)
- `PostMaterialJson` (`POST /materials/json`)

```csharp
"new_type" => await CreateNewTypeFromJson(tenantName, materialData),
```

**Add `"new_type"` to the `ValidMaterialTypes` HashSet** (controller validation) — otherwise the
type is rejected as invalid before dispatch.

**Add CreateNewTypeFromJson method:**
```csharp
private async Task<ActionResult<CreateMaterialResponse>> CreateNewTypeFromJson(
    string tenantName, JsonElement jsonElement)
{
    try
    {
        _logger.LogInformation("Creating NewType material from JSON");

        var material = new NewTypeMaterial();

        if (TryGetPropertyCaseInsensitive(jsonElement, "name", out var nameProp))
            material.Name = nameProp.GetString();

        if (TryGetPropertyCaseInsensitive(jsonElement, "description", out var descProp))
            material.Description = descProp.GetString();

        if (TryGetPropertyCaseInsensitive(jsonElement, "someProperty", out var someProp))
            material.SomeProperty = someProp.GetString();

        var createdMaterial = await _newTypeMaterialService.CreateAsync(material);

        await ProcessRelatedMaterialsAsync(createdMaterial.id, jsonElement);

        return CreatedAtAction(nameof(GetMaterial),
            new { tenantName, id = createdMaterial.id },
            new CreateMaterialResponse
            {
                Status = "success",
                Message = "NewType material created successfully",
                id = createdMaterial.id,
                Name = createdMaterial.Name,
                Type = GetLowercaseType(createdMaterial.Type),
                Created_at = createdMaterial.Created_at
            });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating NewType material from JSON");
        throw;
    }
}
```

**Add to `GetLowercaseType()`** (the detail responses use it; without your case it falls through to
`"default"` and the `type` field comes back wrong):
```csharp
XR50TrainingAssetRepo.Models.Type.NewType => "new_type",
```

**Add to the entity factory in `ParseMaterialFromJson()`** (used by the basic/relationship paths):
```csharp
("newtypematerial", _) or (_, "new_type") => new NewTypeMaterial(),
```

**If numeric `type` values are supported**, add your ordinal to BOTH numeric→string maps in
`ParseMaterialFromJson` (the `type` map and the `materialType` map):
```csharp
13 => "new_type",   // use the enum ordinal from step 1
```

> ⚠️ **Easy-to-miss controller checklist** (each is a separate edit): create switch ×2,
> `ValidMaterialTypes`, `GetCompleteMaterialDetails` switch + `GetNewTypeDetails`, `GetLowercaseType`,
> `ParseMaterialFromJson` factory switch (+ numeric maps). Missing the detail switch or
> `GetLowercaseType` is the most common slip — grep the controller for an existing type
> (e.g. `ai_assistant`) and mirror **every** hit.

### 7. Update Manual Table Creator (`Services/XR50ManualTableCreator.cs`)

**Add columns to CREATE TABLE statement:**
```sql
-- NewType Material specific columns
`SomeProperty` varchar(255) DEFAULT NULL,
`SomeNumericProperty` int DEFAULT NULL,
```

> Tenant databases are created/upgraded by **`XR50ManualTableCreator` (raw SQL)**, NOT by
> `dotnet ef migrations`. Adding columns to the `Materials` CREATE block only helps **fresh**
> tenants — existing tenant DBs need an idempotent `ALTER`. Always do both.

**For an existing tenant DB, add an idempotent column migration** (mirror
`MigrateInnovChatbotColumnsAsync` — check `INFORMATION_SCHEMA.COLUMNS` then `ALTER TABLE ... ADD COLUMN`):
```csharp
public async Task<bool> MigrateNewTypeColumnsAsync(string tenantName) { /* check-then-ALTER per column */ }
```

**Wire the migration in BOTH places** so provisioning and lab-purge/repair converge:
1. `XR50ManualTableCreator.CreateAllTablesAsync()` — call it after `CreateTablesInDatabaseAsync`.
2. `XR50MigrationService.CreateTenantDatabaseAsync()` — call it alongside the other `Migrate*` calls.
   Also add it to the `IXR50ManualTableCreator` interface.

**Side tables** (e.g. a per-(material, asset) job table): add a `CREATE TABLE IF NOT EXISTS` to
`GetCreateTableStatements()` AND a `MigrateXxxTableAsync` for existing DBs.

> ⚠️ **MySQL identifier limit is 64 chars.** Foreign-key / index names like
> `FK_VeryLongMaterialName_Materials_VeryLongMaterialId` can exceed it and fail table creation with
> a non-obvious error (this is invisible to the InMemory tests — only Layer 3 / a real MySQL catches
> it). Keep constraint/index names short.

### 8. Register Service (`Program.cs`)

```csharp
services.AddScoped<INewTypeMaterialService, NewTypeMaterialService>();
```

## Checklist

- [ ] MaterialType enum updated
- [ ] Material class created with constructor setting Type
- [ ] DbContext discriminator added
- [ ] DbContext property mappings added
- [ ] Service interface created
- [ ] Service implementation created
- [ ] Controller service injection added
- [ ] Controller GetDetails method added
- [ ] Controller CreateFromJson method added
- [ ] Controller switch cases updated (GetMaterialDetails, CreateFromJson, GetMaterialTypeClass)
- [ ] Manual table creator columns added
- [ ] Program.cs DI registration added
- [ ] Build succeeds with `dotnet build`

## Common Patterns

### Multi-Asset Materials (like AIAssistantMaterial)
- Store asset IDs as JSON: `public string? AssetIds { get; set; }`
- Add helper methods: `GetAssetIdsList()`, `SetAssetIdsList()`

### Materials with Child Entities (like ChecklistMaterial)
- Define child entity class with foreign key
- Use `HasMany().WithOne().HasForeignKey().OnDelete(Cascade)`
- Handle children in Create/Update/Delete methods

### Materials with External API Integration

Two live examples: `ai_assistant` (DataLens backend) and `innov_chatbot` (INNOV "LLM Engine"). They
are **separate material types** behind a shared `IChatbotProvider` seam (`Services/Chatbot/`), not one
type with a provider flag. Reuse this pattern:

- **Provider seam** (`Services/Chatbot/IChatbotProvider.cs`): a backend-neutral interface
  (ingest / status / chat / health). Implement it natively for the new backend; existing backends can
  be adapted (see `DataLensChatbotProvider` — a thin adapter that doesn't change DataLens behaviour).
  Register each as `IChatbotProvider` in `Program.cs`; the material service resolves the right one by
  `ProviderKey`.
- **Per-(material, asset) job table** (e.g. `AIAssistantMaterialAssetJob`,
  `InnovChatbotMaterialAssetJob`): tracks ingest state per (material, asset) so the same Asset can be
  ingested into different collections/pilots independently. Aggregate the material's status
  (`notready`/`process`/`ready`) from these rows. Add it as a side table (see step 7).
- **Per-tenant connection config** on `XR50Tenant` (base URL + token + default collection/pilot).
  The API **token is a secret**: store it in the tenant registry SQL
  (`XR50TenantManagementService` + `XR50MigrationService` CREATE/SELECT/INSERT/UPDATE) but **never**
  return it — expose a boolean `...Configured` in `TenantResponse` instead. Mirror exactly how
  `DefaultAICollection` / `InnovChatbot*` flow end-to-end.
- **Background status sync**: only needed when ingestion is async with job polling (DataLens —
  `AiStatusSyncService`). INNOV's upload is synchronous, so no poller. Match the backend.
- **Graceful transport errors**: catch `HttpRequestException`/`TaskCanceledException` in the client
  and rethrow as `InvalidOperationException` so the controller returns a clean `400`, not a `500`
  (an unreachable backend should never produce an unhandled 500).

## Testing

> **Preferred: hermetic xUnit tests.** The xUnit suite runs on **EF Core InMemory +
> `MockStorageService`** — no MySQL, MinIO, or Keycloak, and **no env vars / connection strings**.
> Don't follow the "set `ConnectionStrings__DefaultConnection` / AWS vars" guidance below for the
> hermetic suite (that's only relevant if you deliberately point tests at real infra). For
> service-logic tests, construct the service directly with a fake provider and an InMemory
> `IXR50TenantDbContextFactory` — see `tests/.../Services/AIAssistantMaterialUpdateTests.cs` and
> `InnovChatbotMaterialTests.cs`. Live (Jest/probe) verification is Layer 3/4 of the **Autonomous
> Test Loop** in `CLAUDE.md` and needs the docker-compose `sandbox` stack.
>
> Note: `SubcomponentRelatedMaterialsTests` is a **known-red baseline** (currently 12 failing on a
> test/API contract mismatch). A clean run is "those 12 failed, everything else passed" — don't treat
> them as a regression you caused.
>
> Capturing results on Windows: the VSTest console output isn't always captured by the Bash tool —
> run the test csproj explicitly with a TRX logger
> (`dotnet test <proj> --logger "trx;LogFileName=run.trx"`) and read the `.trx`.

### Test Project Structure

```
tests/XR50TrainingAssetRepo.Tests/
├── Factories/
│   └── MaterialFactory.cs      # Test data factories
├── Fixtures/
│   └── WebApplicationFixture.cs # Integration test setup
├── Integration/
│   └── *Tests.cs               # Integration tests
└── Smoke/
    └── HealthCheckTests.cs     # Smoke tests
```

### 9. Add Factory Method (`tests/.../Factories/MaterialFactory.cs`)

```csharp
/// <summary>
/// Creates a NewType material for testing.
/// </summary>
public static object CreateNewTypeRequest(
    string? name = null,
    string? someProperty = null)
{
    return new
    {
        name = name ?? $"Test NewType {Guid.NewGuid():N}",
        description = "Test NewType material",
        type = "new_type",  // Must match enum's EnumMember value
        someProperty = someProperty ?? "default value"
    };
}
```

### 10. Create Integration Tests (`tests/.../Integration/NewTypeMaterialTests.cs`)

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using XR50TrainingAssetRepo.Tests.Factories;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Integration;

[Collection("WebApplication")]
public class NewTypeMaterialTests : IClassFixture<WebApplicationFixture>
{
    private readonly HttpClient _client;
    private readonly string _tenantName = "test-tenant";

    public NewTypeMaterialTests(WebApplicationFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task CreateNewTypeMaterial_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = MaterialFactory.CreateNewTypeRequest(
            name: "Test NewType Material",
            someProperty: "test value"
        );

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/{_tenantName}/materials/json",
            request
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(content);

        result.RootElement.GetProperty("status").GetString().Should().Be("success");
        result.RootElement.GetProperty("Type").GetString().Should().Be("new_type");
    }

    [Fact]
    public async Task GetNewTypeMaterial_WhenExists_ReturnsWithDetails()
    {
        // Arrange - Create material first
        var createRequest = MaterialFactory.CreateNewTypeRequest();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/{_tenantName}/materials/json",
            createRequest
        );
        var createResult = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync()
        );
        var materialId = createResult.RootElement.GetProperty("id").GetInt32();

        // Act
        var response = await _client.GetAsync(
            $"/api/{_tenantName}/materials/{materialId}"
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(content);

        result.RootElement.GetProperty("Type").GetString().Should().Be("new_type");
        result.RootElement.TryGetProperty("SomeProperty", out _).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteNewTypeMaterial_WhenExists_ReturnsNoContent()
    {
        // Arrange - Create material first
        var createRequest = MaterialFactory.CreateNewTypeRequest();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/{_tenantName}/materials/json",
            createRequest
        );
        var createResult = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync()
        );
        var materialId = createResult.RootElement.GetProperty("id").GetInt32();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/{_tenantName}/materials/{materialId}"
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await _client.GetAsync(
            $"/api/{_tenantName}/materials/{materialId}"
        );
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

### Running Tests

#### Command Line (All Platforms)

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~NewTypeMaterialTests"

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run only integration tests
dotnet test --filter "Category=Integration"

# Run a single test method
dotnet test --filter "FullyQualifiedName~NewTypeMaterialTests.CreateNewTypeMaterial_WithValidData_ReturnsCreated"
```

#### Windows Command Prompt (cmd.exe)

```cmd
REM Set environment variables for test database
set ConnectionStrings__DefaultConnection=Server=localhost;Database=xr50_test;User=root;Password=yourpassword
set STORAGE_TYPE=S3
set AWS_ACCESS_KEY_ID=minioadmin
set AWS_SECRET_ACCESS_KEY=minioadmin
set AWS_REGION=us-east-1
set S3_ENDPOINT=http://localhost:9000

REM Run tests
dotnet test

REM Run tests with specific configuration
dotnet test --configuration Debug

REM Run tests and generate code coverage
dotnet test --collect:"XPlat Code Coverage"
```

#### Windows PowerShell

```powershell
# Set environment variables for test database
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=xr50_test;User=root;Password=yourpassword"
$env:STORAGE_TYPE = "S3"
$env:AWS_ACCESS_KEY_ID = "minioadmin"
$env:AWS_SECRET_ACCESS_KEY = "minioadmin"
$env:AWS_REGION = "us-east-1"
$env:S3_ENDPOINT = "http://localhost:9000"

# Run tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run tests and stop on first failure
dotnet test -- xUnit.FailFast=true

# View environment variables (for debugging)
Get-ChildItem Env: | Where-Object { $_.Name -like "*Connection*" -or $_.Name -like "*AWS*" }
```

#### Linux/macOS (Bash)

```bash
# Set environment variables for test database
export ConnectionStrings__DefaultConnection="Server=localhost;Database=xr50_test;User=root;Password=yourpassword"
export STORAGE_TYPE=S3
export AWS_ACCESS_KEY_ID=minioadmin
export AWS_SECRET_ACCESS_KEY=minioadmin
export AWS_REGION=us-east-1
export S3_ENDPOINT=http://localhost:9000

# Run tests
dotnet test
```

### Environment Variables Reference

| Variable | Description | Example |
|----------|-------------|---------|
| `ConnectionStrings__DefaultConnection` | Database connection string | `Server=localhost;Database=xr50_test;User=root;Password=pass` |
| `STORAGE_TYPE` | Storage backend (`S3` or `OwnCloud`) | `S3` |
| `AWS_ACCESS_KEY_ID` | S3/MinIO access key | `minioadmin` |
| `AWS_SECRET_ACCESS_KEY` | S3/MinIO secret key | `minioadmin` |
| `AWS_REGION` | AWS region (use `us-east-1` for MinIO) | `us-east-1` |
| `S3_ENDPOINT` | Custom S3 endpoint (for MinIO) | `http://localhost:9000` |
| `OWNCLOUD_URL` | OwnCloud server URL | `http://localhost:8080` |
| `OWNCLOUD_USERNAME` | OwnCloud username | `admin` |
| `OWNCLOUD_PASSWORD` | OwnCloud password | `admin` |
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Development` |

### Using launchSettings.json

For consistent environment setup, use `Properties/launchSettings.json`:

**File: `tests/XR50TrainingAssetRepo.Tests/Properties/launchSettings.json`**

```json
{
  "profiles": {
    "XR50TrainingAssetRepo.Tests": {
      "commandName": "Project",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ConnectionStrings__DefaultConnection": "Server=localhost;Database=xr50_test;User=root;Password=yourpassword",
        "STORAGE_TYPE": "S3",
        "AWS_ACCESS_KEY_ID": "minioadmin",
        "AWS_SECRET_ACCESS_KEY": "minioadmin",
        "AWS_REGION": "us-east-1",
        "S3_ENDPOINT": "http://localhost:9000"
      }
    }
  }
}
```

### Debugging Tests

#### Visual Studio (Windows)

1. **Open Test Explorer**: `Test` > `Test Explorer` (or `Ctrl+E, T`)

2. **Run tests in Debug mode**:
   - Right-click on a test or test class
   - Select **"Debug"** (not "Run")
   - Or select test and press `Ctrl+R, Ctrl+T`

3. **Set breakpoints**:
   - Click in the left margin of any code line
   - Or press `F9` on the line

4. **Configure environment variables**:
   - Right-click project > **Properties**
   - Go to **Debug** > **General** > **Open debug launch profiles UI**
   - Add environment variables in the UI

5. **View Debug Output**:
   - `Debug` > `Windows` > `Output`
   - Select "Tests" in the dropdown

#### Visual Studio Code (Cross-Platform)

1. **Install C# Dev Kit extension** (if not already installed)

2. **Create/update `.vscode/launch.json`**:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Debug Tests",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/tests/XR50TrainingAssetRepo.Tests/bin/Debug/net8.0/XR50TrainingAssetRepo.Tests.dll",
            "args": [],
            "cwd": "${workspaceFolder}/tests/XR50TrainingAssetRepo.Tests",
            "console": "internalConsole",
            "stopAtEntry": false,
            "env": {
                "ASPNETCORE_ENVIRONMENT": "Development",
                "ConnectionStrings__DefaultConnection": "Server=localhost;Database=xr50_test;User=root;Password=yourpassword",
                "STORAGE_TYPE": "S3",
                "AWS_ACCESS_KEY_ID": "minioadmin",
                "AWS_SECRET_ACCESS_KEY": "minioadmin",
                "AWS_REGION": "us-east-1",
                "S3_ENDPOINT": "http://localhost:9000"
            }
        }
    ]
}
```

3. **Create `.vscode/tasks.json`** (if not exists):

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": [
                "build",
                "${workspaceFolder}/tests/XR50TrainingAssetRepo.Tests/XR50TrainingAssetRepo.Tests.csproj",
                "/property:GenerateFullPaths=true",
                "/consoleloggerparameters:NoSummary"
            ],
            "problemMatcher": "$msCompile"
        }
    ]
}
```

4. **Run tests with debugging**:
   - Open Testing sidebar (flask icon)
   - Click the debug icon next to a test
   - Or use `F5` after selecting a test

#### JetBrains Rider (Cross-Platform)

1. **Open Unit Tests window**: `View` > `Tool Windows` > `Unit Tests`

2. **Debug a test**:
   - Right-click test > **Debug**
   - Or select test and press `Ctrl+D, D`

3. **Configure environment variables**:
   - `Run` > `Edit Configurations`
   - Select your test configuration
   - Add variables in **Environment variables** field

### Debugging Tips

#### Enable Detailed Logging

Add to your test setup or `WebApplicationFixture`:

```csharp
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddConsole();
```

#### View SQL Queries

Enable EF Core query logging:

```csharp
builder.Services.AddDbContext<XR50TrainingContext>(options =>
{
    options.UseMySql(connectionString, serverVersion)
           .EnableSensitiveDataLogging()  // Shows parameter values
           .EnableDetailedErrors()         // More detailed errors
           .LogTo(Console.WriteLine, LogLevel.Information);  // Log queries
});
```

#### Capture HTTP Traffic

Add request/response logging in tests:

```csharp
[Fact]
public async Task DebugApiCall()
{
    var request = MaterialFactory.CreateNewTypeRequest();

    // Log request
    var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine($"REQUEST:\n{requestJson}");

    var response = await _client.PostAsJsonAsync($"/api/{_tenantName}/materials/json", request);

    // Log response
    var responseContent = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"RESPONSE ({response.StatusCode}):\n{responseContent}");

    response.StatusCode.Should().Be(HttpStatusCode.Created);
}
```

### Troubleshooting

#### Common Issues on Windows

| Issue | Solution |
|-------|----------|
| "Connection refused" to database | Ensure MySQL/MariaDB service is running: `net start mysql` |
| "Access denied" to database | Check credentials in connection string |
| S3/MinIO connection failed | Verify MinIO is running: `docker ps` or check services |
| Tests hang indefinitely | Check for async deadlocks; ensure `await` is used correctly |
| Environment variables not applied | Restart terminal/IDE after setting variables |

#### Verify Environment Variables (Windows)

```powershell
# PowerShell - Check if variables are set
$env:ConnectionStrings__DefaultConnection
$env:STORAGE_TYPE

# If empty, they weren't set in current session
```

```cmd
REM Command Prompt - Check if variables are set
echo %ConnectionStrings__DefaultConnection%
echo %STORAGE_TYPE%
```

#### Reset Test Database

```bash
# Drop and recreate test database
mysql -u root -p -e "DROP DATABASE IF EXISTS xr50_test; CREATE DATABASE xr50_test;"

# Or with Docker
docker exec -it mysql-container mysql -u root -p -e "DROP DATABASE IF EXISTS xr50_test; CREATE DATABASE xr50_test;"
```

### Test Checklist

- [ ] Factory method added to `MaterialFactory.cs`
- [ ] CRUD integration tests created
- [ ] Tests verify correct `type` value in responses
- [ ] Tests verify type-specific properties are returned
- [ ] Tests pass with `dotnet test`

## Complete Checklist

### Model Layer
- [ ] MaterialType enum updated with `[EnumMember(Value = "type_name")]`
- [ ] Material class created inheriting from `Material`
- [ ] Constructor sets `Type = Type.NewType`

### Database Layer
- [ ] DbContext discriminator added (`.HasValue<NewTypeMaterial>("NewTypeMaterial")`)
- [ ] DbContext property mappings added (`.HasColumnName()`)
- [ ] DbContext relationships configured (if has children)
- [ ] Manual table creator columns added to CREATE TABLE
- [ ] Migration method added (if updating existing DBs)

### Service Layer
- [ ] Service interface created (`INewTypeMaterialService`)
- [ ] Service implementation created (`NewTypeMaterialService`)
- [ ] Service registered in `Program.cs`

### Controller Layer
- [ ] Service injected in controller constructor
- [ ] `GetNewTypeDetails` method added
- [ ] `CreateNewTypeFromJson` method added
- [ ] Switch cases updated (GetMaterialDetails, CreateFromJson, GetMaterialTypeClass)

### Testing
- [ ] Factory method added
- [ ] Integration tests created
- [ ] All tests pass

### Final Verification
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
- [ ] Swagger shows new type in dropdown
- [ ] API accepts and returns new type correctly
