---
name: api-probe
description: Run targeted curl probes against the running XR50 API at http://localhost:5286 to verify behavior that's too narrow or too new for the Jest suite. Use when (1) no Jest scope cleanly covers the change, (2) you want a fast surgical check after a fix, or (3) the user asks for a quick targeted verification. Requires the docker-compose sandbox stack to be up.
---

# API Probe — Layer 4 of the Autonomous Test Loop

Use this skill to verify a specific code change with a small, surgical HTTP probe instead of running a Jest suite. Probes are for the **gaps**: changes too narrow for any existing scope, or one-off bug fixes where running a full suite is overkill. If the same probe shape comes up more than twice, that's a signal to add a real Jest test instead.

## Preconditions — always do these first

### 1. Health gate

Probe `GET /health` before anything else. If it doesn't return HTTP 200 with `{"status":"healthy"}`, **stop** and tell the user:

> "Integration stack not reachable on http://localhost:5286. Run `docker-compose --profile sandbox up -d` and wait ~90s for services to come up."

Do not invent verification — never claim a probe passed when the stack was down.

```bash
# Bash tool (preferred — works from POSIX shell on Windows)
curl -s -o /dev/null -w "%{http_code}" http://localhost:5286/health
```

```powershell
# PowerShell tool — note: use curl.exe, NOT curl (which is an alias for Invoke-WebRequest)
curl.exe -s -o $null -w "%{http_code}" http://localhost:5286/health
```

Expect `200`. If you get connection-refused or non-200, the stack is down.

### 2. Identify a tenant

Probes need a tenant context. In order of preference:

1. **Reuse an existing tenant** — list them and pick one:
   ```bash
   curl -s http://localhost:5286/xr50/trainingAssetRepository/tenants
   ```
   Look for a tenant with a recent `description` containing "verification" or "probe" — those are throwaway and safe to use.

2. **Use one provided by the user** — if the user mentioned a tenant name in the conversation, use it.

3. **Create a throwaway** — see "Fixtures" below. Keep the name distinctive (`probe-{timestamp}`) so cleanup is easy.

Tenant names are **lowercase with hyphens**. The route is case-sensitive in containers.

## Curl conventions

- **`curl.exe` vs `curl`**: on Windows, PowerShell's `curl` is an alias for `Invoke-WebRequest` (different surface). When using the PowerShell tool, always invoke `curl.exe` explicitly. Bash tool's `curl` is the real binary.
- **Capture status separately**: `-o body.json -w "%{http_code}"` so the body and status code don't get mixed in the output.
- **Silent mode**: `-s` suppresses progress meter; `-fsS` additionally fails on HTTP errors and shows error details.
- **JSON POST**: `-X POST -H "Content-Type: application/json" -d @payload.json` (use a file for non-trivial bodies — easier to debug).
- **Auth**: bypass is enabled in Development (`IAM:AllowAnonymousInDevelopment=true` in `appsettings.Development.json`). No bearer token needed for local probes.

### Probe template

```bash
# Single probe: capture status + body separately
status=$(curl -s -o /tmp/probe-body.json -w "%{http_code}" \
  -X GET http://localhost:5286/api/probe-tenant/materials)
echo "HTTP $status"
cat /tmp/probe-body.json | jq .
```

```powershell
$status = curl.exe -s -o $env:TEMP\probe-body.json -w "%{http_code}" `
  -X GET http://localhost:5286/api/probe-tenant/materials
"HTTP $status"
Get-Content $env:TEMP\probe-body.json | ConvertFrom-Json
```

## Endpoint cheat sheet

| Operation | Method + Path |
|---|---|
| Health | `GET /health` |
| Test endpoint | `GET /api/test` |
| List tenants | `GET /xr50/trainingAssetRepository/tenants` |
| Get tenant | `GET /xr50/trainingAssetRepository/tenants/{name}` |
| Create tenant | `POST /xr50/trainingAssetRepository/tenants` |
| Delete tenant | `DELETE /xr50/trainingAssetRepository/tenants/{name}` |
| List materials | `GET /api/{tenant}/materials` |
| Create material | `POST /api/{tenant}/materials` |
| Get material | `GET /api/{tenant}/materials/{id}` |
| Material detail | `GET /api/{tenant}/materials/{id}/detail` |
| Update material | `PUT /api/{tenant}/materials/{id}` |
| Delete material | `DELETE /api/{tenant}/materials/{id}` |
| List assets | `GET /api/{tenant}/assets` |
| Programs | `GET /api/{tenant}/programs` |
| Learning paths | `GET /api/{tenant}/learningpaths` |
| AI Assistant | `POST /api/{tenant}/ai-assistant` |
| Users | `GET /api/{tenant}/users` |

## Fixtures — payload recipes

These mirror `tests/functional/helpers/test-data.js` so the shapes stay correct.

### Throwaway tenant (MinIO, matches sandbox profile)

```json
{
  "tenantName": "probe-{timestamp}",
  "tenantGroup": "probe-tests",
  "description": "Ad-hoc probe tenant",
  "storageType": "S3",
  "s3Config": {
    "bucketName": "xr50-test-verification",
    "bucketRegion": "us-east-1",
    "endpoint": "http://minio:9000",
    "forcePathStyle": true
  },
  "owner": {
    "userName": "probeadmin",
    "fullName": "Probe Admin",
    "userEmail": "admin@probe.test",
    "password": "TestPass123!",
    "admin": true
  }
}
```

**Bucket must exist.** The sandbox profile pre-provisions four buckets via the `sandbox_init_buckets` script: `xr50-test-verification` (use this for probes), `xr50-sandbox-tenant-demo`, `xr50-sandbox-tenant-pilot4`, `xr50-sandbox-tenant-pilot5`. The tenant create call validates bucket reachability before doing anything else, so an unknown bucket fails with HTTP 400 and never exercises tenant-side code paths.

If the API is running on the host (`dotnet run`) instead of inside docker, change `endpoint` to `http://localhost:9000`.

### Video material

```json
{
  "name": "Probe Video",
  "description": "Probe video material",
  "type": "Video",
  "videoPath": "/videos/probe.mp4",
  "videoDuration": 60,
  "videoResolution": "1920x1080"
}
```

### Checklist material

```json
{
  "name": "Probe Checklist",
  "description": "Probe checklist",
  "type": "Checklist",
  "config": {
    "entries": [
      { "text": "Step 1", "description": "First", "related": [] },
      { "text": "Step 2", "description": "Second", "related": [] }
    ]
  }
}
```

### AI Assistant material (Mode B — empty assets, uses tenant default collection)

```json
{
  "name": "Probe AI Assistant",
  "description": "Mode B: empty assets",
  "type": "ai_assistant",
  "unique_id": 12345,
  "related": [],
  "assets": []
}
```

### Training program

```json
{
  "name": "Probe Program",
  "description": "Probe training program",
  "objectives": "Verify probe behavior",
  "requirements": "None",
  "min_level_rank": 1,
  "max_level_rank": 5
}
```

For more shapes (workflow, chatbot, AI Assistant Mode A variants, programs with paths), see [tests/functional/helpers/test-data.js](../../../tests/functional/helpers/test-data.js).

## Assertion format — every probe ends with this

Don't bury the verdict. Every probe report ends with:

```
Probe: <one-line description of what was tested>
  Request:  <method> <path>
  Status:   <got> (expected <expected>)  [PASS|FAIL]
  Body:     <relevant fields, e.g., id=42, type=Video> [PASS|FAIL]
  Verdict:  <PASS | FAIL — brief reason>
```

For multi-step probes, use one block per step plus a final overall verdict. **No silent passes** — if you ran a probe, the user sees the result.

## Common gotchas

- **`id` is sometimes a string**: a custom JSON converter occasionally serialises material `id` as a string. Don't assume integer; `JsonElement.ValueKind == String` is valid. The `tests/XR50TrainingAssetRepo.Tests/Integration/SubcomponentRelatedMaterialsTests.cs` helper handles this.
- **Routes are case-sensitive in containers**: use lowercase tenant names with hyphens. `Probe-Tenant` and `probe-tenant` are different routes inside Linux.
- **Magic-byte detection needs real binaries**: don't try to upload a JSON-encoded "file" — asset detection inspects the first 12 bytes of the actual stream. Use `tests/functional/helpers/test-data.js#createTestImageFile` as a reference for a minimal valid PNG.
- **Tenant route is the odd one out**: tenants are at `/xr50/trainingAssetRepository/tenants`, not `/api/tenants`. Most other routes follow the `/api/{tenant}/<resource>` pattern.
- **MinIO endpoint depends on where the API runs**: `http://minio:9000` if the API is in docker, `http://localhost:9000` if running via `dotnet run` on the host.

## When NOT to use this skill

- A Jest scope already covers the change → run that scope per the autonomous test loop table in CLAUDE.md.
- The same probe pattern appears more than twice in a session → propose adding a Jest test in the right suite instead.
- The change is in code that has no real-DB or real-storage dependency → `dotnet test` against the hermetic xUnit suite already covers it.

## Cleanup

Throwaway tenants and materials should be deleted after the probe unless the user is keeping the fixture for further work. List what you created in your final report so cleanup is obvious.
