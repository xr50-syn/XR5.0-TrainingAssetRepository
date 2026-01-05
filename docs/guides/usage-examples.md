# XR5.0 Training Asset Repository - Usage Examples

## Table of Contents
1. [Complete Setup Scenario](#complete-setup-scenario)
2. [Tenant Creation Walkthrough](#tenant-creation-walkthrough)
3. [Training Content Organization](#training-content-organization)
4. [API Usage Examples](#api-usage-examples)
5. [Common Workflows](#common-workflows)

---

## Complete Setup Scenario

### Scenario: Manufacturing Company Training Setup

**Company**: AeroTech Manufacturing
**Use Case**: Setting up XR training for aircraft maintenance procedures
**Storage**: AWS S3 (production environment)
**Goal**: Create structured training programs for different skill levels

---

## Tenant Creation Walkthrough

### Step 1: Environment Setup

Start the repository system:
```bash
# Clone and configure
git clone <repository-url>
cd XR5.0-TrainingAssetRepository

# Configure for S3 production
cp .env.prod .env
# Edit .env with your AWS credentials:
# AWS_ACCESS_KEY_ID=your_key_here
# AWS_SECRET_ACCESS_KEY=your_secret_here
# AWS_REGION=eu-west-1

# Start the system
docker-compose --profile prod up -d
```

Wait for services to start (30-60 seconds), then verify:
- Repository API: http://localhost:5286/swagger

### Step 2: Create Tenant Organization

Using Swagger UI at http://localhost:5286/swagger:

1. Navigate to **1. Tenant Management**
2. Use `POST /api/tenants/create`
3. Request body:

```json
{
  "tenantName": "aerotech-manufacturing",
  "tenantGroup": "pilot-aerospace",
  "description": "AeroTech Manufacturing XR Training Platform",
  "storageType": "S3",
  "s3Config": {
    "bucketName": "xr50-tenant-aerotech-manufacturing",
    "bucketRegion": "eu-west-1"
  },
  "owner": {
    "userName": "john.smith",
    "fullName": "John Smith",
    "userEmail": "j.smith@aerotech.com",
    "password": "SecureTraining2024!",
    "admin": true
  }
}
```

**Expected Response**:
```json
{
  "success": true,
  "data": {
    "tenantId": 1,
    "tenantName": "aerotech-manufacturing",
    "storageType": "S3",
    "s3BucketName": "xr50-tenant-aerotech-manufacturing",
    "createdAt": "2024-10-01T10:00:00Z"
  },
  "message": "Tenant created successfully with S3 storage"
}
```

### Step 3: Verify Tenant Setup

Check that everything was created:

**Database**: New tenant database `xr50_tenant_aerotech_manufacturing`
**Storage**: S3 bucket `xr50-tenant-aerotech-manufacturing`
**User**: Admin user `john.smith` created

---

## Training Content Organization

### Step 4: Create Training Program Structure

#### Create Main Training Program

**Endpoint**: `POST /api/aerotech-manufacturing/programs/`

```json
{
  "name": "Aircraft Maintenance Certification",
  "description": "Complete certification program for aircraft maintenance technicians covering safety protocols, engine maintenance, and quality assurance procedures"
}
```

**Response**:
```json
{
  "success": true,
  "data": {
    "id": 1,
    "name": "Aircraft Maintenance Certification",
    "description": "Complete certification program...",
    "createdAt": "2024-10-01T10:15:00Z"
  }
}
```

#### Create Learning Paths

**1. Safety Protocols Learning Path**

**Endpoint**: `POST /api/aerotech-manufacturing/learningpaths/`

```json
{
  "name": "Safety Protocols and PPE",
  "description": "Essential safety procedures and personal protective equipment training",
  "order": 1,
  "trainingProgramIds": [1]
}
```

**2. Engine Maintenance Learning Path**

```json
{
  "name": "Engine Maintenance Procedures",
  "description": "Comprehensive engine inspection, maintenance, and repair procedures",
  "order": 2,
  "trainingProgramIds": [1]
}
```

**3. Quality Assurance Learning Path**

```json
{
  "name": "Quality Assurance and Documentation",
  "description": "Quality control procedures and maintenance documentation requirements",
  "order": 3,
  "trainingProgramIds": [1]
}
```

### Step 5: Upload Training Assets

#### Upload Safety Manual PDF

**Endpoint**: `POST /api/aerotech-manufacturing/assets/upload`

**Form Data**:
- `file`: safety-protocols-manual.pdf
- `originalName`: "Aircraft Safety Protocols Manual v2.1"
- `description`: "Comprehensive safety manual covering all PPE requirements and emergency procedures"

**Response**:
```json
{
  "success": true,
  "data": {
    "assetId": 1,
    "filename": "a8b9c1d2-e3f4-5678-9abc-def123456789.pdf",
    "originalName": "Aircraft Safety Protocols Manual v2.1",
    "fileSize": 2048576,
    "src": "https://s3.eu-west-1.amazonaws.com/xr50-tenant-aerotech-manufacturing/a8b9c1d2-e3f4-5678-9abc-def123456789.pdf"
  }
}
```

#### Upload Training Video

**Form Data**:
- `file`: engine-inspection-procedure.mp4
- `originalName`: "Engine Pre-flight Inspection Procedure"
- `description`: "Step-by-step video guide for engine pre-flight inspection"

**Response**:
```json
{
  "success": true,
  "data": {
    "assetId": 2,
    "filename": "b9c2d3e4-f5g6-7890-bcde-f012345678ab.mp4",
    "originalName": "Engine Pre-flight Inspection Procedure",
    "fileSize": 157286400
  }
}
```

#### Upload 3D Model

**Form Data**:
- `file`: engine-components-model.glb
- `originalName`: "Interactive Engine Components Model"
- `description`: "3D model for interactive engine component identification training"

### Step 6: Create Training Materials

#### Create Document Material

**Endpoint**: `POST /api/aerotech-manufacturing/materials/`

```json
{
  "name": "Safety Protocols Manual",
  "type": "document",
  "description": "Official safety protocols and PPE requirements manual",
  "assetId": 1,
  "learningPathIds": [1]
}
```

#### Create Video Material with Timestamps

**1. Create Video Material**:
```json
{
  "name": "Engine Inspection Video",
  "type": "video",
  "description": "Complete engine pre-flight inspection procedure",
  "assetId": 2,
  "learningPathIds": [2]
}
```

**2. Add Video Timestamps**:

**Endpoint**: `POST /api/aerotech-manufacturing/materials/video/{materialId}/timestamps`

```json
{
  "timestamps": [
    {
      "timeInSeconds": 30,
      "description": "Initial visual inspection - exterior damage check",
      "title": "Visual Inspection Start"
    },
    {
      "timeInSeconds": 120,
      "description": "Oil level and quality verification procedure",
      "title": "Oil System Check"
    },
    {
      "timeInSeconds": 240,
      "description": "Filter inspection and replacement criteria",
      "title": "Filter System Review"
    },
    {
      "timeInSeconds": 360,
      "description": "Documentation requirements and sign-off procedures",
      "title": "Documentation Process"
    }
  ]
}
```

#### Create Interactive 3D Material

```json
{
  "name": "Engine Components 3D Model",
  "type": "3d_model",
  "description": "Interactive 3D model for component identification training",
  "assetId": 3,
  "learningPathIds": [2]
}
```

#### Create Maintenance Checklist

**Endpoint**: `POST /api/aerotech-manufacturing/materials/`

```json
{
  "name": "Pre-flight Engine Checklist",
  "type": "checklist",
  "description": "Mandatory checklist for engine pre-flight inspection"
}
```

**Add Checklist Entries**:

**Endpoint**: `POST /api/aerotech-manufacturing/materials/checklist/{materialId}/entries`

```json
{
  "entries": [
    {
      "item": "Visual inspection for exterior damage, leaks, or corrosion",
      "required": true,
      "order": 1
    },
    {
      "item": "Engine oil level check - minimum 6 quarts",
      "required": true,
      "order": 2
    },
    {
      "item": "Air filter condition assessment",
      "required": true,
      "order": 3
    },
    {
      "item": "Fuel system inspection for leaks",
      "required": true,
      "order": 4
    },
    {
      "item": "Documentation completion and supervisor sign-off",
      "required": true,
      "order": 5
    }
  ]
}
```

---

## API Usage Examples

### Authentication and Error Handling

All API requests should include proper error handling:

```bash
# Example using curl
curl -X POST "http://localhost:5286/api/aerotech-manufacturing/materials/" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Safety Manual",
    "type": "document",
    "description": "Safety protocols manual",
    "assetId": 1
  }'
```

**Success Response (200)**:
```json
{
  "success": true,
  "data": { "id": 1, "name": "Safety Manual", ... },
  "message": "Material created successfully"
}
```

**Error Response (400)**:
```json
{
  "success": false,
  "error": "ValidationError",
  "message": "Material name is required",
  "details": {
    "name": ["The Name field is required."]
  }
}
```

### File Download Examples

#### Get Asset Download URL

**Endpoint**: `GET /api/aerotech-manufacturing/assets/{assetId}/download`

```bash
curl -X GET "http://localhost:5286/api/aerotech-manufacturing/assets/1/download"
```

**Response**:
```json
{
  "success": true,
  "data": {
    "downloadUrl": "https://s3.eu-west-1.amazonaws.com/xr50-tenant-aerotech-manufacturing/a8b9c1d2-e3f4-5678-9abc-def123456789.pdf?X-Amz-Expires=3600&...",
    "filename": "Aircraft Safety Protocols Manual v2.1.pdf",
    "fileSize": 2048576,
    "expiresAt": "2024-10-01T11:15:00Z"
  }
}
```

#### Get File Information

**Endpoint**: `GET /api/aerotech-manufacturing/assets/{assetId}/file-info`

```json
{
  "success": true,
  "data": {
    "id": 1,
    "originalName": "Aircraft Safety Protocols Manual v2.1",
    "filename": "a8b9c1d2-e3f4-5678-9abc-def123456789.pdf",
    "filetype": "pdf",
    "fileSize": 2048576,
    "uploadedAt": "2024-10-01T10:30:00Z",
    "description": "Comprehensive safety manual covering all PPE requirements"
  }
}
```

### User Management Examples

#### Create Additional Users

**Endpoint**: `POST /api/aerotech-manufacturing/users/`

```json
{
  "userName": "mike.johnson",
  "fullName": "Mike Johnson",
  "userEmail": "m.johnson@aerotech.com",
  "password": "TrainingUser2024!",
  "admin": false
}
```

#### List Tenant Users

**Endpoint**: `GET /api/aerotech-manufacturing/users/`

**Response**:
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "userName": "john.smith",
      "fullName": "John Smith",
      "userEmail": "j.smith@aerotech.com",
      "admin": true,
      "createdAt": "2024-10-01T10:00:00Z"
    },
    {
      "id": 2,
      "userName": "mike.johnson",
      "fullName": "Mike Johnson",
      "userEmail": "m.johnson@aerotech.com",
      "admin": false,
      "createdAt": "2024-10-01T10:45:00Z"
    }
  ]
}
```

---

## Common Workflows

### Workflow 1: Complete Training Module Setup

1. **Create Training Program** → Get program ID
2. **Create Learning Paths** → Associate with program
3. **Upload Assets** (PDFs, videos, 3D models) → Get asset IDs
4. **Create Materials** → Link assets to materials
5. **Add Material Details** (timestamps, checklist items, etc.)
6. **Associate Materials with Learning Paths**

### Workflow 2: Asset Organization and Sharing

#### For OwnCloud Deployments (with sharing support):

**Create Share Link**:

**Endpoint**: `POST /api/aerotech-manufacturing/assets/{assetId}/share`

```json
{
  "shareType": "public_link",
  "permissions": "read",
  "expirationDate": "2024-12-31T23:59:59Z",
  "password": "training2024"
}
```

**Response**:
```json
{
  "success": true,
  "data": {
    "shareId": "abc123def456",
    "shareUrl": "http://localhost:8080/s/abc123def456",
    "expirationDate": "2024-12-31T23:59:59Z",
    "hasPassword": true
  }
}
```

### Workflow 3: Content Retrieval for XR Applications

#### Get Complete Training Program Structure

**Endpoint**: `GET /api/aerotech-manufacturing/programs/{programId}`

**Response includes**:
- Program details
- All learning paths (ordered)
- All materials per learning path
- Asset download URLs
- Material-specific data (timestamps, checklists, etc.)

#### Optimized Asset Loading

1. **Get Program Structure** → Identify required assets
2. **Batch Download URLs** → Get all download URLs at once
3. **Parallel Asset Downloads** → Download assets in parallel
4. **Cache Management** → Store locally with expiration tracking

### Workflow 4: Progress Tracking Integration

For integration with XR training platforms:

#### Track Material Completion

```json
{
  "userId": 2,
  "materialId": 5,
  "completedAt": "2024-10-01T14:30:00Z",
  "timeSpent": 1800,
  "score": 85
}
```

#### Generate Training Reports

```json
{
  "userId": 2,
  "programId": 1,
  "progress": {
    "completedPaths": 2,
    "totalPaths": 3,
    "completedMaterials": 8,
    "totalMaterials": 12,
    "overallScore": 87.5
  }
}
```

---

## Best Practices

### Asset Organization
- **Consistent Naming**: Use descriptive, version-controlled filenames
- **File Sizes**: Optimize videos/3D models for XR platform requirements
- **Metadata**: Include comprehensive descriptions for searchability

### API Integration
- **Error Handling**: Always check response success status
- **Rate Limiting**: Implement delays for bulk operations
- **Caching**: Cache download URLs until expiration
- **Pagination**: Implement pagination for large result sets

### Security Considerations
- **HTTPS**: Use HTTPS in production environments
- **Input Validation**: Validate all user inputs
- **File Scanning**: Scan uploaded files for malware
- **Access Control**: Implement proper user authorization

---

## Troubleshooting Common Issues

### Asset Upload Failures
```bash
# Check asset file size and type
curl -X GET "http://localhost:5286/api/aerotech-manufacturing/assets/{id}/file-info"

# Verify storage connectivity
docker-compose logs training-repo
```

### Database Connection Issues
```bash
# Check tenant database exists
docker-compose exec mariadb mysql -u root -p -e "SHOW DATABASES LIKE 'xr50_tenant_%';"

# Verify tenant configuration
curl -X GET "http://localhost:5286/api/tenants/aerotech-manufacturing"
```

### Storage Backend Problems
```bash
# For S3: Test AWS credentials
aws s3 ls s3://xr50-tenant-aerotech-manufacturing/

# For OwnCloud: Check container status
docker-compose logs owncloud
```

---

*This usage guide demonstrates practical implementation of the XR5.0 Training Asset Repository for real-world training scenarios. Adapt the examples to your specific use case and storage requirements.*