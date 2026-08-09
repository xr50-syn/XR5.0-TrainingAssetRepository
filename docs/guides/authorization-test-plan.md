# Authorization test plan (standalone / no XR5.0 Hub)

Verification plan for the authorization work introduced in `0d2ea61` (Auth v1) through
`ed79954`. Scope is the **standalone deployment**: Keycloak JWT bearer, no XR5.0 Hub. The
Hub-specific paths are covered by the hermetic suite only and are out of scope here.

The change added a global authorization `FallbackPolicy` plus explicit policies on roughly
200 endpoints. The risk is two-sided and the plan has to test both directions:

- **Under-permissioning (the loud failure).** A legitimate tenant admin can no longer do
  something they could do before. Shows up as a `403` where the pilot expects `200`.
- **Over-permissioning (the quiet failure).** An endpoint was missed, or given a weaker
  policy than its siblings, and a plain member can reach it.

---

## 0. Two structural facts that shape everything below

**The standalone deployment only works in `ASPNETCORE_ENVIRONMENT=Development`.**
`AddJwtBearer` is registered inside `if (builder.Environment.IsDevelopment())`
(`Program.cs:91-93`), and the `XR50AuthSelector` policy scheme forwards to the Hub scheme
whenever the environment is not Development (`Program.cs:83-86`). A standalone stack started
as `Production` will reject every request, because the only registered scheme will try to call
the Hub decrypt API that this deployment does not have.

**In Development, one config flag disables the entire authorization system.**
`IamOptions.AllowAnonymousInDevelopment` is checked in `XR50AuthorizationHandler.DevelopmentBypassActive()`
(`TenantAuthorizationHandlers.cs:70-73`), which is the single gate every policy handler runs
first — including the fallback policy. When it is on, every policy succeeds for anonymous
callers and `GetEffectiveUserId()` returns `"demoadmin"`.

It is set to `true` in **both** `appsettings.json:17` and `appsettings.Development.json:15`.
Only `docker-compose.yaml:114` overrides it (`IAM__AllowAnonymousInDevelopment: ${IAM_ALLOW_ANONYMOUS:-false}`).

> **Consequence:** a plain `dotnet run` on `localhost:5286` runs with **all authorization
> disabled**. Every test in this plan would pass against it while proving nothing. Stage 0
> exists to make that impossible to do by accident.

---

## Stage 0 — Pre-flight (blocking)

None of the later stages mean anything until these three pass.

### P0-1. Rebuild the stack — the running one predates the change

The container currently on this machine was built `2026-08-03`; the authorization commits
land `2026-08-05` through `2026-08-07`. Confirmed stale:

- `GET /api/auth/me` returns `404` (the controller does not exist in that image).
- The base database has no `TenantRegistry` table and `Users` has no `email` column.
- The app log carries ~10,000 MySQL errors (`Unknown database 'xr50_tenant_test_company'`).

```bash
docker compose --profile sandbox down --remove-orphans
docker compose --profile sandbox build training-repo
docker compose --profile sandbox up -d
```

Use the **`sandbox`** profile, not `minio` or `lab`. `keycloak` is only a member of `sandbox`
(`docker-compose.yaml:307`), and without Keycloak there is no way to obtain a token in the
standalone configuration. `sandbox` starts `training-repo` + `mariadb` + `minio` + `keycloak`.

### P0-2. Prove the development bypass is OFF

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5286/api/auth/me     # must be 401
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5286/health          # must be 200
```

If `/api/auth/me` answers `200` to an unauthenticated request, the bypass is active and **the
run is void**. Set `IAM_ALLOW_ANONYMOUS=false` in `.env` and recreate.

Treat this as a permanent gate, not a one-off: it is the check that distinguishes "authorization
works" from "authorization is switched off". `scripts/authz-probe.sh` runs it first and refuses
to continue if it fails.

Note that this check cannot also detect P0-1. The fallback policy covers unmatched routes as
well as endpoints, so an anonymous request to a route that does not exist returns `401`, not
`404` — indistinguishable from a route that exists and requires auth. Detecting a stale build
takes an *authenticated* request (the probe script does this after acquiring its tokens).

### P0-3. Provision a tenant whose name matches the Keycloak claim

> Tenant names must match `^[a-zA-Z0-9_]+$` (see finding 3). Use `test_company`, not
> `test-company`. Bucket names are a separate field and may keep hyphens.

The bundled realm gives `testuser`, `admin` and `tenantadmin` the attribute
`tenantName=test_company` (`keycloak-config/xr50-realm.json`). `TenantMemberHandler` compares
that claim against the `{tenantName}` route segment, so the local tenant **must** be named
`test_company` or every tenant-scoped request from those three users returns `403` for reasons
that have nothing to do with the change under test.

Create it as `sysadmin` (the only realm user with a system-admin role), then confirm the
per-tenant database exists.

### Stage 0 exit gate

```bash
# as testuser
GET /api/auth/me  ->  200
{ "authenticated": true, "authenticationScheme": "Bearer", "userName": "testuser",
  "tenantName": "test_company", "role": "member", "isTenantAdmin": false, "isSystemAdmin": false }
```

`/api/auth/me` is the cheapest diagnostic in the system and the correct first stop for every
"why did I get a 403" in the stages below — it reports the tenant and role the pipeline actually
derived, rather than the one you assumed.

> Expected-value gotcha: `Role` is computed as `isTenantAdmin ? tenantadmin : member`
> (`XR50AuthController.cs:44`). `sysadmin` therefore reports `"role": "member"` with
> `"isSystemAdmin": true`. That is correct behaviour, not a bug — assert on the booleans.

---

## Stage 1 — Hermetic suite (fast, already green)

```bash
dotnet build
dotnet test tests/XR50TrainingAssetRepo.Tests/XR50TrainingAssetRepo.Tests.csproj
```

**Current baseline: 122 passed, 0 failed, build clean (148 warnings, all pre-existing).**

This suite already runs with the bypass explicitly disabled (`WebApplicationFixture.cs`, sets
`IAM:AllowAnonymousInDevelopment=false`) and drives identities through header-based
`TestAuthHandler`. It covers the fallback 401, tenant-mismatch 403, role policies, Hub
self-service provisioning, progress ownership and user role management.

Because it is hermetic and fast, this is where **new** authorization assertions belong. The
live stages below are for catching what the in-memory substitutes cannot model.

### Gaps worth adding here

| Gap | Why it matters |
|---|---|
| `POST /materials/{id}/ai-assistant/refresh-status` requires only `TenantMember` | Every sibling in the `ai-assistant/*` family is `TenantAdmin` (`XR50MaterialsController.cs:5829` vs `5792`, `5859`, `5890`) |
| `POST /ai-assistant/{id}/session/invalidate` is `TenantMember` | The equivalent `DELETE /innov-chatbot/{id}/history` is `TenantAdmin`; the two chatbot controllers disagree on who may wipe shared state |
| `GET /assets/{id}/share-url`, `/shares*` are `TenantMember` | A member can read externally-usable share URLs an admin created |
| `GET /users` returns the full roster incl. admin flags at `TenantMember` | Widest read in a controller whose every other action is `TenantAdmin` |
| Progress `isAdmin` falls back to the `Users.admin` DB column | `ProgramProgressController.cs:98-105`, `QuizProgressController.cs:190-197` decide tenant-wide progress visibility from tenant DB state rather than the policy layer, using what is documented elsewhere as the *system*-admin flag |
| `GET /api/test` is `[AllowAnonymous]` unconditionally | Unlike Swagger it is not Development-gated, so a debug route ships enabled |

Each of these is a **finding to adjudicate, not necessarily a bug** — decide the intended
policy first, then write the test that pins it.

---

## Stage 2 — Functional suite (the regression net)

This is the stage that answers *"did we break the app for legitimate users"*.

```bash
cd tests/functional && npm install
npm test              # all 9 suites, --runInBand
```

The suite already performs a Keycloak password grant and attaches `Authorization: Bearer` to
its requests (`helpers/api-client.js:53-98`), so it does not need auth plumbing added. Two
things must be fixed first or the results are not trustworthy:

### Fix before running

1. **Suites that accept `401`/`403` as a passing status.** `suites/04-storage.test.js:49,83,105`
   and `suites/09-ai-assistant.test.js:25` tolerate auth failures as expected outcomes. An
   authorization regression in storage or AI-assistant would **pass silently**. Tighten these
   to assert the success status, or exclude the two suites from this run and probe them by hand.

2. **`getHeaders()` is overwritten by caller options.** In all four verb helpers the spread comes
   after the headers key (`api-client.js:114-117` and the `post`/`put`/`delete` equivalents), so
   any caller passing `options.headers` silently drops `Authorization` *and* `Content-Type`.
   Only the two intentional bad-token probes do this today
   (`suites/02-auth.test.js:137-140`, `:152-155`), but it makes an auth regression look like a
   test bug. Merge rather than replace: `headers: { ...this.getHeaders(...), ...options.headers }`.

### Known-broken, not worth fixing for this run

`scripts/run-integration-tests.sh` issues **unauthenticated** curl smoke tests against routes
that no longer exist (`/api/tenants` rather than `/xr50/trainingAssetRepository/Tenants`). It
will report failures that are neither regressions nor real. Skip it, or repair it separately.

### Run it per identity

Run the whole suite three times, varying `TEST_USER`/`ADMIN_USER` (`config.js:52-94`):

| Run | Identity | Expectation |
|---|---|---|
| A | `sysadmin` | Everything passes. This is the "nothing is broken" baseline. |
| B | `tenantadmin` | Everything inside `test_company` passes; only the `SystemAdmin` endpoints (tenant list/create/delete, troubleshooting) turn `403`. |
| C | `testuser` | Reads pass; every authoring/mutation suite turns `403`. Failures here are *expected* — the value is the diff against run B. |

Run A failing is a real regression. Run C **not** failing on mutations is an over-permissioning
bug.

---

## Stage 3 — Role matrix sweep

The functional suite exercises depth; this stage exercises the **policy tiers** directly, and
is the only stage that tests over-permissioning systematically. One representative endpoint per
tier, four identities, plus anonymous.

```bash
./scripts/authz-probe.sh [tenantName]     # defaults to test_company
```

The script implements the matrix below. It runs the Stage 0 gate first, refuses to continue
against a stale build, and exits non-zero on any mismatch.

Realm identities (`keycloak-config/xr50-realm.json`), all password `<username>123`:

| User | Realm role | `tenantName` | Effective |
|---|---|---|---|
| `testuser` | `user` | `test_company` | member |
| `tenantadmin` | `tenantadmin` | `test_company` | tenant admin |
| `admin` | `admin` | `test_company` | tenant admin (role alias) |
| `sysadmin` | `systemadmin` | *(none)* | system admin |

`sysadmin` having no `tenantName` is deliberate and valuable: it is what proves the
system-admin exemption from tenant-route matching actually works.

### Expected matrix

`—` means the row does not apply. Status codes are the *authorization* outcome; a `404`/`400`
from the handler means authorization passed, which is the assertion that matters.

| Endpoint | Tier | anon | testuser | tenantadmin | sysadmin |
|---|---|---|---|---|---|
| `GET /health` | anonymous | 200 | 200 | 200 | 200 |
| `GET /api/test` | anonymous | 200 | 200 | 200 | 200 |
| `GET /api/auth/me` | fallback | **401** | 200 | 200 | 200 |
| `GET /xr50/trainingAssetRepository/Tenants/examples/create-requests` | fallback | **401** | 200 | 200 | 200 |
| `GET /api/test_company/materials` | TenantMember | **401** | 200 | 200 | 200 |
| `GET /api/test_company/users` | TenantMember | **401** | 200 | 200 | 200 |
| `DELETE /api/test_company/materials/999999` | TenantAdmin | **401** | **403** | 404 | 404 |
| `POST /api/test_company/materials/workflow-complete` | TenantAdmin | **401** | **403** | 4xx | 4xx |
| `DELETE /api/test_company/innov-chatbot/1/history` | TenantAdmin | **401** | **403** | not 403 | not 403 |
| `GET /xr50/trainingAssetRepository/Tenants` | SystemAdmin | **401** | **403** | **403** | 200 |
| `POST /xr50/trainingAssetRepository/Tenants` | TenantCreator | **401** | **403** | **403** | 2xx/4xx |
| `DELETE /xr50/trainingAssetRepository/Tenants/nonexistent` | SystemAdmin | **401** | **403** | **403** | not 403 |
| `GET /api/troubleshooting/health-check` | SystemAdmin | **401** | **403** | **403** | 200 |
| **`GET /api/other_company/materials`** | cross-tenant | **401** | **403** | **403** | not 403 |

The last row is the tenant-isolation check and needs no second tenant to exist:
`TokenMatchesRouteTenant` compares the claim to the route segment and fails closed before the
handler touches the database, so an unknown tenant name is a valid probe. `sysadmin` is exempt
from the match and will fall through to a handler error instead — that is the correct result.

All of these are read-only or target ids that do not exist, so the sweep is non-destructive and
needs no cleanup.

### The probe already earns its keep

A dry run against the *stale* pre-change image (before P0-1) returned, as `testuser`:

- `POST /materials/workflow-complete` → `415` instead of `403`
- `DELETE /innov-chatbot/1/history` → `500` instead of `403`

Both are endpoints the change under test moved to `TenantAdmin`, and on the old build a plain
member sailed past authorization into the handler. That is exactly the over-permissioning
signal this stage exists to catch, and it confirms the matrix discriminates.

It also exposed a measurement trap: a bare `POST` with no body answers `415` from the model
binder, which is neither `401` nor `403` and would therefore *score as "authorization passed"*
whether or not a policy was present. The script always sends `Content-Type: application/json`
and `{}` on `POST`/`PUT` so the authorization verdict is the only thing being measured. Any
hand-written probe must do the same.

### Two negative checks that belong here

- **`TenantCreator` is unusually wide.** `TenantCreatorHandler` admits *any* Hub-authenticated
  principal (`TenantAdminHandlers.cs:146`); its real authorization lives in controller code
  (`XR50TenantController.cs:78-102`, `194-203`). In standalone there are no Hub principals, so
  the policy should collapse to system-admin-only — assert `tenantadmin` gets `403` on
  `POST Tenants`. If it does not, the standalone deployment has self-service tenant creation
  it was never meant to have.

- **A malformed / expired / wrong-issuer bearer token must be `401`, never `200`.** Cheap to
  check, and it is the failure mode that would silently disable the whole scheme.

---

## Stage 4 — Configuration and deployment-mode checks

These test the *envelope* rather than the endpoints, and each corresponds to a way the standalone
deployment can be misconfigured into being insecure or unusable.

| # | Check | Expected | Why |
|---|---|---|---|
| C1 | Start with `IAM_ALLOW_ANONYMOUS=true`, request `/api/test_company/materials` anonymously | `200` | Confirms the bypass is real and that P0-2 is a meaningful gate — you have to see it *work* to trust the check that it is off |
| C2 | Start with `ASPNETCORE_ENVIRONMENT=Production`, send a valid Keycloak bearer token | `401` | Documents that standalone cannot run outside Development. If this returns `200`, the Hub-only production assumption is broken |
| C3 | Swagger UI at `/swagger/index.html`, unauthenticated, Development | `200`/redirect | Swagger is mounted *before* `UseAuthorization` (`Program.cs:544-546`) precisely because the fallback policy also covers non-endpoint requests. Middleware-order regressions break this and nothing else catches it |
| C4 | `/swagger/index.html` with `ASPNETCORE_ENVIRONMENT=Production` | `404` | Swagger must not ship enabled |
| C5 | Confirm no `HL-Hub-Session-Token` header is required anywhere in the standalone flow | — | Proves the standalone path is genuinely Hub-free |

C2 is worth doing even though it "should" fail: right now the only thing documenting the
Development-only constraint is a comment in `Program.cs`. A test turns it into a known
property.

---

## Execution order and stop conditions

```
Stage 0  ──►  Stage 1  ──►  Stage 2  ──►  Stage 3  ──►  Stage 4
pre-flight    hermetic      functional    matrix       config
(blocking)    (~10 s)       (~5 min)      (~1 min)     (needs restarts)
```

Stop and diagnose rather than continuing:

- **Stage 0 gate fails** → everything downstream is void. Do not run the other stages.
- **Stage 1 regresses from 122 passing** → a hermetic failure is always a real defect; the
  suite has no infrastructure flakiness to blame.
- **Stage 2 run A (sysadmin) fails** → a legitimate admin operation broke. This is the
  highest-severity outcome in the plan, because it is what pilots will hit.
- **Stage 3 shows a `200` where the matrix says `403`** → over-permissioning; triage against
  the Stage 1 gap table before assuming it is new.

---

# Results — run of 2026-08-09

Executed against a freshly built `sandbox` stack (MinIO + Keycloak + MariaDB), tenant
`test_company`, bypass off.

| Stage | Result |
|---|---|
| 0 — pre-flight | Pass. Rebuilt; `/api/auth/me` resolves `testuser` → `test_company` / `member`. |
| 1 — hermetic | **122 / 122 passed.** |
| 2 — functional, run A (sysadmin) | 84 / 87. The 3 failures are pre-existing and unrelated (below). |
| 2 — functional, run B (tenantadmin) | 77 / 87. 7 failures are all `403` on SystemAdmin tenant management — **correct**. |
| 2 — functional, run C (member) | 73 / 87. Mutations refused across materials, programs, users — **correct**. |
| 3 — role matrix | **66 / 66 probes matched.** |
| 4 — configuration | C1, C3 pass. **C2 and C4 fail — see finding 1.** |

> **All findings below were fixed on the same day; see "Fixes applied" at the end.**
> Post-fix state: hermetic 122/122, role matrix 66/66, functional **87/87 across all 9 suites**,
> and stage 4 C1–C4 all pass.

**The authorization model itself is sound.** Every policy tier behaved as designed across all
four identities: the fallback returns 401, tenant binding returns 403 on mismatch, the
system-admin exemption works, the `admin`/`tenantadmin` role alias works, and a malformed token
is refused. No over-permissioning was found among the probed endpoints.

## Finding 1 — `ASPNETCORE_ENVIRONMENT` has no effect; the container always runs as Development

**Severity: high.** Root cause: `Dockerfile` ends with `ENTRYPOINT ["./run-migrations.sh"]`,
whose last line is `dotnet run`. `dotnet run` applies `Properties/launchSettings.json`, whose
every profile pins `ASPNETCORE_ENVIRONMENT=Development` — and launch-profile variables take
precedence over the ambient process environment.

Verified: with `ASPNETCORE_ENVIRONMENT=Production` confirmed on PID 1, the app logs
`Hosting environment: Development`, serves Swagger (`200`, expected `404`), and accepts a
Keycloak JWT (`200`, expected `401`).

Consequences, in order of severity:

1. **The `IsDevelopment()` guard is load-bearing in three places and is always true.** It gates
   the anonymous bypass, JWT bearer registration, and Swagger. None of them can be turned off by
   configuration as things stand.
2. **Three of the four shipped env templates enable the bypass**: `.env.example:30`,
   `.env.sandbox.example:43` and `.env.lab.example:41` all set `IAM_ALLOW_ANONYMOUS=true`. Since
   the app always believes it is Development, any deployment using those templates runs with
   **authorization entirely disabled** — an anonymous caller passes every policy including
   `SystemAdmin`. Only `.env.prod.example:41` sets it `false`.
3. **The production auth surface is not Hub-only.** `docs/guides/authentication.md` states the
   Keycloak scheme is "Development only"; in practice it is registered in every deployment, so
   anyone holding a Keycloak token authenticates.
4. The startup guard that warns about a missing `XR50Hub:SharedSecret` never fires, because it
   sits behind `if (!app.Environment.IsDevelopment())`.

Fix direction: publish the app and run the built assembly
(`ENTRYPOINT ["dotnet", "XR50TrainingAssetRepo.dll"]`, already present but commented out in the
`Dockerfile`), or pass `dotnet run --no-launch-profile`. Then re-run stage 4; C2 and C4 should
flip to pass. Worth doing before relying on any environment-gated behaviour.

## Finding 2 — `04-storage` reports success on authorization failures

**Severity: medium (masks regressions).** Run as a plain member, the suite reported
**10 / 10 passed** while every upload returned `403`; tests named "can upload text file" and
"can upload image file" passed on a refusal. `suites/04-storage.test.js:49,83,105` and
`09-ai-assistant.test.js:25` treat `401`/`403` as acceptable statuses. As written, these suites
cannot detect an authorization regression in storage or AI-assistant. Fix before relying on
them as a regression net.

## Finding 3 — pre-existing defects, unrelated to authorization

- **`assetIds` "serialized as strings" — the test was wrong, not the API.** The 3 failures
  common to every functional run assert `[3]` and receive `["3"]`. Root cause is
  `IntToStringConverter` / `NullableIntToStringConverter` (`Program.cs:45-46`, `753-795`), a
  deliberate global contract that writes **every** `int` as a JSON string across the whole API —
  which is why asset and material `id` come back as `"5"` and `"15"` too. The API is behaving as
  designed; the suite's `Number(...)` expectation contradicts the contract. Fixed in the test.
- **Tenant name validation does not reject invalid names — and the real defect is worse than a
  missing 400.** The 03-tenant test created a tenant literally named `invalid name with spaces!`.
  `XR50TenantService.GetTenantSchema` derives the per-tenant database as
  `"xr50_tenant_" + Regex.Replace(name, "[^a-zA-Z0-9_]", "_")`, so `foo bar`, `foo-bar` and
  `foo.bar` **all resolve to the same database** `xr50_tenant_foo_bar`. Two distinct tenants
  could silently share storage — a cross-tenant data leak that the tenant-binding authorization
  model cannot see, because binding is by name while isolation is by folded schema.
- **MinIO endpoint mismatch.** Every `.env*.example` sets `AWS_HOST=http://minio:9000`, but
  `docker-compose.yaml:260` starts MinIO with `--address ":10000"`. Storage is unreachable until
  corrected; this blocked tenant creation until the local `.env` was fixed to `:10000`.
- **`/swagger/v1/swagger.json` returns 404** while `Program.cs` registers it as the "Default"
  entry in the Swagger UI dropdown. The named groups (`tenants`, `materials`, `users`, …) all
  return 200.

## Fixes applied

| # | Fix | Files |
|---|---|---|
| 1 | `dotnet run --no-launch-profile`, so `ASPNETCORE_ENVIRONMENT` is no longer overridden by the launch profile | `run-migrations.sh` |
| 2 | `AllowAnonymousInDevelopment` now defaults to **false** — the bypass must be opted into, never out of | `appsettings.json`, `appsettings.Development.json` |
| 3 | `IAM_ALLOW_ANONYMOUS=false` in the dev/sandbox/lab templates, matching the compose default | `.env.example`, `.env.sandbox.example`, `.env.lab.example` |
| 4 | MinIO endpoint corrected to `:10000` to match `docker-compose.yaml` | all `.env*.example` |
| 5 | `401`/`403` removed from tolerated statuses — a refused request now fails the suite | `suites/04-storage.test.js`, `suites/09-ai-assistant.test.js` |
| 6 | Request headers **merged** under caller options instead of replaced, so `options.headers` can no longer silently drop `Authorization` | `helpers/api-client.js` |
| 7 | Tenant names restricted to `^[a-zA-Z0-9_]+$` (max 52 chars), closing the schema-collision leak | `Models/DTOs/XR50TenantDtos.cs` |
| 8 | Generated test tenant names switched to underscores to satisfy the new rule | `config.js`, `setup.js`, `suites/03-tenant.test.js` |
| 9 | Swagger dropdown: dead `v1`/"Default" entry replaced with the real `all` document, `innov-chatbot` added | `Program.cs` |

Verified after the fixes:

- `ASPNETCORE_ENVIRONMENT=Production` now yields `Hosting environment: Production`, the
  Keycloak JWT scheme is **gone** (`401`), Swagger is **not served**, and the
  `XR50Hub:SharedSecret is not configured` startup guard fires. C2 and C4 now pass.
- C4's expected value in the table above should read **"not 200"** rather than `404`: with the
  Swagger middleware unregistered the path is not an endpoint, so the fallback policy answers
  `401`. That is correct, and leaks less than a `404`.
- The storage suite now **fails** (2 of 10) when run as a plain member whose uploads are
  refused, where it previously reported 10/10 passed. The false green is gone.
- Tenant creation rejects `invalid name with spaces!` and `foo-bar` with `400`, accepts
  `ok_name_123`.
- Full re-run: hermetic **122/122**, role matrix **66/66**, functional **87/87** (was 84/87).

Not changed, deliberately: the `IntToStringConverter` global contract. Every client of this API
already depends on ids being JSON strings; changing it would be a breaking API change well
outside the scope of an authorization review.

## Environment left behind

Stack running on the `sandbox` profile, `ASPNETCORE_ENVIRONMENT=Development`,
`IAM_ALLOW_ANONYMOUS=false`. Tenant `test_company` retained as the fixture for future runs, with
its materials and assets cleaned. The stray invalid-named tenant was deleted. The only local
change outside new files is `.env`, corrected to `minio:10000`.

---

## What this plan deliberately does not cover

- **Hub session token paths.** No Hub in standalone; the hermetic suite
  (`HubAuthenticationTests`, `TenantSelfServiceCreationTests`) is the only coverage and it is
  adequate for now.
- **OwnCloud storage.** `sandbox` uses MinIO/S3. If the pilots ship OwnCloud, the `lab` profile
  needs its own pass, since tenant creation there also mirrors users into an external store.
- **Performance of the authorization path.** The Hub decrypt cache is the only latency-relevant
  piece and it is not on the standalone path.
