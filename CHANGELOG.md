# Changelog

All notable changes to this project will be documented in this file.

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
- Preserves `learningPath_id`

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
  "UniqueId": 12345,
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
