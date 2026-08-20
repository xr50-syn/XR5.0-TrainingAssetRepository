# XR5.0 Training Asset Repository - Technical Documentation

## Table of Contents
1. [Project Overview](#project-overview)
2. [System Architecture](#system-architecture)
3. [Multi-Tenancy Model](#multi-tenancy-model)
4. [API Reference](#api-reference)
5. [Data Models](#data-models)
6. [Storage Backends](#storage-backends)
7. [Development Guide](#development-guide)
8. [Testing Framework](#testing-framework)
9. [Known Limitations](#known-limitations)
10. [Future Development](#future-development)

---

## Project Overview

### Research Context
The XR5.0 Training Asset Repository is a research prototype developed as part of the **Horizon Europe XR5.0 project** (Grant Agreement No. 101135209). This system serves as a multi-tenant, cloud-agnostic storage platform for Extended Reality (XR) training materials.

### Primary Objectives
- **Multi-tenant asset management** for XR training content
- **Storage backend abstraction** supporting S3, OwnCloud, and MinIO
- **RESTful API** for integration with XR training platforms
- **Dynamic database provisioning** per tenant
- **Secure asset sharing** and access control

### Technology Stack
- **Backend**: ASP.NET Core 8.0 (C#)
- **Database**: MySQL/MariaDB with Entity Framework Core
- **Storage**: AWS S3, OwnCloud, MinIO (S3-compatible)
- **Documentation**: OpenAPI/Swagger
- **Containerization**: Docker with multi-profile support

---

## System Architecture

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     XR5.0 Training Platform                │
│                    (External Client Systems)               │
└─────────────────────┬───────────────────────────────────────┘
                      │ REST API
┌─────────────────────▼───────────────────────────────────────┐
│                  XR50 Training Repository                  │
│  ┌───────────────┐  ┌──────────────┐  ┌──────────────────┐ │
│  │  Controllers  │  │   Services   │  │   Data Layer     │ │
│  │               │  │              │  │                  │ │
│  │ • Assets      │  │ • Asset      │  │ • XR50DbContext  │ │
│  │ • Materials   │  │ • Material   │  │ • Migrations     │ │
│  │ • Tenants     │  │ • Tenant     │  │ • Models         │ │
│  │ • Programs    │  │ • Storage    │  │                  │ │
│  │ • Paths       │  │ • Migration  │  │                  │ │
│  │ • Users       │  │              │  │                  │ │
│  └───────────────┘  └──────────────┘  └──────────────────┘ │
└─────────────┬───────────────────────────┬───────────────────┘
              │                           │
              ▼                           ▼
┌─────────────────────┐    ┌──────────────────────────────────┐
│   Storage Backends  │    │        Database Layer            │
│                     │    │                                  │
│ ┌─────────────────┐ │    │ ┌──────────────┐ ┌──────────────┐│
│ │   AWS S3        │ │    │ │ Admin DB     │ │ Tenant DBs   ││
│ │                 │ │    │ │              │ │              ││
│ │ • Prod buckets  │ │    │ │ • Tenants    │ │ • Assets     ││
│ │ • Multi-region  │ │    │ │ • Users      │ │ • Materials  ││
│ └─────────────────┘ │    │ │ • Config     │ │ • Programs   ││
│                     │    │ │              │ │ • Paths      ││
│ ┌─────────────────┐ │    │ └──────────────┘ │ • Metadata   ││
│ │   OwnCloud      │ │    │                  │              ││
│ │                 │ │    │                  └──────────────┘│
│ │ • Lab/dev env   │ │    │                                  │
│ │ • WebDAV API    │ │    │                                  │
│ │ • Self-hosted   │ │    │                                  │
│ └─────────────────┘ │    └──────────────────────────────────┘
│                     │
│ ┌─────────────────┐ │
│ │   MinIO         │ │
│ │                 │ │
│ │ • Local testing │ │
│ │ • S3-compatible │ │
│ │ • Development   │ │
│ └─────────────────┘ │
└─────────────────────┘
```

### Component Architecture

#### **Controller Layer**
- **XR50AssetController**: Asset upload, download, sharing operations
- **XR50MaterialsController**: Training material management (videos, documents, workflows)
- **XR50TenantController**: Tenant provisioning and management
- **XR50programController**: Training program lifecycle
- **XR50LearningPathController**: Learning path creation and assignment
- **XR50UserController**: User management within tenants

#### **Service Layer**
- **IStorageService**: Abstract storage interface with S3/OwnCloud implementations
- **IXR50TenantManagementService**: Dynamic tenant database management
- **XR50MigrationService**: Tenant database lifecycle (create, migrate, register, drop)
- **IXR50SchemaMigrator**: Applies the committed EF Core migrations to every database, adopting pre-migration ones
- **Asset/Material/Program Services**: Business logic implementation

#### **Data Layer**
- **XR50TrainingContext**: Entity Framework context with dynamic connection strings
- **XR50TenantDbContextFactory**: Per-tenant database context creation
- **Migrations** (`Migrations/Training`, `Migrations/Registry`): the committed schema, one stream per DbContext

---

## Multi-Tenancy Model

### Architecture Pattern
The system implements a **Database-per-Tenant** pattern with shared application infrastructure:

```
Base database (magical_library, set by XR50_REPO_DB_NAME)
├── XR50TenantRegistry (tenant configurations; XR50RegistryContext)
└── the full tenant schema, serving the "default" tenant (XR50TrainingContext)

Tenant Databases (xr50_tenant_[name])
├── Assets
├── Materials  
├── TrainingPrograms
├── LearningPaths
├── Users (tenant-specific)
└── Associations/Relationships
```

### Tenant Isolation
- **Database**: Each tenant has a dedicated MySQL database
- **Storage**: Isolated storage containers (S3 buckets/OwnCloud directories)
- **Users**: Tenant-scoped user management with global admin oversight
- **API Access**: Tenant-aware routing via path parameters

### Dynamic Provisioning
1. **Tenant Creation Request** → Validation
2. **Database Creation** → Schema migration
3. **Storage Provisioning** → Bucket/directory creation
4. **User Setup** → Owner account creation
5. **Configuration Storage** → Admin database update

---

## API Reference

### API Structure
The API follows RESTful conventions with tenant-scoped endpoints:

```
Base URL: /api/{tenantName}/
```

### Endpoint Categories

#### **1. Tenant Management** (`/xr50/trainingAssetRepository/tenants`)

Tenant management is the one group that is **not** under `/api/{tenantName}/` - it operates on
tenants rather than within one.

- `POST /` - Create a new tenant with storage provisioning
- `GET /` - List all tenants (system admin only)
- `GET /{tenantName}` - Get tenant details
- `DELETE /{tenantName}` - Remove tenant and all data
- `PUT /{tenantName}/hub-tenant` - Rebind the tenant to an XR5.0 Hub tenant id
- `GET /{tenantName}/validate-storage` - Check the tenant's storage configuration
- `GET /{tenantName}/storage-stats` - Storage usage for the tenant
- `GET /examples/create-requests` - Sample creation payloads per storage type

#### **2. Training Program Management** (`/api/{tenant}/programs/`)
- `GET /` - List training programs
- `POST /` - Create training program
- `GET /{id}` - Get program details with learning paths
- `PUT /{id}` - Update program
- `DELETE /{id}` - Delete program

#### **3. Learning Path Management** (`/api/{tenant}/learningpaths/`)
- `GET /` - List learning paths
- `POST /` - Create learning path with materials
- `GET /{id}` - Get path with material sequence
- `PUT /{id}` - Update path structure
- `DELETE /{id}` - Remove learning path

#### **4. Material Management** (`/api/{tenant}/materials/`)
- `GET /` - List materials with filtering
- `POST /` - Create material (document, video, workflow)
- `GET /{id}` - Get material with metadata
- `PUT /{id}` - Update material properties
- `DELETE /{id}` - Remove material
- **Specialized endpoints**:
  - `POST /video/{id}/timestamps` - Add video timestamps
  - `POST /checklist/{id}/entries` - Add checklist items
  - `POST /workflow` - Create complete workflows

#### **5. Asset Management** (`/api/{tenant}/assets/`)
- `POST /` - Upload file assets
- `GET /{id}/download` - Download asset files
- `GET /{id}/file-info` - Get file metadata
- `POST /{id}/share` - Create sharing links (OwnCloud only)
- `GET /shares` - List tenant shares
- `DELETE /shares/{id}` - Revoke shares

Uploaded files are deduplicated within each tenant by their SHA-256 content hash. The first
`POST /api/{tenant}/assets` returns `201 Created`; a later byte-identical upload returns the
existing asset with `200 OK` and `reused: true`. Filenames and other multipart metadata do not
affect identity. Reference-only assets are not content-hashed.

`POST /api/{tenant}/assets/{id}/upload` attaches a file to an existing reference-only asset. It
hashes what it stores like any other upload, so the content participates in deduplication. Because
it targets one specific asset there is no duplicate to silently reuse: content another asset already
holds is refused with `409 Conflict` naming that asset.

Existing tenant databases receive the `ContentHash` and `StorageKey` columns when they are
migrated (at startup, or through `POST /api/troubleshooting/migrate/{tenantName}`). Existing
asset rows retain a null hash and are not backfilled, avoiding an automatic download of every
object in tenant storage.

#### **6. User Management** (`/api/{tenant}/users/`)
- `GET /` - List tenant users
- `POST /` - Create user account
- `GET /{id}` - Get user profile
- `PUT /{id}` - Update user
- `DELETE /{id}` - Remove user access

#### **7. Other endpoint groups**

Not enumerated here; see Swagger at `/swagger` for the full contract.

- `/api/{tenant}/ai-assistant` - AI Assistant materials and chat (DataLens-backed)
- `/api/{tenant}/innov-chatbot` - INNOV chatbot material type
- `/api/{tenant}/program-progress`, `/api/{tenant}/quiz-progress` - progress tracking
- `/api/auth` - token introspection (`/api/auth/me`)
- `/api/troubleshooting` - system-admin diagnostics and schema migrations (`migration-status`, `migrate/{tenant}`, `migrate-all`)

### Response Format

Successful responses return the resource directly - an object or an array - with **no wrapper
envelope**. Write endpoints may return a small status object instead, for example material
creation:

```json
{ "status": "success", "message": "Material created successfully", "id": "1", "name": "..." }
```

Note that integer ids serialize as JSON **strings** (`"1"`, not `1`). This is a deliberate
API-wide contract; clients and tests must not assume numbers.

Errors use [RFC 7807 problem details](https://datatracker.ietf.org/doc/html/rfc7807):

```json
{
  "type": "https://api.xr50/errors/resource-not-found",
  "title": "Resource not found",
  "status": "404",
  "detail": "Material 999999 was not found.",
  "instance": "/api/test_company/materials/999999",
  "traceId": "0HNNTPT4KIMFV:00000001",
  "errorCode": "resource_not_found"
}
```

`status` is a string for the same reason. `errorCode` is the stable machine-readable
discriminator; `type` and `title` are human-facing and may be reworded.

---

## Data Models

### Core Entities

#### **XR50Tenant**
```csharp
public class XR50Tenant
{
    public int Id { get; set; }
    public string TenantName { get; set; }        // Unique identifier
    public string TenantGroup { get; set; }       // Pilot grouping
    public string Description { get; set; }
    public string StorageType { get; set; }       // S3, OwnCloud, MinIO
    public string? S3BucketName { get; set; }     // AWS S3 bucket
    public string? S3BucketRegion { get; set; }
    public string? TenantDirectory { get; set; }  // OwnCloud directory
    public string? StorageEndpoint { get; set; }  // Custom endpoints
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}
```

#### **Asset**
```csharp
public class Asset
{
    public int Id { get; set; }
    public string Filename { get; set; }      // Storage filename (UUID-based)
    public string? OriginalName { get; set; }  // User-provided name
    public string? Filetype { get; set; }     // Extension/MIME category
    public string? Description { get; set; }
    public string? Src { get; set; }          // Storage URL/path
    public long? FileSize { get; set; }       // Bytes
    public DateTime UploadedAt { get; set; }
    public List<Material> Materials { get; set; } // Reverse navigation
}
```

#### **Material**
```csharp
public class Material
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }           // document, video, workflow, checklist, questionnaire
    public string? Description { get; set; }
    public int? AssetId { get; set; }         // Optional file attachment
    public Asset? Asset { get; set; }
    
    // Type-specific collections
    public List<VideoTimestamp> VideoTimestamps { get; set; }
    public List<WorkflowStep> WorkflowSteps { get; set; }
    public List<ChecklistEntry> Entries { get; set; }
    public List<QuestionnaireEntry> QuestionnaireEntries { get; set; }
}
```

#### **TrainingProgram**
```csharp
public class TrainingProgram
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<LearningPath> LearningPaths { get; set; }
}
```

#### **LearningPath**
```csharp
public class LearningPath
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public int Order { get; set; }            // Sequence within program
    public List<Material> Materials { get; set; }
    public List<TrainingProgram> TrainingPrograms { get; set; } // Many-to-many
}
```

### Relationship Patterns

#### **Many-to-Many Associations**
- **TrainingProgram ↔ LearningPath**: Programs contain multiple paths, paths can be shared
- **LearningPath ↔ Material**: Paths sequence multiple materials, materials can be reused

#### **One-to-Many Hierarchies**
- **Asset → Materials**: One file can support multiple materials
- **Material → VideoTimestamps/WorkflowSteps/etc**: Type-specific child collections

---

## Storage Backends

### Interface Abstraction
The `IStorageService` interface provides unified operations across storage types:

```csharp
public interface IStorageService
{
    // Tenant Storage Management
    Task<bool> CreateTenantStorageAsync(string tenantName, XR50Tenant tenant);
    Task<bool> DeleteTenantStorageAsync(string tenantName);
    Task<bool> TenantStorageExistsAsync(string tenantName);

    // File Operations. fileName is the storage key (see Object Layout below), not the
    // asset's display filename; downloadFileName carries the name to serve the file under.
    Task<string> UploadFileAsync(string tenantName, string fileName, IFormFile file,
                                 string? downloadFileName = null);
    Task<Stream> DownloadFileAsync(string tenantName, string fileName);
    Task<string> GetDownloadUrlAsync(string tenantName, string fileName, TimeSpan? expiration = null);
    Task<bool> DeleteFileAsync(string tenantName, string fileName);
    
    // Sharing (OwnCloud only)
    Task<string> CreateShareAsync(string tenantName, XR50Tenant tenant, Asset asset);
    Task<bool> DeleteShareAsync(string tenantName, string shareId);
    bool SupportsSharing();
    
    // Storage Info
    Task<StorageStatistics> GetStorageStatisticsAsync(string tenantName);
    string GetStorageType();
}
```

### Object Layout

An asset's file is addressed by `Asset.StorageKey`, recorded once when the file is stored and never
recomputed. Uploads set it to the content hash, so the object path is content-addressed rather than
named after the file:

```
{tenant}/{sha256}          <- what the backend stores
report.pdf                 <- Asset.Filename, what users see
```

Two properties follow, and both are load-bearing:

- **One row owns one object.** The unique index on `ContentHash` admits a single row per hash per
  tenant, so no two assets can share a key. A same-named upload cannot overwrite another asset's
  content, and deleting an asset cannot remove a file another asset still points at. The dependency
  guard in `DeleteAssetAsync` protects the row being deleted; this is what protects the file.
- **The key is immutable.** It does not depend on `Filename`, and `UpdateAssetAsync` preserves it
  alongside `ContentHash`. Renaming an asset changes only its display name, never where its bytes
  live, so a rename cannot orphan a file.

Storage keys are opaque, so the original filename travels with the upload as `downloadFileName`. S3
applies it as `Content-Disposition`, keeping downloads named `report.pdf`. WebDAV serves files under
their stored path, so OwnCloud shows the key itself.

Rows written before storage keys were recorded, and reference-only assets, have no key and fall back
to addressing by filename, which is where their files were placed. Those rows remain vulnerable to
filename collisions with each other; re-uploading moves a row onto a content-addressed key. There is
no backfill, for the same reason the hash is not backfilled.

### Implementation Details

#### **S3StorageServiceImplementation**
- **Bucket Naming**: `{prefix}-tenant-{sanitized-name}`
- **Path Style**: Forced for MinIO compatibility
- **Regions**: Configurable, defaults to eu-west-1
- **Credentials**: AWS SDK standard (env vars, IAM roles, profiles)
- **Pre-signed URLs**: 1-hour default expiration
- **Limitations**: No native sharing (uses pre-signed URLs)

#### **OwnCloudStorageServiceImplementation**  
- **Directory Structure**: `/tenant-{name}/` in OwnCloud root
- **API Access**: WebDAV for file operations, OCS API for sharing
- **Authentication**: Admin user for provisioning, tenant users for access
- **Sharing**: Full OwnCloud sharing capabilities (public links, user shares)
- **User Management**: Automatic OwnCloud user creation per tenant

### Storage Selection Logic
```csharp
// Program.cs configuration
var storageType = builder.Configuration.GetValue<string>("Storage__Type") ?? 
                  Environment.GetEnvironmentVariable("STORAGE_TYPE") ?? 
                  "OwnCloud";

if (storageType.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IStorageService, S3StorageServiceImplementation>();
}
else
{
    builder.Services.AddScoped<IStorageService, OwnCloudStorageServiceImplementation>();
}
```

---

## Development Guide

### Prerequisites
- **.NET 8.0 SDK**
- **Docker & Docker Compose**
- **MySQL/MariaDB** (or use containerized version)
- **AWS CLI** (for S3 development)
- **Git**

### Local Development Setup

#### 1. Clone and Configure
```bash
git clone <repository-url>
cd XR5.0-TrainingAssetRepository
cp .env.example .env
# Edit .env with your configuration
```

#### 2. Database Setup
```bash
# Using Docker
docker-compose --profile lab up -d mariadb

# Or local MySQL: create the base database (XR50_REPO_DB_NAME); the application creates
# its tables at startup from the committed migrations.
mysql -u root -p
CREATE DATABASE magical_library;
```

#### 3. Run Application
```bash
# Development with file watching
dotnet watch run

# Or with Docker
docker-compose --profile lab up --build
```

### Code Organization

#### **Controllers** (`/Controllers/`)
- Follow REST conventions
- Use dependency injection for services
- Implement proper error handling
- Include comprehensive logging
- Support tenant-scoped operations

#### **Services** (`/Services/`)
- Implement business logic
- Handle cross-cutting concerns (logging, validation)
- Manage external integrations (storage, database)
- Follow single responsibility principle

#### **Models** (`/Models/`)
- **Entity models**: Database-mapped classes
- **DTOs** (`/Models/DTOs/`): API request/response objects
- Use data annotations for validation
- Implement proper navigation properties

#### **Data** (`/Data/`)
- Entity Framework configuration
- Database context management
- Migration definitions
- Seed data (if applicable)

### Development Patterns

#### **Dependency Injection**
```csharp
// Service registration
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IStorageService, S3StorageServiceImplementation>();

// Controller injection
public class AssetsController : ControllerBase
{
    private readonly IAssetService _assetService;
    private readonly ILogger<AssetsController> _logger;
    
    public AssetsController(IAssetService assetService, ILogger<AssetsController> logger)
    {
        _assetService = assetService;
        _logger = logger;
    }
}
```

#### **Error Handling**
```csharp
try
{
    var result = await _service.PerformOperationAsync();
    return Ok(result);
}
catch (ValidationException ex)
{
    _logger.LogWarning("Validation failed: {Message}", ex.Message);
    return BadRequest(new { Error = ex.Message });
}
catch (NotFoundException ex)
{
    _logger.LogWarning("Resource not found: {Message}", ex.Message);
    return NotFound(new { Error = ex.Message });
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error in operation");
    return StatusCode(500, new { Error = "Internal server error" });
}
```

#### **Tenant Resolution**
```csharp
// Automatic tenant detection from route
[Route("api/{tenantName}/[controller]")]
public class BaseController : ControllerBase
{
    protected string GetTenantName()
    {
        return HttpContext.Request.RouteValues["tenantName"]?.ToString() ?? 
               throw new InvalidOperationException("Tenant name not found in route");
    }
}
```

### Database Migrations

The schema is owned by EF Core migrations committed under `Migrations/`, one stream per
DbContext, each with its own history table:

| Context | Migrations | History table | Applied to |
|---|---|---|---|
| `XR50TrainingContext` | `Migrations/Training` | `__EFMigrationsHistory` | every `xr50_tenant_*` database and the base database (the "default" tenant) |
| `XR50RegistryContext` | `Migrations/Registry` | `__EFMigrationsHistory_Registry` | the base database (`XR50TenantRegistry`) |

#### **Authoring a migration**
```bash
dotnet tool restore    # pins dotnet-ef through .config/dotnet-tools.json
dotnet build
dotnet ef migrations add <Name> --context XR50TrainingContext --output-dir Migrations/Training --no-build
# registry changes: --context XR50RegistryContext --output-dir Migrations/Registry
```
No database is needed: the design-time factories pin the server version
(`Database:ServerVersion`, default `10.11.0-mariadb`). Commit the migration with the updated
snapshot; the hermetic `MigrationModelDriftTests` fails when they disagree. Keep each
migration to one concern: MySQL DDL is not transactional, so a failed multi-statement
migration stops halfway and has to be repaired by hand.

#### **When migrations run**
- **Startup** (`Database:MigrateOnStartup`, default `true`): before Kestrel listens and before
  background services start, the registry and the training schema of the base database are
  migrated, then every active tenant in `XR50TenantRegistry`, in sequence. A failure keeps the
  application down; with `Database:TolerateTenantMigrationFailures=true` only central
  failures do. Schemas named `xr50_tenant_*` that no registry row points at are reported as
  orphans and left alone.
- **Tenant creation**: after `CREATE DATABASE`, the new database receives the full migration
  set before the tenant is registered.
- **Operator CLI**: `dotnet XR50TrainingAssetRepo.dll migrate [--status] [--all | --central |
  --tenant <name> ...] [--target <id>] [--no-adopt-legacy] [--tolerate-tenant-failures] [--json]`;
  exit codes 0 ok, 1 failed, 2 usage, 3 manual intervention. With the stack:
  `docker compose --profile sandbox run --rm --no-deps training-repo migrate --status`.
- **HTTP** (system admin): `GET /api/troubleshooting/migration-status[/{tenant}]`,
  `POST /api/troubleshooting/migrate/{tenant}` (404 unregistered, 409 manual intervention),
  `POST /api/troubleshooting/migrate-all`. `POST repair/{tenant}` creates a missing database
  and migrates it.

A server-side advisory lock (`GET_LOCK`) per database serialises concurrent appliers.

#### **States and adoption**
Each database is classified before anything is done to it:

| State | Meaning | Action |
|---|---|---|
| `Empty` | no model tables, no history | apply every migration |
| `Managed` | history holds the Baseline | apply pending migrations |
| `LegacyRawDdl` | built by the pre-migration CREATE TABLE script; no history | reconcile (the frozen legacy script and routines, then the finishing ALTERs), stamp the Baseline, apply the rest |
| `LegacyEfConvention` | built by the old boot-time `InitialCreate` or `EnsureCreated` | if every table is empty, drop them and rebuild from the Baseline; otherwise refuse |
| `Unknown` | none of the above | refuse (exit code 3 / HTTP 409) |

Adoption is idempotent and resumable: the Baseline row is written only after the reconcile
succeeds, so an interrupted run re-enters the legacy state and repeats it. `--no-adopt-legacy`
turns the two legacy states into failures for operators who want to stage the upgrade by hand.

#### **Upgrading a deployment**
1. `scripts/db-backup.sh` dumps the base database and every tenant schema to a timestamped
   file; this is the rollback path.
2. `migrate --status`: every target should be `Managed` or one of the legacy states;
   investigate `Unknown` before going on.
3. Deploy. Startup migrates; or set `DB_MIGRATE_ON_STARTUP=false`, run `migrate --all` in a
   maintenance window and start afterwards.
4. `migrate --status` again: everything `Managed`, nothing pending.

Rolling back one migration: `migrate --tenant <name> --target <previous id>` runs its
`Down()`; the Baseline is the floor. Restoring data is `mysql < backup.sql` with the dump from
step 1.

### Testing Strategy

#### **Unit Tests** (`/tests/`)
- Service layer business logic
- Model validation
- Utility functions
- Mock external dependencies

#### **Integration Tests**
- API endpoint testing
- Database operations
- Storage backend integration
- Multi-tenant scenarios

#### **Manual Testing**
- Swagger UI for API exploration
- Docker environment testing
- Cross-storage compatibility
- Performance testing with large files

---

## Testing Framework

### Current Test Structure
```
tests/
└── XR50TrainingAssetRepo.Tests/
    ├── UnitTest1.cs          # Basic framework tests
    ├── xr50_unit_tests.cs    # Service-specific tests
    └── GlobalUsings.cs       # Test dependencies
```

### Running Tests
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "ClassName=UnitTest1"
```

### Test Categories

#### **Unit Tests**
- **Service Logic**: Asset management, tenant operations, material handling
- **Model Validation**: Data annotation testing, business rule validation
- **Storage Abstraction**: Interface compliance, error handling
- **Database Context**: Entity relationships, query logic

#### **Integration Tests** (Recommended additions)
- **API Endpoints**: Full request/response cycles
- **Storage Backends**: Actual S3/OwnCloud operations
- **Database Operations**: Multi-tenant data isolation
- **File Upload/Download**: End-to-end file handling

#### **Performance Tests** (Future)
- **Large File Handling**: Multi-GB asset uploads
- **Concurrent Users**: Multi-tenant load testing
- **Database Performance**: Query optimization validation
- **Storage Throughput**: Backend comparison testing

---

## Known Limitations

### Current Research Prototype Constraints

#### **Security**
- **Authentication**: JWT Bearer authentication implemented with Keycloak (see [Authentication Guide](guides/authentication.md))
- **Authorization**: Basic auth on quiz submission endpoints; full RBAC not yet implemented
- **Input Validation**: Basic validation, needs comprehensive security review
- **SSL/TLS**: Development certificates only

#### **Scalability**
- **Database Connections**: No connection pooling optimization
- **File Size Limits**: No enforced limits on asset uploads
- **Concurrent Operations**: Limited testing under load
- **Memory Management**: Large file operations not optimized

#### **Production Readiness**
- **Error Recovery**: Limited retry logic for storage operations
- **Monitoring**: Basic logging, no metrics collection
- **Backup Strategy**: `scripts/db-backup.sh` dumps every database on demand; nothing is scheduled
- **High Availability**: Single-instance deployment only

#### **Storage Backend Limitations**

| Feature | S3 Implementation | OwnCloud Implementation |
|---------|-------------------|-------------------------|
| Native Sharing | ❌ (pre-signed URLs only) | ✅ Full sharing API |
| User Management | ❌ External IAM | ✅ Integrated users |
| File Versioning | ❌ Not implemented | ⚠️ Basic support |
| Metadata Storage | ⚠️ Limited tags | ✅ Extended attributes |
| Search Capabilities | ❌ Not implemented | ⚠️ Basic filename search |

#### **API Limitations**
- **Pagination**: Not implemented for large result sets
- **Filtering**: Limited query parameter support
- **Caching**: No response caching strategy
- **Rate Limiting**: No request throttling
- **API Versioning**: Single version only

#### **Development/Research Focus**
- **Code Quality**: Debug statements and TODO markers present
- **Documentation**: API-focused, limited architectural docs
- **Configuration Management**: Multiple environment files, inconsistent patterns
- **Testing Coverage**: Limited test suite, no automated testing pipeline

---

## Future Development

### Planned Enhancements

#### **Phase 1: Production Hardening**
- **Security Implementation**
  - ~~JWT-based authentication system~~ (Implemented - Keycloak integration)
  - Role-based authorization (admin, tenant-admin, user)
  - Input validation and sanitization
  - Security headers and CORS configuration

- **Performance Optimization**
  - Database connection pooling
  - Response caching strategies
  - File upload optimization (chunking, resumable uploads)
  - Query optimization and indexing

- **Operational Features**
  - Health check endpoints
  - Metrics collection (Prometheus/OpenMetrics)
  - Structured logging with correlation IDs
  - Configuration validation on startup

#### **Phase 2: Feature Extensions**
- **Advanced Storage Features**
  - File versioning across all backends
  - Metadata extraction (EXIF, document properties)
  - Content-based search and indexing
  - Automated thumbnail generation

- **Enhanced Multi-Tenancy**
  - Tenant resource quotas and limits
  - Cross-tenant content sharing
  - Tenant-specific customization options
  - Usage analytics and reporting

- **API Enhancements**
  - GraphQL endpoint for complex queries
  - Webhook system for external integrations
  - Bulk operations API
  - Advanced filtering and search

#### **Phase 3: Platform Integration**
- **XR Platform Integration**
  - Real-time asset synchronization
  - Training session asset tracking
  - Performance analytics integration
  - Content recommendation engine

- **External System Connectors**
  - Learning Management System (LMS) integration
  - Enterprise SSO providers (SAML, OIDC)
  - Content Distribution Network (CDN) integration
  - Third-party storage providers (Azure Blob, Google Cloud)

### Research Directions

#### **Experimental Features**
- **AI-Powered Content Analysis**
  - Automated content categorization
  - Quality assessment metrics
  - Accessibility compliance checking
  - Content similarity detection

- **Advanced XR Support**
  - 3D model optimization and conversion
  - Spatial audio processing
  - Interactive content validation
  - Cross-platform compatibility testing

- **Distributed Architecture**
  - Microservices decomposition
  - Event-driven architecture
  - Container orchestration (Kubernetes)
  - Multi-region deployment

#### **Research Validation**
- **Performance Benchmarking**
  - Storage backend comparison studies
  - Multi-tenant isolation validation
  - Scalability threshold identification
  - Cost-benefit analysis per deployment model

- **User Experience Research**
  - API usability studies with integration partners
  - Storage workflow optimization
  - Content management efficiency metrics
  - Cross-platform compatibility validation

### Migration Path
The current research prototype provides a solid foundation for production development. The recommended evolution path:

1. **Immediate**: Address security and error handling gaps
2. **Short-term**: Implement production monitoring and optimization
3. **Medium-term**: Extend feature set based on XR5.0 project requirements
4. **Long-term**: Consider architectural evolution based on research outcomes

---

## Contributing

### Development Workflow
1. **Fork repository** and create feature branch
2. **Follow coding standards** established in existing codebase
3. **Add tests** for new functionality
4. **Update documentation** for API changes
5. **Submit pull request** with detailed description

### Code Standards
- **C# Conventions**: Follow Microsoft C# coding guidelines
- **API Design**: RESTful principles, consistent naming
- **Error Handling**: Comprehensive logging, appropriate HTTP status codes
- **Documentation**: XML comments for public APIs, README updates for features

### Research Collaboration
This is a research prototype developed for the XR5.0 EU project. Contributions should align with research objectives and maintain compatibility with existing XR platform integrations.

---

*This documentation reflects the current state of the XR5.0 Training Asset Repository as a research prototype. For production deployment, additional hardening and security measures are required.*

**Project**: Horizon Europe XR5.0 (Grant Agreement No. 101135209)  
**Contact**: Emmanouil Mavrogiorgis (emaurog@synelixis.com)  
**Last Updated**: January 2026
