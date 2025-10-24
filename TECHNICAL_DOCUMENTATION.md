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
- **XR50TrainingProgramController**: Training program lifecycle
- **XR50LearningPathController**: Learning path creation and assignment
- **XR50UserController**: User management within tenants

#### **Service Layer**
- **IStorageService**: Abstract storage interface with S3/OwnCloud implementations
- **IXR50TenantManagementService**: Dynamic tenant database management
- **XR50MigrationService**: Automated database schema provisioning
- **Asset/Material/Program Services**: Business logic implementation

#### **Data Layer**
- **XR50TrainingContext**: Entity Framework context with dynamic connection strings
- **XR50TenantDbContextFactory**: Per-tenant database context creation
- **Migration System**: Automated schema deployment per tenant

---

## Multi-Tenancy Model

### Architecture Pattern
The system implements a **Database-per-Tenant** pattern with shared application infrastructure:

```
Admin Database (xr50_repository)
├── Tenants table (tenant configurations)
├── Global users table
└── System configuration

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

#### **1. Tenant Management** (`/api/tenants/`)
- `POST /create` - Create new tenant with storage provisioning
- `GET /` - List all tenants (admin only)
- `GET /{name}` - Get tenant details
- `DELETE /{name}` - Remove tenant and all data

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
- `POST /upload` - Upload file assets
- `GET /{id}/download` - Download asset files
- `GET /{id}/file-info` - Get file metadata
- `POST /{id}/share` - Create sharing links (OwnCloud only)
- `GET /shares` - List tenant shares
- `DELETE /shares/{id}` - Revoke shares

#### **6. User Management** (`/api/{tenant}/users/`)
- `GET /` - List tenant users
- `POST /` - Create user account
- `GET /{id}` - Get user profile
- `PUT /{id}` - Update user
- `DELETE /{id}` - Remove user access

### Response Format
All endpoints return JSON with consistent structure:

```json
{
  "success": true,
  "data": { /* response payload */ },
  "message": "Operation completed successfully",
  "timestamp": "2024-09-10T14:30:00Z"
}
```

Error responses:
```json
{
  "success": false,
  "error": "ValidationError",
  "message": "Detailed error description",
  "details": { /* validation errors */ },
  "timestamp": "2024-09-10T14:30:00Z"
}
```

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
    public List<ChecklistEntry> ChecklistEntries { get; set; }
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

    // File Operations
    Task<string> UploadFileAsync(string tenantName, string fileName, IFormFile file);
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

# Or local MySQL
mysql -u root -p
CREATE DATABASE xr50_repository;
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

#### **Creating Migrations**
```bash
# Add new migration
dotnet ef migrations add MigrationName --context XR50TrainingContext

# Update database
dotnet ef database update --context XR50TrainingContext
```

#### **Multi-Tenant Migrations**
The system automatically applies migrations to tenant databases during:
- Tenant creation
- Application startup (for existing tenants)
- Manual migration service calls

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
- **Authentication**: System-level auth disabled (commented out)
- **Authorization**: No role-based access control implementation
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
- **Backup Strategy**: No automated backup procedures
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
  - JWT-based authentication system
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
**Last Updated**: September 2024