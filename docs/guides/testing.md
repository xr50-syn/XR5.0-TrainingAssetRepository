# Testing Guide

How to run and write tests for the XR5.0 Training Asset Repository.

For *which* checks a given change needs, and how to probe behavior no suite covers yet, see
[Verification Workflow](verification-workflow.md). This guide covers the suites themselves.

## Test suites

| Suite | Technology | Location | Needs infrastructure |
|-------|------------|----------|----------------------|
| Hermetic tests | .NET 10 / xUnit | `tests/XR50TrainingAssetRepo.Tests/` | no |
| Functional tests | Node.js 20+ / Jest | `tests/functional/` | yes - running API, MariaDB, storage, Keycloak |

The application targets .NET 8; the test project targets .NET 10.

## Quick start

```bash
# Hermetic tests - no stack required
dotnet test tests/XR50TrainingAssetRepo.Tests/XR50TrainingAssetRepo.Tests.csproj

# Functional tests - requires a running stack
cd tests/functional && npm install && npm test

# Or run the whole ladder, starting the stack if needed
./scripts/verify-e2e.sh --up
```

## Hermetic tests

Fast, deterministic, no external dependencies. They use an in-memory EF Core provider and a mock
storage service, so they cover logic and controller behavior but **not** anything that depends on
a real database: identifier handling, collation, connection-string derivation, and applying
migrations all need the functional suite or a targeted probe. Drift between the EF model and the
committed migrations is the exception: `Migrations/MigrationModelDriftTests` catches it without a
database, and `Migrations/SchemaMigratorTests` covers the migrator's state detection and
adoption order against fakes.

```
tests/XR50TrainingAssetRepo.Tests/
├── Controllers/     # controller-level unit tests
├── Factories/       # MaterialFactory and other test builders
├── Fixtures/        # WebApplicationFixture, HubAuthWebApplicationFixture, TestAuthHandler
├── Integration/     # in-process API tests through the fixtures
├── Migrations/      # model-drift guard, migrator orchestration with fakes, migrate CLI
├── Services/        # service-level unit tests
└── Smoke/           # health checks
```

```bash
# Everything
dotnet test tests/XR50TrainingAssetRepo.Tests/XR50TrainingAssetRepo.Tests.csproj

# Filter by name
dotnet test --filter "FullyQualifiedName~TenantDatabaseNaming"

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

If `dotnet test` fails with **MSB3030** about a missing `MvcTestingAppManifest.json` under a deeply
nested path, the test project's build output has been copied into itself. Delete the artifacts and
rerun; both directories are gitignored and nothing in source needs changing:

```bash
rm -rf tests/XR50TrainingAssetRepo.Tests/bin tests/XR50TrainingAssetRepo.Tests/obj
```

## Functional tests

### Structure

```
tests/functional/
├── config.js                  # environment configuration
├── setup.js                   # global setup: creates the per-run tenant
├── teardown.js                # global teardown: deletes it again
├── testSequencer.js           # forces suite order
├── helpers/
│   ├── api-client.js          # authenticated HTTP client
│   └── test-data.js           # payload factories
└── suites/
    ├── 01-health.test.js      # API health checks
    ├── 02-auth.test.js        # authentication
    ├── 03-tenant.test.js      # tenant CRUD
    ├── 04-storage.test.js     # storage validation
    ├── 05-materials.test.js   # material CRUD
    ├── 06-hierarchy.test.js   # material relationships
    ├── 07-programs.test.js    # training programs
    ├── 08-users.test.js       # user management
    ├── 09-ai-assistant.test.js # AI Assistant material, both create modes
    └── 10-migrations.test.js  # schema migration status and idempotence, system-admin only
```

Each run provisions its own tenant (`test_<timestamp>`) in `setup.js` and deletes it in
`teardown.js`. A leftover `test_*` tenant means a run was aborted; remove it before the next run.

### Running

```bash
cd tests/functional

npm test                 # all suites, in order
npm run test:health      # 01
npm run test:auth        # 02
npm run test:tenant      # 03
npm run test:storage     # 04
npm run test:materials   # 05
npm run test:hierarchy   # 06
npm run test:programs    # 07
npm run test:users       # 08
npm run test:ai-assistant # 09
npm run test:migrations  # 10
npm run test:verbose     # all suites, verbose
```

### Configuration

All settings come from environment variables; defaults are in `config.js`.

| Variable | Default | Description |
|----------|---------|-------------|
| `API_URL` | `http://localhost:5286` | API base URL |
| `KEYCLOAK_URL` | `http://localhost:8180` | Keycloak server |
| `KEYCLOAK_REALM` | `xr50` | Keycloak realm |
| `KEYCLOAK_CLIENT` | `xr50-training-app` | Client ID |
| `KEYCLOAK_CLIENT_SECRET` | _(empty)_ | Client secret, if the client is confidential |
| `TEST_USER` / `TEST_PASSWORD` | `testuser` / `testuser123` | Ordinary member identity |
| `ADMIN_USER` / `ADMIN_PASSWORD` | `sysadmin` / `sysadmin123` | Identity for tenant-scoped suites |
| `SYSADMIN_USER` / `SYSADMIN_PASSWORD` | `sysadmin` / `sysadmin123` | Tenant create/delete in setup and teardown |
| `TEST_TENANT` | _(generated)_ | Override the generated `test_<timestamp>` name |
| `EXISTING_TENANT` | _(empty)_ | Use a pre-existing tenant instead of creating one |
| `STORAGE_TYPE` | `S3` | Storage backend for generated tenant payloads (`S3` or `OwnCloud`) |
| `S3_BUCKET` | `xr50-test-verification` | Bucket for the generated tenant |
| `S3_REGION` | `eu-west-1` | Bucket region |
| `S3_ENDPOINT` | _(empty)_ | Custom S3 endpoint; set for MinIO |
| `REQUEST_TIMEOUT` | `10000` | Per-request timeout in ms |
| `NO_AUTH` | `false` | Skip authentication entirely |
| `DEBUG` | `false` | Log every request and response |
| `SKIP_CLEANUP` | `false` | Keep created resources for inspection |

`ADMIN_USER` defaults to the system admin because the suites run against a per-run tenant, and
tenant binding rejects a token whose `tenantName` claim does not match the route. Only the system
admin is exempt. To exercise tenant-level roles instead, point the suites at the seeded tenant:

```bash
EXISTING_TENANT=test_company ADMIN_USER=tenantadmin ADMIN_PASSWORD=tenantadmin123 npm test
```

### Common scenarios

```bash
# Debug a failing suite, keeping its resources
DEBUG=true SKIP_CLEANUP=true npm run test:materials

# Against the MinIO sandbox (compose publishes the MinIO API on 10000)
STORAGE_TYPE=S3 S3_ENDPOINT=http://localhost:10000 npm test

# Against an existing tenant
EXISTING_TENANT=test_company npm test
```

> `NO_AUTH=true` disables authentication for the suites. It makes every authorization assertion
> vacuous, so use it only to isolate a non-auth failure, and never as evidence that authorization
> works. The same applies to the server-side `IAM__AllowAnonymousInDevelopment` bypass, which
> `scripts/verify-e2e.sh` refuses to run against.

### Test data factories

`helpers/test-data.js` builds request payloads:

```javascript
const testData = require('./helpers/test-data');
```

| Group | Factories |
|---|---|
| Tenants | `createTenant`, `createS3Tenant`, `createOwnCloudTenant`, `createMinioTenant` |
| Materials | `createSimpleMaterial`, `createVideoMaterial`, `createVideoWithTimestamps`, `createChecklistMaterial`, `createWorkflowMaterial`, `createCompositeMaterial`, `createChatbotMaterial` |
| AI Assistant | `createAIAssistantMaterialEmpty`, `createAIAssistantMaterialWithConfigAssets`, `createAIAssistantMaterialWithTopLevelAssets`, `createAIAssistantMaterialWithLegacyIds` |
| Programs | `createTrainingProgram`, `createProgramWithPaths` |
| Users | `createTestUser`, `createAdminUser` |
| Files | `createTestTextFile`, `createTestImageFile` |
| Other | `TestResourceTracker`, `STORAGE_TYPE` |

### API client

`helpers/api-client.js` exports a singleton with authentication handled for you:

```javascript
const apiClient = require('../helpers/api-client');

await apiClient.authenticate(username, password);   // no-op when NO_AUTH=true

// Generic verbs
await apiClient.get(url); await apiClient.post(url, data);
await apiClient.put(url, data); await apiClient.delete(url);

// Tenants
await apiClient.listTenants(); await apiClient.getTenant(name);
await apiClient.createTenant(data); await apiClient.deleteTenant(name);
await apiClient.validateStorage(name); await apiClient.getStorageStats(name);

// Materials
await apiClient.listMaterials(); await apiClient.getMaterial(id);
await apiClient.getMaterialDetail(id); await apiClient.createMaterial(data);
await apiClient.updateMaterial(id, data); await apiClient.deleteMaterial(id);
await apiClient.getMaterialChildren(id);

// Uploads and diagnostics
await apiClient.uploadFile(url, filePath);
await apiClient.uploadBuffer(url, buffer, filename);
await apiClient.health(); await apiClient.swagger();

apiClient.logResponse(response, 'CONTEXT');   // for debugging a failure
```

## Writing tests

### Functional test pattern

```javascript
const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

describe('Material creation', () => {
  let createdId;

  beforeAll(async () => {
    if (!config.NO_AUTH) {
      await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
    }
  });

  afterAll(async () => {
    if (createdId && !config.SKIP_CLEANUP) {
      try { await apiClient.deleteMaterial(createdId); } catch { /* cleanup is best effort */ }
    }
  });

  test('creates a video material', async () => {
    const response = await apiClient.createMaterial(testData.createVideoMaterial());
    expect([200, 201]).toContain(response.status);
    createdId = response.data.id;
  });

  test('rejects a material with no name', async () => {
    const response = await apiClient.createMaterial({ description: 'no name' });
    expect([400, 422]).toContain(response.status);
  });
});
```

### Conventions

1. **Assert the contract, not the current output.** Integer ids serialize as JSON *strings*
   API-wide; `expect(Number(id))` contradicts that contract even when it happens to pass.
2. **Do not tolerate `401`/`403`.** Accepting them alongside success codes turns an authorization
   regression into a green run. Authenticate properly instead.
3. **Clean up in `afterAll`**, guarded by `SKIP_CLEANUP`, and track ids as you create them.
4. **Use underscores in tenant names.** Hyphens are accepted but fold to `_` in the derived
   database, so `foo-bar` and `foo_bar` collide - see the tenant naming rule in
   [AGENTS.md](../../AGENTS.md).
5. **Prefer a hermetic test.** If the behavior can be tested without infrastructure, write it in
   `tests/XR50TrainingAssetRepo.Tests/` instead - it runs in seconds and cannot flake.

### Debugging

- `DEBUG=true` logs every request and response.
- `SKIP_CLEANUP=true` leaves resources in place for inspection.
- `npm run test:verbose` gives per-test Jest output.
- Check the API side too: `docker compose logs training-repo`.

## Continuous integration

`.gitlab-ci.yml` currently runs GitLab SAST only - **no test stage runs automatically**. Both
suites are run locally or manually until that changes. A test stage would look roughly like:

```yaml
stages: [test]

hermetic-tests:
  stage: test
  image: mcr.microsoft.com/dotnet/sdk:10.0
  script:
    - dotnet test tests/XR50TrainingAssetRepo.Tests/XR50TrainingAssetRepo.Tests.csproj
```

The functional suite additionally needs MariaDB, storage and Keycloak reachable from the runner,
which is why it is not simply a second job: see `docker-compose.yaml` for what the stack requires.
