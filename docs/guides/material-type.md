# Material Type Changes

Use this procedure when creating or changing a material type. Read
[AGENTS.md](../../AGENTS.md) first, including schema migrations and authorization.
For request/response shapes, see [Materials API](../api/materials-api.md).
For an existing type, change only the affected surfaces; the checklist is not a
requirement to invent new services or tables.

## Model and persistence

Materials use TPH inheritance with a `Discriminator` column. The enum is named
`Type` in `Models/Material.cs` (usually aliased as `MaterialType`). Its integer
ordinals are persisted: append new values, never reorder or insert them.

- Add the derived entity and initialize its `Type`. Match adjacent types' naming
  and nullability conventions.
- In `Data/XR50DbContext.cs`, add the discriminator, column mappings, and any child
  relationships/indexes. Keep MySQL index and constraint names within 64 characters.
- Update `SetMaterialTypeFromClass()` in `Services/Materials/MaterialService.cs`.
- Direct enum serialization and the controller's snake_case detail representation
  are distinct. Do not assume `[EnumMember]` alone configures `JsonStringEnumConverter`.
  Test both the input parser and emitted response type.

The committed EF migrations are authoritative, not a manual table creator.
After changing the model:

```bash
dotnet tool restore
dotnet build
dotnet ef migrations add AddNewMaterialType --context XR50TrainingContext --output-dir Migrations/Training --no-build
```

Replace the migration name for the actual change. For central registry changes,
use `XR50RegistryContext` and `Migrations/Registry` instead. Review the generated
migration and snapshot together. Do not add inline DDL, manual ALTER helpers, or
edit old generated migrations. Check both fresh-tenant provisioning and upgrade
of an existing database using the schema procedure in
[Verification Workflow](verification-workflow.md#verifying-a-schema-migration).
Back up the authorized test databases before applying migrations.

## Service and controller checklist

Use an adjacent material service as the implementation reference. Create a
tenant context through `IXR50TenantDbContextFactory` and dispose it. Preserve
immutable identifiers and creation timestamps during updates, manage children
and relationships on deletion, and use transactions for multi-step writes.
Prevent hierarchy cycles. Register a new service/interface in `Program.cs`.

`Controllers/XR50MaterialsController.cs` has several independent dispatch paths;
updating one does not update the others. Search for the most similar existing
type and inspect every hit, including:

| Surface | What to inspect |
|---|---|
| Input validation | `ValidMaterialTypes`, type/discriminator parsing |
| Create dispatch | `PostMaterialDetailed`, `PostMaterialJson`, `PostMaterialDetailedWithAsset`, and the advanced path |
| Asset-backed creation | `ShouldCreateAsset`, `CreateMaterialWithAssetId`, type-specific creation helpers |
| Detail output | `GetCompleteMaterialDetails`, type-specific detail helper, `GetLowercaseType` |
| Entity parsing | `ParseMaterialFromJson` factory and both numeric maps (`type` and `materialType`) |
| Update | `ParseMaterialFromJsonForUpdate`, type-specific field merging and update dispatch |
| Other type lookups | `GetSystemTypeFromMaterialType`, hierarchy and list/detail projections |

Use the new enum's actual ordinal in numeric maps. Preserve omitted fields on
partial updates. Return the correct type-specific details, not the generic
fallback. Include `tenantName` in `CreatedAtAction` route values. Follow adjacent
ProblemDetails helpers for validation/not-found/server failures; do not expose
raw exceptions or secrets in responses.

A new controller also needs tenant policies, a Swagger group, and the appropriate
`DocInclusionPredicate` registration in `Program.cs`. A new storage backend belongs
behind `IStorageService`, with configuration and DI registration, not controller
branches that bypass the storage abstraction.

## Conditional patterns

- **Multiple assets:** follow existing JSON ID-list helpers where appropriate,
  but keep ingest jobs per `(material, asset)` so one asset can be processed for
  multiple materials independently.
- **Child entities:** map foreign keys and deletion behavior explicitly; exercise
  child creation, update, deletion, and relationship cleanup.
- **External chat providers:** `ai_assistant` and `innov_chatbot` are separate
  material types behind `Services/Chatbot/IChatbotProvider.cs`, not a single type
  with a provider flag. Register providers and resolve by `ProviderKey`.
- **Provider configuration:** trace tenant model, DTOs, controller, management
  service, and registry persistence. Migrate registry changes through EF. Never
  return provider tokens; expose a configured/not-configured indicator instead.
  Do not assume DataLens already supports per-tenant credentials.
- **Ingestion:** uploaded content goes through `IAssetContentReader` and storage
  keys, not display filenames or public URLs. Only reference-only assets use URL
  ingestion. DataLens is asynchronous and uses `AiStatusSyncService`; INNOV's
  synchronous upload does not need a copied poller. Map transport failures through
  the existing provider/controller contract, not unhandled exceptions.
- **Status and collection binding:** aggregate `notready | process | ready` from
  per-document jobs. DataLens material collections are tenant-scoped; preserve
  explicit and legacy bindings. See [AI Assistant Probe](ai-assistant-probe.md).

## Verification

Add a request builder in
`tests/XR50TrainingAssetRepo.Tests/Factories/MaterialFactory.cs` and focused service
or in-process API tests beside the existing material tests. Use fake providers,
InMemory tenant contexts, and mock storage for hermetic tests: they need no DB
credentials, cloud secrets, or real services.

Cover each supported create path, emitted type, type-specific details, numeric
parsing if supported, update field preservation, child/relationship cleanup,
invalid input, and authorization/tenant isolation. For external providers, test
ingest failure and status aggregation without calling a live provider.

Run build and hermetic tests, including `MigrationModelDriftTests`; then follow
the shared verification ladder for real persistence/storage and migrations.
Use `test:materials` and `test:hierarchy` for material changes, plus relevant
provider suites/probes. InMemory cannot verify MySQL DDL or collation.

Do not accept a historical fixed failure count as a passing baseline. Diagnose
current failures and provide evidence before calling them pre-existing.
Test commands, optional TRX output, and suite setup belong in [Testing](testing.md).
