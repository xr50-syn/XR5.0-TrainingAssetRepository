# XR5.0 – Component OpenAPI Specifications

**Ref.** Ares(2024)9180685 – 20/12/2024
**Project:** HUMAN-CENTRIC AI-ENABLED EXTENDED REALITY APPLICATIONS FOR THE INDUSTRY 5.0 ERA

---

## 1. Purpose of This Document

This document is addressed to **XR5.0 component developers** and defines the **OpenAPI Specifications** for the **Training Asset Repository** component developed by Synelixis within the XR5.0 platform.

Given the system complexity, **Task T2.5** establishes these specifications to ensure:

- Seamless integration
- Interoperability
- Secure and efficient communication
- Modular, scalable, and maintainable design

The OpenAPI descriptions below explain both the **why** and the **how** behind architectural decisions, aligned with XR5.0 best practices.

---

## 2. Document Structure

This document is divided into three main sections:

1. **General Details**
2. **Open Interfaces**
3. **Information Flows**

Each section documents a different aspect of component integration within the XR5.0 platform.

---

## 3. General Details

### Table 1 – General Details of Component

| Field | Description |
|-------|-------------|
| **Component Name** | XR5.0 Training Asset Repository |
| **Responsible Partner** | SYN (Synelixis) |
| **Project Task(s)** | T2.5 |
| **Description** | A multi-tenant, cloud-agnostic REST API for managing Extended Reality (XR) training materials. Provides storage, retrieval, and organisation of training assets (files), materials (typed training content), training programs, and learning paths. Supports user progress tracking and quiz evaluation. Integrates with AI chatbot and voice assistant services. Does **not** provide front-end UI, real-time streaming, or XR rendering. |
| **User Interfaces** | Swagger UI available at `/swagger` for interactive API exploration. No other user-facing UI. |
| **Hardware Interfaces** | N/A |
| **Software / Services Interfaces** | **MySQL/MariaDB** (database, via Pomelo EF Core provider) -- **AWS S3 / MinIO** (object storage, via AWSSDK.S3) -- **OwnCloud** (alternative storage, via WebDAV) -- **ASP.NET Core 8.0** (runtime framework) -- **Entity Framework Core 8.0** (ORM) |
| **Component Interfaces** | AI Chatbot Service (proxied via Chat API endpoints) -- AI Voice Assistant Service (proxied via Voice Assistant endpoints) -- AI Asset Processing Service (asset submission and sync endpoints) |
| **Notes / Remarks** | Research prototype. Authentication is implemented (OAuth2/JWT) but may be disabled in development environments. Database-per-tenant architecture ensures data isolation. |
| **Component URL** | Swagger UI: `https://<host>/swagger` |

---

## 4. Open Interfaces

The Training Asset Repository exposes eight open interfaces:

1. **Tenant Management API** -- Provisioning and managing tenants
2. **Asset Management API** -- File upload, download, metadata, and sharing
3. **Material Management API** -- Training material CRUD and completion tracking
4. **Training Program API** -- Training program and learning path management
5. **User Management API** -- User CRUD and progress tracking
6. **Chat API** -- AI chatbot interaction
7. **Voice Assistant API** -- AI voice assistant interaction
8. **Troubleshooting API** -- Administrative diagnostics and repair

---

## 5. Open Interface Specifications

---

### Open Interface #1 -- Tenant Management API

### Table 2 – Tenant Management API

| Field | Description |
|-------|-------------|
| **Open Interface Name** | Tenant Management API |
| **Purpose** | Provision, configure, and manage tenants. Each tenant receives an isolated database and storage bucket. |
| **Function** | Enables multi-tenant operation by allowing creation, retrieval, deletion, and validation of tenant environments. |
| **Technology Stack** | REST API (ASP.NET Core 8.0) |
| **Protocol** | HTTPS |
| **Data Format** | JSON |
| **Authentication Method** | OAuth2 / JWT Bearer Token |

#### Authentication Details

- **Authentication flow:** OAuth2 Password Grant or Authorization Code Grant. JWT Bearer tokens are accepted via the `Authorization: Bearer <token>` header.
- **Token endpoint:** `POST /connect/token`
- **Authorization endpoint:** `GET /connect/authorize`
- **Required policy:** `SystemAdmin` (requires `role` claim of `systemadmin` or `superadmin`)
- **Error handling:** `401 Unauthorized` if no valid token is provided; `403 Forbidden` if the user lacks the required role.

#### Example Usage

**Request:**
```http
POST /xr50/trainingAssetRepository/tenants
Authorization: Bearer eyJhbGciOi...
Content-Type: application/json

{
  "Name": "pilot-factory",
  "DisplayName": "Pilot Factory Tenant",
  "StorageType": "S3",
  "ConnectionString": "Server=db;Database=xr50_tenant_pilot_factory;User=root;Password=..."
}
```

**Response (201 Created):**
```json
{
  "name": "pilot-factory",
  "displayName": "Pilot Factory Tenant",
  "storageType": "S3",
  "createdAt": "2025-06-15T10:30:00Z"
}
```

#### Input / Output Specification

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/xr50/trainingAssetRepository/tenants` | List all tenants |
| POST | `/xr50/trainingAssetRepository/tenants` | Create a new tenant |
| GET | `/xr50/trainingAssetRepository/tenants/{tenantName}` | Get a specific tenant |
| DELETE | `/xr50/trainingAssetRepository/tenants/{tenantName}` | Delete a tenant and its resources |
| GET | `/xr50/trainingAssetRepository/tenants/{tenantName}/validate-storage` | Validate tenant storage connectivity |
| GET | `/xr50/trainingAssetRepository/tenants/{tenantName}/storage-stats` | Get tenant storage usage statistics |
| GET | `/xr50/trainingAssetRepository/tenants/examples/create-requests` | Get example create-tenant payloads |

**Path parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `tenantName` | string | Yes | Unique tenant identifier (lowercase, hyphens allowed) |

**Status codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 201 | Tenant created |
| 400 | Invalid request (e.g., duplicate name, missing fields) |
| 404 | Tenant not found |
| 500 | Internal server error |

---

### Open Interface #2 -- Asset Management API

### Table 2 – Asset Management API

| Field | Description |
|-------|-------------|
| **Open Interface Name** | Asset Management API |
| **Purpose** | Upload, download, search, and share binary files (images, videos, PDFs, Unity bundles) within a tenant. |
| **Function** | Manages the lifecycle of training asset files. Performs magic-byte file type detection for security. Supports sharing via pre-signed URLs or storage-native shares. Integrates with AI processing pipeline. |
| **Technology Stack** | REST API (ASP.NET Core 8.0), IStorageService abstraction (S3 / OwnCloud) |
| **Protocol** | HTTPS |
| **Data Format** | JSON (metadata), multipart/form-data (uploads), binary streams (downloads) |
| **Authentication Method** | OAuth2 / JWT Bearer Token |

#### Authentication Details

- **Required policy:** `TenantUser` (requires `tenantName` claim matching the route parameter)
- See Tenant Management API for token acquisition flow.

#### Example Usage

**Upload a file:**
```http
POST /api/pilot-factory/assets/upload?description=Safety+training+video
Authorization: Bearer eyJhbGciOi...
Content-Type: multipart/form-data; boundary=---boundary

-----boundary
Content-Disposition: form-data; name="file"; filename="safety-training.mp4"
Content-Type: video/mp4

<binary data>
-----boundary--
```

**Response (201 Created):**
```json
{
  "status": "success",
  "message": "Asset created",
  "id": 42,
  "fileName": "safety-training.mp4",
  "fileType": "mp4",
  "assetType": "Video",
  "created_at": "2025-06-15T11:00:00Z"
}
```

**Download a file:**
```http
GET /api/pilot-factory/assets/42/download
Authorization: Bearer eyJhbGciOi...
```

**Response:** `200 OK` with binary file stream.

#### Input / Output Specification

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/{tenantName}/assets` | List all assets |
| GET | `/api/{tenantName}/assets/{id}` | Get asset metadata |
| POST | `/api/{tenantName}/assets` | Create asset (multipart: Description, Src, Filetype, Filename, File) |
| POST | `/api/{tenantName}/assets/upload` | Simple file upload (multipart: file, query: description) |
| PUT | `/api/{tenantName}/assets/{id}` | Update asset metadata |
| DELETE | `/api/{tenantName}/assets/{id}` | Delete asset and its file |
| GET | `/api/{tenantName}/assets/search` | Search assets (query: searchTerm, filetype) |
| GET | `/api/{tenantName}/assets/by-filetype/{filetype}` | Filter assets by file type |
| GET | `/api/{tenantName}/assets/{id}/materials` | Get materials referencing this asset |
| GET | `/api/{tenantName}/assets/{id}/usage-count` | Count materials referencing this asset |
| GET | `/api/{tenantName}/assets/{id}/download` | Download file (binary stream) |
| GET | `/api/{tenantName}/assets/{id}/file-info` | Get file metadata (size, type, storage path) |
| POST | `/api/{tenantName}/assets/{assetId}/share` | Create a share link |
| GET | `/api/{tenantName}/assets/{assetId}/shares` | List shares for an asset |
| GET | `/api/{tenantName}/assets/shares` | List all shares in tenant |
| GET | `/api/{tenantName}/assets/shares/{shareId}` | Get a specific share |
| DELETE | `/api/{tenantName}/assets/shares/{shareId}` | Delete a share |
| GET | `/api/{tenantName}/assets/{assetId}/share-url` | Get pre-signed download URL |
| POST | `/api/{tenantName}/assets/{id}/submit` | Submit asset for AI processing |
| GET | `/api/{tenantName}/assets/ai-status/{status}` | Get assets by AI processing status |
| GET | `/api/{tenantName}/assets/ai-pending` | Get assets pending AI processing |
| POST | `/api/{tenantName}/assets/ai-sync` | Sync assets with AI processing service |

**Path parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `tenantName` | string | Yes | Tenant identifier |
| `id` / `assetId` | int | Yes (where present) | Asset ID |
| `shareId` | int | Yes (where present) | Share ID |
| `filetype` | string | Yes (where present) | File type filter (e.g., `pdf`, `mp4`) |
| `status` | string | Yes (where present) | AI processing status |

**Query parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `searchTerm` | string | No | Text search across asset fields |
| `filetype` | string | No | Filter by file type |
| `description` | string | No | Description for simple upload |

**Supported file types (detected via magic bytes):**

| Category | Formats |
|----------|---------|
| Images | PNG, JPEG, GIF, BMP, WebP |
| Videos | MP4, MOV, AVI, WebM |
| Documents | PDF |
| Unity | UnityFS bundles, UnityWeb bundles |

**Status codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 201 | Asset created |
| 204 | Asset updated/deleted |
| 400 | Invalid file or request |
| 404 | Asset or share not found |
| 500 | Internal server error |

---

### Open Interface #3 -- Material Management API

### Table 2 – Material Management API

| Field | Description |
|-------|-------------|
| **Open Interface Name** | Material Management API |
| **Purpose** | Manage typed training content (materials) and track learner completion and quiz submissions. |
| **Function** | Provides CRUD and detail retrieval for materials. Materials use Table-Per-Hierarchy (TPH) inheritance with a type discriminator. Supports quiz answer submission and material completion marking. |
| **Technology Stack** | REST API (ASP.NET Core 8.0), EF Core TPH inheritance |
| **Protocol** | HTTPS |
| **Data Format** | JSON |
| **Authentication Method** | OAuth2 / JWT Bearer Token |

#### Authentication Details

- **Required policy:** `TenantUser`

#### Example Usage

**Get material with full details:**
```http
GET /api/pilot-factory/materials/5/detail
Authorization: Bearer eyJhbGciOi...
```

**Response (200 OK):**
```json
{
  "id": 5,
  "name": "Safety Checklist",
  "description": "Pre-operation safety checklist",
  "type": "Checklist",
  "checklistEntries": [
    { "id": 1, "text": "Verify emergency stop is accessible", "order": 1 },
    { "id": 2, "text": "Check PPE availability", "order": 2 }
  ],
  "created_at": "2025-06-10T08:00:00Z",
  "updated_at": "2025-06-10T08:00:00Z"
}
```

**Submit quiz answers:**
```http
POST /api/pilot-factory/materials/12/submit
Authorization: Bearer eyJhbGciOi...
Content-Type: application/json

{
  "userId": "user-001",
  "answers": [
    { "questionId": 1, "selectedOptionId": 3 },
    { "questionId": 2, "selectedOptionId": 7 }
  ]
}
```

#### Input / Output Specification

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/{tenantName}/materials` | List all materials |
| GET | `/api/{tenantName}/materials/{id}` | Get a material (polymorphic) |
| GET | `/api/{tenantName}/materials/{id}/detail` | Get material with all related entities |
| POST | `/api/{tenantName}/materials/{materialId}/submit` | Submit quiz answers |
| POST | `/api/{tenantName}/materials/{materialId}/complete` | Mark a material as complete for a user |

**Material types and their specific properties:**

| Type | Discriminator | Additional Properties |
|------|---------------|-----------------------|
| Video | `Video` | `videoTimestamps` (list of timestamp markers) |
| Image | `Image` | -- |
| PDF | `PDF` | -- |
| Checklist | `Checklist` | `checklistEntries` (ordered list of items) |
| Workflow | `Workflow` | `workflowSteps` (ordered list of steps) |
| Questionnaire | `Questionnaire` | Quiz questions with answer options |
| Unity | `Unity` | -- |
| Chatbot | `Chatbot` | `endpoint`, `apiKey` |
| MQTT_Template | `MQTT_Template` | -- |
| Default | `Default` | -- |

**Status codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 400 | Invalid submission (e.g., missing answers) |
| 404 | Material not found |
| 500 | Internal server error |

---

### Open Interface #4 -- Training Program API

### Table 2 – Training Program API

| Field | Description |
|-------|-------------|
| **Open Interface Name** | Training Program API |
| **Purpose** | Create and manage structured training curricula composed of learning paths and materials. |
| **Function** | Full lifecycle management of training programs. Programs contain learning paths, which in turn contain ordered materials. Supports material assignment with ordering and relationship types. |
| **Technology Stack** | REST API (ASP.NET Core 8.0) |
| **Protocol** | HTTPS |
| **Data Format** | JSON |
| **Authentication Method** | OAuth2 / JWT Bearer Token |

#### Authentication Details

- **Required policy:** `TenantUser` for read operations, `TenantAdmin` for write operations.

#### Example Usage

**Create a training program:**
```http
POST /api/pilot-factory/programs
Authorization: Bearer eyJhbGciOi...
Content-Type: application/json

{
  "Name": "Machine Safety Certification",
  "Description": "Complete safety training for machine operators",
  "Objectives": "Certify operators on safety protocols",
  "Requirements": "Basic factory orientation completed",
  "min_level_rank": 1,
  "max_level_rank": 3,
  "required_upto_level_rank": 2,
  "materials": [1, 5, 12],
  "learning_path": [
    {
      "Name": "Safety Fundamentals",
      "Description": "Core safety concepts",
      "materials": [1, 5]
    }
  ]
}
```

**Response (201 Created):**
```json
{
  "status": "success",
  "id": 1,
  "name": "Machine Safety Certification",
  "learningPaths": [
    {
      "id": 1,
      "name": "Safety Fundamentals",
      "materials": [...]
    }
  ],
  "created_at": "2025-06-15T12:00:00Z"
}
```

#### Input / Output Specification

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/{tenantName}/programs` | List all programs |
| GET | `/api/{tenantName}/programs/{id}` | Get a program |
| GET | `/api/{tenantName}/programs/{id}/detail` | Get program with nested learning paths and materials |
| GET | `/api/{tenantName}/programs/detail` | List all programs with full details |
| POST | `/api/{tenantName}/programs` | Create a program (with inline learning paths and materials) |
| POST | `/api/{tenantName}/programs/detail` | Create a program (detailed request format) |
| PUT | `/api/{tenantName}/programs/{id}` | Update a program |
| DELETE | `/api/{tenantName}/programs/{id}` | Delete a program |
| POST | `/api/{tenantName}/programs/{programId}/submit` | Bulk-submit material completions |
| GET | `/api/{tenantName}/programs/{trainingProgramId}/materials` | Get materials in a program (query: includeOrder) |
| POST | `/api/{tenantName}/programs/{trainingProgramId}/assign-material/{materialId}` | Assign material (query: relationshipType, displayOrder) |
| DELETE | `/api/{tenantName}/programs/{trainingProgramId}/remove-material/{materialId}` | Remove material from program |

**Learning path sub-endpoints (deprecated as standalone -- use via programs):**

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/{tenantName}/learningpaths` | List all learning paths |
| GET | `/api/{tenantName}/learningpaths/{id}` | Get a learning path |
| POST | `/api/{tenantName}/learningpaths` | Create a learning path |
| PUT | `/api/{tenantName}/learningpaths/{id}` | Update a learning path |
| DELETE | `/api/{tenantName}/learningpaths/{id}` | Delete a learning path |
| GET | `/api/{tenantName}/learningpaths/program/{trainingProgramId}` | Get learning paths for a program |
| POST | `/api/{tenantName}/learningpaths/{learningPathId}/assign/{trainingProgramId}` | Assign learning path to program |
| DELETE | `/api/{tenantName}/learningpaths/{learningPathId}/unassign/{trainingProgramId}` | Unassign learning path from program |
| GET | `/api/{tenantName}/learningpaths/{learningPathId}/materials` | Get materials in path (query: includeOrder, relationshipType) |
| POST | `/api/{tenantName}/learningpaths/{learningPathId}/assign-material/{materialId}` | Assign material to path (query: relationshipType, displayOrder) |
| DELETE | `/api/{tenantName}/learningpaths/{learningPathId}/remove-material/{materialId}` | Remove material from path |
| PUT | `/api/{tenantName}/learningpaths/{learningPathId}/reorder-materials` | Reorder materials (body: `{materialId: order}` map) |

**Status codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 201 | Program/path created |
| 204 | Updated/deleted |
| 400 | Invalid request (e.g., circular reference) |
| 404 | Program, path, or material not found |
| 500 | Internal server error |

---

### Open Interface #5 -- User Management API

### Table 2 – User Management API

| Field | Description |
|-------|-------------|
| **Open Interface Name** | User Management API |
| **Purpose** | Manage tenant users and track learner progress across materials, programs, and quizzes. |
| **Function** | CRUD operations for users within a tenant. Provides progress tracking per user, per material, per program, and quiz-level analytics. |
| **Technology Stack** | REST API (ASP.NET Core 8.0) |
| **Protocol** | HTTPS |
| **Data Format** | JSON |
| **Authentication Method** | OAuth2 / JWT Bearer Token |

#### Authentication Details

- **Required policy:** `TenantUser` for own progress; `TenantAdmin` for managing other users.

#### Example Usage

**Get user progress:**
```http
GET /api/pilot-factory/users/user-001/progress
Authorization: Bearer eyJhbGciOi...
```

**Response (200 OK):**
```json
{
  "userId": "user-001",
  "completedMaterials": 8,
  "totalMaterials": 15,
  "completionPercentage": 53.3,
  "quizScores": [
    { "materialId": 12, "score": 85, "passed": true }
  ]
}
```

#### Input / Output Specification

**User CRUD:**

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/{tenantName}/users` | List all users |
| GET | `/api/{tenantName}/users/{userName}` | Get a user |
| POST | `/api/{tenantName}/users` | Create a user |
| PUT | `/api/{tenantName}/users/{userName}` | Update a user |
| DELETE | `/api/{tenantName}/users/{userName}` | Delete a user |

**User progress:**

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/{tenantName}/users/{userId}/progress` | Get progress for a specific user |
| GET | `/api/{tenantName}/users/progress` | Get progress for all users |
| GET | `/api/{tenantName}/users/{userId}/materials/{materialId}` | Get detailed user-material interaction |
| GET | `/api/{tenantName}/users/{userId}/programs/{programId}/materials` | Get user's material progress within a program |

**Quiz progress:**

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/{tenantName}/quiz-progress/tenant` | Tenant-wide quiz analytics |
| GET | `/api/{tenantName}/quiz-progress/program/{programId}` | Quiz progress for a program |
| GET | `/api/{tenantName}/quiz-progress/learning-path/{learningPathId}` | Quiz progress for a learning path |
| GET | `/api/{tenantName}/quiz-progress/material/{materialId}` | Quiz progress for a specific material |

**Program progress:**

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/{tenantName}/program-progress/program/{programId}` | Completion progress for a program |

**Status codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 201 | User created |
| 204 | User updated/deleted |
| 404 | User, material, or program not found |
| 500 | Internal server error |

---

### Open Interface #6 -- Chat API

### Table 2 – Chat API

| Field | Description |
|-------|-------------|
| **Open Interface Name** | Chat API |
| **Purpose** | Provide conversational AI access to training content via chatbot materials. |
| **Function** | Proxies chat queries to configured chatbot backends. Supports session-based conversations, multiple chatbot instances per tenant, and health checking. |
| **Technology Stack** | REST API (ASP.NET Core 8.0), external chatbot service integration |
| **Protocol** | HTTPS |
| **Data Format** | JSON, application/x-www-form-urlencoded |
| **Authentication Method** | OAuth2 / JWT Bearer Token |

#### Authentication Details

- **Required policy:** `TenantUser`

#### Example Usage

**Ask a question:**
```http
POST /api/pilot-factory/chat/ask
Authorization: Bearer eyJhbGciOi...
Content-Type: application/json

{
  "Query": "What are the safety requirements for machine X?",
  "SessionId": "session-abc-123"
}
```

**Response (200 OK):**
```json
{
  "answer": "The safety requirements for machine X include...",
  "sessionId": "session-abc-123",
  "sources": [...]
}
```

#### Input / Output Specification

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/{tenantName}/chat/ask` | Ask default chatbot (JSON body) |
| POST | `/api/{tenantName}/chat/ask/form` | Ask default chatbot (form data: query, session_id) |
| POST | `/api/{tenantName}/chat/{chatbotId}/ask` | Ask specific chatbot (JSON body) |
| POST | `/api/{tenantName}/chat/{chatbotId}/ask/form` | Ask specific chatbot (form data) |
| GET | `/api/{tenantName}/chat` | List available chatbots |
| GET | `/api/{tenantName}/chat/{chatbotId}` | Get chatbot details |
| GET | `/api/{tenantName}/chat/health` | Health check (default chatbot) |
| GET | `/api/{tenantName}/chat/{chatbotId}/health` | Health check (specific chatbot) |

**Request body (JSON):**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Query` | string | Yes | The user's question |
| `SessionId` | string | No | Session ID for multi-turn conversation context |

**Status codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 400 | Missing or invalid query |
| 404 | Chatbot not found |
| 502 | Upstream chatbot service unavailable |
| 500 | Internal server error |

---

### Open Interface #7 -- Voice Assistant API

### Table 2 – Voice Assistant API

| Field | Description |
|-------|-------------|
| **Open Interface Name** | Voice Assistant API |
| **Purpose** | Provide voice-based AI interaction with training content, including document ingestion for knowledge base. |
| **Function** | Proxies voice queries to configured voice assistant backends. Supports document upload for knowledge enrichment, session-based conversations, and health checking. |
| **Technology Stack** | REST API (ASP.NET Core 8.0), external voice assistant service integration |
| **Protocol** | HTTPS |
| **Data Format** | JSON, multipart/form-data |
| **Authentication Method** | OAuth2 / JWT Bearer Token |

#### Authentication Details

- **Required policy:** `TenantUser`

#### Example Usage

**Ask a question:**
```http
POST /api/pilot-factory/voice-assistant/ask
Authorization: Bearer eyJhbGciOi...
Content-Type: application/json

{
  "Query": "Explain the emergency shutdown procedure",
  "SessionId": "voice-session-456"
}
```

**Upload a document to the knowledge base:**
```http
POST /api/pilot-factory/voice-assistant/documents
Authorization: Bearer eyJhbGciOi...
Content-Type: multipart/form-data

file=@machine-manual.pdf
```

#### Input / Output Specification

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/{tenantName}/voice-assistant/ask` | Ask default voice assistant (JSON) |
| POST | `/api/{tenantName}/voice-assistant/ask/form` | Ask default voice assistant (form data) |
| POST | `/api/{tenantName}/voice-assistant/{voiceId}/ask` | Ask specific voice assistant (JSON) |
| POST | `/api/{tenantName}/voice-assistant/{voiceId}/ask/form` | Ask specific voice assistant (form data) |
| POST | `/api/{tenantName}/voice-assistant/documents` | Upload document (default assistant) |
| POST | `/api/{tenantName}/voice-assistant/{voiceId}/documents` | Upload document (specific assistant) |
| GET | `/api/{tenantName}/voice-assistant/{voiceId}/documents` | List documents for an assistant |
| GET | `/api/{tenantName}/voice-assistant` | List available voice assistants |
| GET | `/api/{tenantName}/voice-assistant/{voiceId}` | Get voice assistant details |
| GET | `/api/{tenantName}/voice-assistant/health` | Health check (default) |
| GET | `/api/{tenantName}/voice-assistant/{voiceId}/health` | Health check (specific assistant) |

**Status codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 400 | Missing or invalid query |
| 404 | Voice assistant not found |
| 502 | Upstream voice service unavailable |
| 500 | Internal server error |

---

### Open Interface #8 -- Troubleshooting API

### Table 2 – Troubleshooting API

| Field | Description |
|-------|-------------|
| **Open Interface Name** | Troubleshooting API |
| **Purpose** | Administrative endpoints for diagnosing and repairing tenant databases and infrastructure. |
| **Function** | Provides diagnostics, connection testing, table inspection, migration execution, and database rebuild capabilities for tenant environments. Intended for system administrators only. |
| **Technology Stack** | REST API (ASP.NET Core 8.0), EF Core Migrations |
| **Protocol** | HTTPS |
| **Data Format** | JSON |
| **Authentication Method** | OAuth2 / JWT Bearer Token |

#### Authentication Details

- **Required policy:** `SystemAdmin`

#### Input / Output Specification

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/troubleshooting/diagnose/{tenantName}` | Run diagnostics on a tenant |
| POST | `/api/troubleshooting/repair/{tenantName}` | Attempt automatic repair |
| GET | `/api/troubleshooting/test-connection/{tenantName}` | Test database connectivity |
| GET | `/api/troubleshooting/databases` | List all known databases |
| POST | `/api/troubleshooting/create-test-tenant/{tenantName}` | Create a test tenant |
| POST | `/api/troubleshooting/force-recreate/{tenantName}` | Force-recreate tenant database |
| POST | `/api/troubleshooting/create-tables/{tenantName}` | Create tables in tenant database |
| POST | `/api/troubleshooting/rebuild/{tenantName}` | Full rebuild of tenant database |
| GET | `/api/troubleshooting/tables/{tenantName}` | List tables in a tenant database |
| DELETE | `/api/troubleshooting/delete-database/{tenantName}` | Delete tenant database |
| DELETE | `/api/troubleshooting/delete-completely/{tenantName}` | Delete tenant database and record |
| GET | `/api/troubleshooting/health-check` | System-wide health check |
| POST | `/api/troubleshooting/migrate-annotations/{tenantName}` | Run annotation schema migrations |
| POST | `/api/troubleshooting/migrate-quiz-answers/{tenantName}` | Run quiz answer schema migrations |

**Status codes:**

| Code | Meaning |
|------|---------|
| 200 | Success |
| 404 | Tenant not found |
| 500 | Internal server error or repair failure |

---

## 6. Global Health Endpoint

```
GET /health
```

**Response (200 OK):**
```json
{
  "status": "healthy",
  "timestamp": "2025-06-15T10:00:00Z"
}
```

---

## 7. Information Flows

### 7.1 Training Content Delivery Flow

```
Client Application
    │
    ├─── GET /api/{tenant}/programs/detail ──────────> Training Program API
    │                                                      │
    │    <── Program with Learning Paths & Materials ──────┘
    │
    ├─── GET /api/{tenant}/materials/{id}/detail ────> Material Management API
    │                                                      │
    │    <── Material with type-specific content ──────────┘
    │
    └─── GET /api/{tenant}/assets/{id}/download ─────> Asset Management API
                                                           │
         <── Binary file stream ──────────────────────────┘
```

### 7.2 Quiz Submission Flow

```
Client Application
    │
    ├─── POST /api/{tenant}/materials/{id}/submit ───> Material Management API
    │         (quiz answers)                               │
    │                                                      ├── Validates answers
    │                                                      ├── Records attempt
    │    <── Score and pass/fail result ───────────────────┘
    │
    └─── GET /api/{tenant}/users/{userId}/progress ──> User Management API
                                                           │
         <── Aggregated progress data ────────────────────┘
```

### 7.3 Asset Upload and AI Processing Flow

```
Client Application
    │
    ├─── POST /api/{tenant}/assets/upload ───────────> Asset Management API
    │         (multipart file)                             │
    │                                                      ├── Magic-byte detection
    │                                                      ├── Store to S3/OwnCloud
    │    <── Asset metadata (id, type) ───────────────────┘
    │
    ├─── POST /api/{tenant}/assets/{id}/submit ──────> Asset Management API
    │                                                      │
    │                                                      ├── Submit to AI Service
    │    <── Submission confirmation ──────────────────────┘
    │
    └─── GET /api/{tenant}/assets/ai-status/completed ─> Asset Management API
                                                           │
         <── Processed assets list ───────────────────────┘
```

### 7.4 Tenant Provisioning Flow

```
System Administrator
    │
    ├─── POST /xr50/trainingAssetRepository/tenants ─> Tenant Management API
    │         (tenant config)                              │
    │                                                      ├── Create database
    │                                                      ├── Run migrations
    │                                                      ├── Create storage bucket
    │    <── Tenant created ──────────────────────────────┘
    │
    └─── GET .../tenants/{name}/validate-storage ────> Tenant Management API
                                                           │
         <── Storage validation result ───────────────────┘
```

### 7.5 Chat / Voice Assistant Flow

```
Client Application
    │
    ├─── POST /api/{tenant}/chat/ask ────────────────> Chat API
    │         (query + session)                            │
    │                                                      ├── Forward to chatbot backend
    │    <── AI-generated answer ─────────────────────────┘
    │
    └─── POST /api/{tenant}/voice-assistant/ask ─────> Voice Assistant API
              (query + session)                            │
                                                           ├── Forward to voice backend
         <── AI-generated answer ─────────────────────────┘
```

---

## 8. Summary

| Interface | Endpoints | Route Prefix |
|-----------|-----------|--------------|
| Tenant Management | 7 | `xr50/trainingAssetRepository/tenants` |
| Asset Management | 21 | `api/{tenantName}/assets` |
| Material Management | 5 | `api/{tenantName}/materials` |
| Training Program | 12 | `api/{tenantName}/programs` |
| Learning Paths (deprecated) | 12 | `api/{tenantName}/learningpaths` |
| User Management | 5 | `api/{tenantName}/users` |
| Progress Tracking | 9 | `api/{tenantName}/users`, `quiz-progress`, `program-progress` |
| Chat | 8 | `api/{tenantName}/chat` |
| Voice Assistant | 11 | `api/{tenantName}/voice-assistant` |
| Troubleshooting | 14 | `api/troubleshooting` |
| Health | 2 | `/health`, `api/test` |
| **Total** | **106** | |
