# Agent development guide

This file is the vendor-neutral source of project guidance for coding agents and
human contributors. Tool-specific configuration may supplement it locally, but
must not replace or contradict it.

## Project

XR5.0 Training Asset Repository is a multi-tenant ASP.NET Core service for XR
training assets, materials, programs, learning paths, users, and progress.

- Application target: .NET 8
- Test target: .NET 10
- Persistence: EF Core 8 with Pomelo MySQL/MariaDB
- Tenant model: one database per tenant
- Storage: S3-compatible storage or OwnCloud through `IStorageService`
- API documentation: Swagger/OpenAPI

Start with `README.md` and `docs/README.md`. Architecture details are in
`docs/architecture.md`; test setup is in `docs/guides/testing.md`.

## Working conventions

- Preserve unrelated working-tree changes. Do not rewrite or delete user work.
- Prefer small, targeted changes and follow the patterns in adjacent code.
- Controllers are tenant-scoped through the `{tenantName}` route parameter.
- Tenant names must match `^[a-zA-Z0-9_-]+$` and be 3-50 characters. The name becomes the
  per-tenant database as `xr50_tenant_{name}` with any other character folded to `_`, so distinct
  names can still collide: `foo-bar` and `foo_bar` both derive `xr50_tenant_foo_bar`, and
  `Foo_Bar` folds onto it as well on a server with `lower_case_table_names=1`. Tenant creation
  refuses a colliding name with `409` by checking the derived database before provisioning
  anything. `Services/XR50TenantDatabase.cs` is the single source of truth for this mapping:
  derive database names through `SchemaFor` and answer "is this name already taken" through
  `CollisionKeyFor`, never by rebuilding the string inline. Use underscores in examples,
  fixtures and sample payloads. S3 bucket names are a separate field and keep hyphens.
- Create tenant contexts through `IXR50TenantDbContextFactory`; never share a
  context between tenants, and dispose contexts with `using`.
- Use structured logging with semantic placeholders. Do not add emoji to logs
  or source comments.
- Wrap multi-step persistence changes in a transaction and roll back on failure.
- Reset seekable upload streams after inspecting magic bytes.
- Check material hierarchies for cycles before adding relationships.
- Address stored files by `Asset.ResolvedStorageKey`, never by `Asset.Filename`.
  Filenames are display metadata, are not unique, and can change; keys are
  content-addressed and fixed. Any new path that writes file content must hash
  it and record a key, or the file becomes unreachable to deduplication and
  collides with same-named assets. See `docs/architecture.md` "Object Layout".

## Material model

Materials use table-per-hierarchy inheritance in `Models/Material.cs`. The
`Type` enum is persisted by ordinal value, so new values must be appended, never
inserted or reordered.

Adding a material type usually requires coordinated updates to:

- `Models/Material.cs`
- `Data/XR50DbContext.cs`
- `Services/Materials/MaterialService.cs`
- the type-specific service interface and implementation
- every relevant dispatch point in `Controllers/XR50MaterialsController.cs`
- a new EF Core migration (see "Schema migrations" below)
- dependency injection in `Program.cs`
- the material test factory and focused tests

## Schema migrations

The committed EF Core migrations under `Migrations/` are the schema. `XR50TrainingContext`
(`Migrations/Training`) describes every tenant database and the "default" tenant in the base
database; `XR50RegistryContext` (`Migrations/Registry`) owns the central `XR50TenantRegistry`.
To change the schema: change the model, run `dotnet tool restore && dotnet build`, then
`dotnet ef migrations add <Name> --context XR50TrainingContext --output-dir Migrations/Training --no-build`
(or `--context XR50RegistryContext --output-dir Migrations/Registry`), review the generated
migration and commit it together with the snapshot. Never write DDL inline in a service; the
hermetic `MigrationModelDriftTests` fails when the model and the snapshot disagree. The Baseline
is the only hand-edited migration (its `Up()` omits the foreign keys deployed databases never
had); do not edit generated migrations otherwise. The `Material.Type` enum stays append-only,
and index and constraint names must stay under MySQL's 64-character limit.

Migrations run at startup against the base database and every registered tenant
(`Database:MigrateOnStartup`, default true), on tenant creation, through
`dotnet XR50TrainingAssetRepo.dll migrate ...`, and through the system-admin endpoints
`GET /api/troubleshooting/migration-status`, `POST migrate/{tenant}` and `POST migrate-all`.
Databases provisioned before migrations existed are adopted automatically. The full model,
states and upgrade procedure are in `docs/architecture.md` under "Database Migrations".

## Verification

Use the smallest adequate checks and stop to diagnose failures before expanding
the test scope.

1. Build: `dotnet build`
2. Hermetic tests: `dotnet test tests/XR50TrainingAssetRepo.Tests/XR50TrainingAssetRepo.Tests.csproj`
3. Functional tests, when the change crosses real infrastructure: first check
   `http://localhost:5286/health`, then run the matching script in
   `tests/functional`.
4. For a narrow live API behavior not covered by tests, use a focused `curl`
   probe and report the request, expected result, actual result, and cleanup.
5. For a schema migration: `scripts/db-backup.sh` first, `migrate --status` before and
   after, and the schema-parity probe described in the verification guide.

`./scripts/verify-e2e.sh` runs rungs 1-3 in order and can start the sandbox stack
with `--up`; `--help` lists the rungs and flags. The full procedure, including how
to build a targeted probe with a control group and verified cleanup, is in
[docs/guides/verification-workflow.md](docs/guides/verification-workflow.md).
Read that guide before verifying a change that touches persistence, storage,
authorization, or tenant provisioning - the hermetic suite stubs the database and
cannot catch those.

Functional test routing:

| Area | Command from `tests/functional` |
|---|---|
| AI Assistant and status sync | `npm run test:ai-assistant` |
| Materials, assets, type hierarchy | `npm run test:materials` and `npm run test:hierarchy` |
| Tenant provisioning | `npm run test:tenant` |
| Storage backends | `npm run test:storage` |
| Programs and learning paths | `npm run test:programs` and `npm run test:hierarchy` |
| Users | `npm run test:users` |
| Authentication | `npm run test:auth` and `npm run test:health` |
| Schema migrations | `npm run test:migrations` |
| Cross-cutting startup or middleware | `npm run test:health` |

The functional suite requires a running stack. If the health endpoint is not
available, report that the functional check was skipped; do not claim it passed.
Do not expose secrets from `.env` in commands, logs, or reports. Clean up any
tenant, material, asset, or external collection created by a live probe.

## Documentation and agent portability

- Keep shared project knowledge here or in `docs/`, using ordinary Markdown and
  executable commands rather than vendor-specific slash commands.
- Do not require a particular model, agent vendor, IDE, or proprietary tool.
- Vendor adapters such as `.claude/`, `.agents/skills/`, or editor settings are optional
  and must point back to this file. They are tracked in the repository so that a
  contributor gets the same workflow whichever agent they use, but they carry only
  trigger conditions and pointers - never project knowledge that exists nowhere
  else. Deleting every adapter directory must lose no information, and an agent
  with no adapter at all still finds the workflow through this file.

  | Adapter | Status |
  |---|---|
  | `.claude/skills/` (`e2e-verify`, `e2e-probe`) | Present |
  | `.agents/skills/` (`e2e-verify`, `e2e-probe`) | Present for Codex |

- When behavior or commands change, update this guide and the relevant project
  documentation together. A change to `.claude/` or `.agents/skills/` that does not touch
  `docs/guides/` is either adapter packaging or trigger wording only, or it is a mistake.
