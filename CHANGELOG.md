# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased] - 2025-11-20

### Changed - Binary Stream File Type Detection for Assets

#### Summary
Asset creation now uses **magic bytes detection** by reading the actual binary file content instead of relying on file extensions or MIME type headers. This provides secure, accurate asset type classification that cannot be spoofed by renaming files or manipulating headers.

#### How It Works

When a file is uploaded via `IFormFile`, the system:
1. **Reads the first 12 bytes** of the file stream
2. **Checks file signatures (magic bytes)** against known patterns
3. **Determines both filetype and AssetType** from the binary signature
4. **Resets stream position** to allow normal file processing
5. **Cannot be spoofed** - detection is based on actual file content, not headers or extensions

#### Supported File Signatures

**Images:**
- PNG: `0x89 0x50 0x4E 0x47` → png
- JPEG: `0xFF 0xD8 0xFF` → jpg
- GIF: `0x47 0x49 0x46` (GIF87a/GIF89a) → gif
- BMP: `0x42 0x4D` (BM header) → bmp
- WebP: `RIFF....WEBP` → webp

**Videos:**
- MP4/MOV: `ftyp` box with brand codes (isom, mp41, qt) → mp4 or mov
- AVI: `RIFF....AVI` → avi
- WebM/MKV: `0x1A 0x45 0xDF 0xA3` (EBML) → webm

**Documents:**
- PDF: `0x25 0x50 0x44 0x46` (%PDF) → pdf

**Unity:**
- Unity Bundle: `UnityFS` header → unity
- Unity Asset: `UnityWeb` header → unity

#### Security Benefits

- **Cannot rename malicious files** to bypass detection (e.g., rename virus.exe to image.png)
- **Cannot manipulate MIME type headers** to trick the system
- **Verifies actual file content** before storage and processing
- **Logs unknown signatures** for security monitoring

#### API Impact

**No breaking changes** - the API contract remains the same, but:
- **More secure** - file type verified from binary content
- **More accurate** - works even with wrong extensions or missing MIME types
- **Better logging** - unknown file signatures are logged for investigation

#### Example Logs

```
Detected file type from binary stream for asset screenshot.png: Type=Image, Filetype=png
Detected file type from binary stream for training-video.mp4: Type=Video, Filetype=mp4
Unknown file signature: 0x4D 0x5A 0x90 0x00 0x03 0x00 0x00 0x00
```

#### Affected Files
- `Services/XR50AssetService.cs:119-214` - Added `DetectFileTypeFromStream()` method with magic bytes detection
- `Services/XR50AssetService.cs:246-278` - Updated `CreateAssetAsync()` to use binary stream detection
- `Services/XR50AssetService.cs:466-500` - Updated `UploadAssetAsync()` to use binary stream detection
- `Models/Asset.cs:42-58` - Removed hardcoded defaults (detection is always performed)

#### Migration Notes

Existing assets are not affected - their type assignments remain valid. New uploads will use binary stream detection to accurately determine file types based on content, not metadata.

### Added - Enhanced Training Program Responses

#### Summary
Training program GET responses now include complete learning path details with materials, making it easier for the GUI to display full program structure without additional API calls.

#### Enhanced Response Format

**Before:**
```json
{
  "learning_path": [
    {
      "id": "1",
      "learningPathName": "Path 1",
      "description": "..."
    }
  ]
}
```

**After:**
```json
{
  "learning_path": [
    {
      "id": "1",
      "learningPathName": "Path 1",
      "description": "...",
      "inherit_from_program": true,
      "materials": [
        {
          "id": 10,
          "name": "Video Lesson",
          "type": "video",
          "description": "..."
        },
        {
          "id": 15,
          "name": "Quiz",
          "type": "quiz",
          "description": "..."
        }
      ]
    }
  ]
}
```

#### Improved PUT Endpoint

The training program PUT endpoint now accepts the same format as GET responses:
- ✅ Accepts learning paths as full objects (from GET) or simple IDs
- ✅ Accepts materials as full objects or simple IDs
- ✅ String ID support (UUID-ready)
- ✅ Extracts IDs from objects automatically

**You can now PUT exactly what you GET!**

#### Affected Files
- `Models/DTOs/XR50TrainingProgramDtos.cs:182-191` - Enhanced `LearningPathResponse` with materials
- `Services/XR50TrainingProgramService.cs:968-990` - Populate materials in learning paths
- `Controllers/XR50TrainingProgramController.cs:73-186` - Redesigned PUT with JSON parsing

### Added - Asset Type Classification ⚠️ BREAKING CHANGE

#### Summary
Added a `Type` field to the Asset model for high-level categorization (Image, PDF, Video, Unity) separate from the specific `Filetype` field (mp4, png, pdf, etc.).

**This is a breaking change** - the database schema has been updated and requires migration for existing tenants.

#### Schema Changes

**New Column:**
- `Assets.Type` (int, NOT NULL, DEFAULT 0)
- Maps to `AssetType` enum: 0=Image, 1=PDF, 2=Video, 3=Unity

**Migration Required:**
Run the migration for each tenant:
```csharp
await _tableCreator.MigrateAssetTypeColumnAsync(tenantName);
```

The migration will:
1. Add the `Type` column if it doesn't exist
2. Automatically infer `Type` from existing `Filetype` values
3. Set appropriate defaults for all existing assets

#### API Changes

Asset responses now include both `type` and `filetype`:
```json
{
  "id": 3,
  "filename": "video.mp4",
  "filetype": "mp4",
  "type": 2,  // NEW: 2 = Video (AssetType enum)
  "src": "http://...",
  "url": "http://..."
}
```

**AssetType Enum Values:**
- `0` - Image (png, jpg, jpeg, gif, bmp, svg, webp)
- `1` - PDF (pdf)
- `2` - Video (mp4, avi, mov, wmv, flv, webm, mkv)
- `3` - Unity (unity, unitypackage, bundle)

#### Affected Files
- `Models/Asset.cs:29-35,50-51` - Added `AssetType` enum and `Type` property
- `Services/XR50AssetService.cs:115-137` - Added `InferAssetTypeFromFiletype()` helper
- `Services/XR50AssetService.cs:80-101,350-371` - Updated asset creation to set `Type`
- `Services/XR50ManualTableCreator.cs:18,253,481-555` - Added migration method and updated table schema

#### Default Values

When asset type or filetype are not provided, both default to PDF:
- `Type` → `AssetType.PDF` (1)
- `Filetype` → `"pdf"`

This ensures compatibility with GUI requests that may not specify these fields.

#### Migration Instructions

**For New Tenants:**
No action needed - the `Type` column is included in table creation.

**For Existing Tenants:**
Use the tenant admin interface or programmatically call:
```csharp
var migrated = await _tableCreator.MigrateAssetTypeColumnAsync("tenant-name");
```

### Added - Material-to-Material Relationships

#### Summary
Implemented full support for material-to-material relationships, allowing materials to be organized in hierarchical structures. Materials can now contain other materials (e.g., a module containing lessons, a lesson containing videos and quizzes).

#### New Controller Endpoints (XR50MaterialsController.cs)

**Material Relationship Management:**
- `POST /api/{tenantName}/materials/{parentId}/assign-material/{childId}` - Assign a child material to a parent material
- `DELETE /api/{tenantName}/materials/{parentId}/remove-material/{childId}` - Remove material relationship
- `GET /api/{tenantName}/materials/{materialId}/children` - Get all child materials
- `GET /api/{tenantName}/materials/{materialId}/parents` - Get all parent materials
- `GET /api/{tenantName}/materials/{materialId}/hierarchy` - Get complete material hierarchy tree

#### Response Format Changes

All GET material detail endpoints now include a `related` array containing simplified child material references:

```json
{
  "id": "5",
  "name": "Parent Material",
  "type": "image",
  "related": [
    {
      "id": "2",
      "name": "Child Material",
      "description": "Description"
    }
  ]
}
```

#### POST Material Creation with Relationships

The comprehensive POST endpoint (`POST /api/{tenantName}/materials`) now supports creating materials with relationships in a single request. Include a `related` array in your JSON payload:

```json
{
  "name": "Module 1",
  "type": "workflow",
  "description": "Complete training module",
  "config": {
    "steps": [...]
  },
  "related": [
    {"id": "10"},
    {"id": "15"},
    {"id": "23"}
  ]
}
```

The system will:
- Create the parent material first
- Automatically assign child materials in the order specified
- Set display order based on array position
- Continue processing even if individual relationship assignments fail (with warnings logged)

**Note**: The `related` array expects existing material IDs. Child materials must be created before the parent if using this approach, or use the dedicated relationship endpoints after creation.

#### PUT Material Update with Relationships

The PUT endpoint (`PUT /api/{tenantName}/materials/{id}`) has been redesigned to accept JSON in the same format as GET responses:

**Key Features:**
- **String ID Support**: Route parameter accepts string IDs (e.g., `/materials/5` or future `/materials/uuid-here`)
- **Flexible ID Format**: JSON body can use string or integer IDs
- **Relationship Management**: Include `related` array to update relationships
- **Idempotent Behavior**: Replaces material properties and synchronizes relationships

**Example PUT Request:**
```json
PUT /api/{tenantName}/materials/5
{
  "id": "5",
  "name": "Updated Material",
  "type": "image",
  "related": [
    {"id": "2"},
    {"id": "10"}
  ]
}
```

**Relationship Synchronization:**
- Removes relationships not in the `related` array
- Adds new relationships from the `related` array
- Preserves relationships already present
- Updates display order based on array position

**Future-Proof Design:**
This implementation prepares for UUID migration by treating IDs as strings in the API layer while maintaining integer compatibility in the database layer.

#### Features
- **Circular Reference Prevention**: System prevents creating circular material dependencies
- **Display Ordering**: Support for custom ordering of child materials via `displayOrder` parameter
- **Relationship Types**: Flexible relationship types (e.g., "contains", "prerequisite")
- **Hierarchy Queries**: Retrieve complete material trees with configurable depth limits

#### Affected Files
- `Controllers/XR50MaterialsController.cs:2757-2936` - New relationship endpoints and helper methods
- `Controllers/XR50MaterialsController.cs:2972-3035` - `ProcessRelatedMaterialsAsync()` helper for POST
- `Controllers/XR50MaterialsController.cs:125-509` - Updated all detail methods to include `related` array
- `Controllers/XR50MaterialsController.cs:879,1302,1385,1474,1571,1716,1762` - Added relationship processing to all POST material creation methods
- `Controllers/XR50MaterialsController.cs:2234-2378` - Redesigned PUT endpoint with JSON parsing and relationship management
- `Services/XR50MaterialsService.cs:113-120` - Added interface methods to `IMaterialService`
- `Services/XR50MaterialsService.cs:1959-2183` - Service layer methods (already existed, now exposed via controller)

### Fixed - Quiz Question Creation Bug

#### Summary
Fixed exception when creating quizzes with multiple questions. The bug occurred when Entity Framework's change tracker tried to save answers from multiple questions in a single batch.

#### Technical Fix
Modified quiz creation logic to save answers immediately after adding them for each question, rather than batching all answers at the end.

#### Affected Files
- `Services/XR50MaterialsService.cs:307,325-326` - `CreateMaterialAsyncComplete()` for QuizMaterial case
- `Services/XR50MaterialsService.cs:1433,1444-1445` - `CreateQuizWithQuestionsAsync()`

## [Unreleased] - 2025-11-18

### Changed - PUT Methods Redesign (Delete-and-Recreate Pattern)

#### Summary
Redesigned all PUT methods to use a clean **delete-and-recreate pattern** instead of Entity Framework change tracking. This provides predictable, idempotent behavior where the client's request represents the complete final state of the resource.

#### Affected Files

**Services Layer:**
- `Services/XR50AssetService.cs:172` - `UpdateAssetAsync()`
- `Services/XR50LearningPathService.cs:232` - `UpdateLearningPathAsync()`
- `Services/XR50MaterialsService.cs:371` - `UpdateMaterialAsync()`
- `Services/XR50TrainingProgramService.cs:377` - `UpdateTrainingProgramAsync()`

#### Technical Implementation

All PUT methods now follow this pattern:

1. **Find Existing**: Query for existing entity, throw `KeyNotFoundException` if not found
2. **Validate**: Type validation for polymorphic entities (Materials)
3. **Preserve Metadata**: Save `Created_at` timestamp from existing entity
4. **Delete**: Remove old entity (cascades to child collections and junction tables)
5. **Recreate**: Add new entity with same ID and complete state from request
6. **Transaction**: Wrap in database transaction with rollback on failure

#### Detailed Changes

##### 1. Asset Service (`XR50AssetService.cs`)

**Before:**
```csharp
public async Task<Asset> UpdateAssetAsync(Asset asset)
{
    using var context = _dbContextFactory.CreateDbContext();
    context.Assets.Update(asset);
    await context.SaveChangesAsync();
    return asset;
}
```

**After:**
```csharp
public async Task<Asset> UpdateAssetAsync(Asset asset)
{
    using var context = _dbContextFactory.CreateDbContext();
    using var transaction = await context.Database.BeginTransactionAsync();

    try
    {
        var existing = await context.Assets.FindAsync(asset.Id);
        if (existing == null)
            throw new KeyNotFoundException($"Asset {asset.Id} not found");

        context.Assets.Remove(existing);
        await context.SaveChangesAsync();

        context.Assets.Add(asset);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
        return asset;
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

##### 2. Learning Path Service (`XR50LearningPathService.cs`)

**Before:**
```csharp
public async Task<LearningPath> UpdateLearningPathAsync(LearningPath learningPath)
{
    using var context = _dbContextFactory.CreateDbContext();
    context.Entry(learningPath).State = EntityState.Modified;
    await context.SaveChangesAsync();
    return learningPath;
}
```

**After:**
- Same delete-and-recreate pattern as Asset
- Wrapped in transaction
- Existence validation added
- Preserves `id`

##### 3. Material Service (`XR50MaterialsService.cs`)

**Before:**
```csharp
public async Task<Material> UpdateMaterialAsync(Material material)
{
    using var context = _dbContextFactory.CreateDbContext();
    material.Updated_at = DateTime.UtcNow;
    context.Entry(material).State = EntityState.Modified;
    await context.SaveChangesAsync();
    return material;
}
```

**After:**
- Added type validation: Cannot change material type (e.g., VideoMaterial → QuizMaterial)
- Preserves `Created_at` timestamp from existing entity
- Sets `Updated_at = DateTime.UtcNow` on new entity
- Delete cascades to child collections automatically:
  - `QuestionnaireEntries` (QuestionnaireMaterial)
  - `VideoTimestamps` (VideoMaterial)
  - `ChecklistEntries` (ChecklistMaterial)
  - `WorkflowSteps` (WorkflowMaterial)
  - `QuizQuestions` and nested `QuizAnswers` (QuizMaterial)
- Added helper method `GetChildCollectionCount()` for detailed logging

**New Type Validation:**
```csharp
if (existing.GetType() != material.GetType())
{
    throw new InvalidOperationException(
        $"Cannot change material type from {existing.GetType().Name} to {material.GetType().Name}");
}
```

##### 4. Training Program Service (`XR50TrainingProgramService.cs`)

**Before:**
```csharp
public async Task<TrainingProgram> UpdateTrainingProgramAsync(TrainingProgram program)
{
    using var context = _dbContextFactory.CreateDbContext();
    context.Entry(program).State = EntityState.Modified;
    await context.SaveChangesAsync();
    return program;
}
```

**After:**
- Preserves `Created_at` timestamp
- Delete cascades to junction tables:
  - `ProgramMaterial` (Program-to-Material associations)
  - `ProgramLearningPath` (Program-to-LearningPath associations)
- New entity includes complete `Materials` and `LearningPaths` collections from request
- Logs material and learning path counts

#### API Contract Changes

##### Breaking Change: Full State Replacement

Clients must now send **complete resource state** in PUT requests. Omitted fields/collections will not be preserved.

**Example - Updating Material with Child Collections:**

```json
PUT /api/tenant1/materials/123
{
  "id": 123,
  "Type": "quiz",
  "Name": "Updated Quiz Material",
  "Description": "Complete quiz",
  "Unique_id": 12345,
  "Questions": [
    {
      "QuestionNumber": 1,
      "QuestionType": "multiple-choice",
      "Text": "What is 2+2?",
      "Score": 10,
      "Answers": [
        { "Text": "3", "IsCorrect": false, "DisplayOrder": 1 },
        { "Text": "4", "IsCorrect": true, "DisplayOrder": 2 },
        { "Text": "5", "IsCorrect": false, "DisplayOrder": 3 }
      ]
    }
  ]
}
```

**Example - Updating Program with Associations:**

```json
PUT /api/tenant1/programs/456
{
  "id": 456,
  "Name": "Updated Training Program",
  "Description": "Complete program description",
  "min_level_rank": 1,
  "max_level_rank": 5,
  "required_upto_level_rank": 3,
  "Materials": [
    { "TrainingProgramId": 456, "MaterialId": 123 },
    { "TrainingProgramId": 456, "MaterialId": 789 }
  ],
  "LearningPaths": [
    { "TrainingProgramId": 456, "LearningPathId": 10 }
  ]
}
```

#### Benefits

1. **Predictability**: No hidden EF Core change tracking behavior
2. **Idempotency**: Multiple identical PUT requests produce identical results
3. **Simplicity**: Clear delete-then-add logic, easy to debug
4. **True REST Semantics**: PUT replaces entire resource
5. **Atomicity**: All changes wrapped in transactions
6. **Natural Cascades**: Database cascade deletes work automatically
7. **Type Safety**: Material type validation prevents invalid transformations

#### Error Handling

All PUT methods now throw:
- `KeyNotFoundException`: When resource with specified ID doesn't exist (caught by controller → 404)
- `InvalidOperationException`: For business rule violations like material type changes (caught by controller → 400)
- Database exceptions wrapped in transaction rollback

#### Preserved Metadata

- `Created_at` timestamps are always preserved from original entity
- `Updated_at` is set to `DateTime.UtcNow` on Materials (automatic via DbContext for other entities)
- Entity IDs remain unchanged

#### Database Cascade Behavior

The implementation relies on existing EF Core cascade delete configurations in `XR50DbContext.cs`:

**Material cascades (lines 228-260):**
- QuestionnaireMaterial → QuestionnaireEntries
- VideoMaterial → VideoTimestamps
- ChecklistMaterial → ChecklistEntries
- WorkflowMaterial → WorkflowSteps
- QuizMaterial → QuizQuestions → QuizAnswers

**Junction table cascades:**
- TrainingProgram deletion cascades to ProgramMaterial and ProgramLearningPath
- Material deletion cascades to ProgramMaterial and MaterialRelationship

#### Migration Notes

**For Frontend/API Clients:**

1. Update PUT request payloads to include complete resource state
2. Include all child collections (Questions, Entries, Steps, etc.) even if unchanged
3. Include all junction table relationships (Materials, LearningPaths) for Programs
4. Expect `KeyNotFoundException` (404) for missing resources instead of silent creation
5. Expect `InvalidOperationException` (400) when attempting to change Material types

**For Testing:**

Test that PUT requests properly:
- Replace all fields
- Remove child collections not in request
- Remove associations not in request
- Preserve `Created_at`
- Update `Updated_at`
- Rollback on errors

#### Related Configuration

**DbContext Audit Fields** (`XR50DbContext.cs:306-337`):
- Automatically sets `Created_at` on Materials when `EntityState.Added`
- Automatically sets `Updated_at` on Materials when `EntityState.Modified`
- This automatic behavior still applies with delete-and-recreate pattern

**JSON Serialization** (`Program.cs`):
- `ReferenceHandler.IgnoreCycles` prevents circular reference issues
- Navigation properties marked with `[JsonIgnore]` to prevent unwanted serialization

---

## Decision Rationale

### Why Delete-and-Recreate?

**Considered Approaches:**

1. **Entity Framework Change Tracking** (original implementation)
   - **Pros**: Less code, EF handles change detection
   - **Cons**: Unpredictable with detached entities, complex debugging, unclear what gets updated

2. **Manual Property Updates** (traditional approach)
   - **Pros**: Explicit control over each field
   - **Cons**: Must track every property change, easy to miss fields, complex for nested objects

3. **Delete-and-Recreate** (chosen approach)
   - **Pros**: Simple, predictable, true PUT semantics, natural cascades
   - **Cons**: Requires complete state in request (this is actually a feature, not a bug)

**Decision**: Option 3 chosen for alignment with REST principles and operational clarity.

---

## Future Considerations

- Consider adding PATCH endpoints for partial updates if needed
- Monitor database performance with large child collections
- Consider optimizing transaction scope if performance issues arise
- May want to add RowVersion/Timestamp fields for true optimistic concurrency control
- Consider audit logging to track before/after states for compliance

---

## References

- Original discussion: "Isn't it way easier (&safer) to basically delete the object being modified and then recreate it while just keeping the ID?"
- Design principle: "Option A: Simple Delete-Recreate - Client sends COMPLETE resource state"
- Agreement: "I strongly favor option A too. It makes sense to have a clear alignment vs each side having different understanding of what changed."
