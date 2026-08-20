---
name: ai-assistant-probe
description: Drive the XR5.0 app's AI Assistant -> DataLens ingestion + status pipeline end-to-end against the running stack, with optional direct DataLens cross-checks. Use to verify that uploading a document and creating an AI Assistant material actually pushes to DataLens, that the background status sync drives the material process -> ready, and to diagnose where the chain breaks (asset URL scheme, collection binding, status sync). Requires the docker-compose sandbox stack up on http://localhost:5286.
---

# AI Assistant / DataLens Probe

Verifies the full app-driven AI Assistant flow: upload a PDF asset -> create an AI Assistant material (which ensures the DataLens collection and submits the document) -> watch the background `AiStatusSyncService` reconcile the material to `ready` -> ask a question. Optionally cross-checks DataLens itself (collection / documents / jobs) to localize a failure.

Use this when:
1. Someone reports AI Assistant ingestion or status problems ("submitting PDFs fails", "status stuck at notready/process", "Bad Gateway").
2. You changed anything in the AI Assistant ingest/status path (`AIAssistantMaterialService`, `ChatbotApiService`, `AiStatusSyncService`, `AIAssistantService`, collection binding) and want a live end-to-end check.
3. You're validating a fresh/partner deployment can actually reach and drive DataLens.

This is a scenario probe (Layer 4). If the same check recurs, promote it to a Jest scope or hermetic xUnit test.

## How the pipeline works (so you can interpret results)

- Create AI Assistant material with assets -> `SubmitForProcessingAsync` calls `EnsureCollectionExistsAsync` (GET collection; if 404, POST-create), then for each asset `ChatbotApiService.SubmitDocumentAsync` **downloads `asset.URL` via HttpClient** and uploads the bytes to DataLens, storing an `AIAssistantMaterialAssetJob` row (status `pending`, with the DataLens `jobId`).
- The background `AiStatusSyncService` polls every **15s when active** (5 min idle), calls `GetJobStatusAsync` per in-flight job, and recomputes the material's aggregate `AIAssistantStatus`: `notready` -> `process` -> `ready`/`failed`.
- Material-level status vocabulary: `notready | process | ready`. Per-document job vocabulary (on `/ai-assistant/{id}/documents`): `pending | processing | completed | failed`.
- Collection: an AI Assistant material binds to `collectionName` if supplied, else its own per-material collection `aiassist_{id}` (derived from the material id). The tenant's `DefaultAICollection` is only used by the generic Chat API / default endpoint, not by AI Assistant materials.

## Preconditions — always do these first

### 1. App health gate
```bash
curl -s -o /dev/null -w "%{http_code}" http://localhost:5286/health
```
Expect `200`. If not, **stop** and tell the user: "Integration stack not reachable on http://localhost:5286. Run `docker-compose --profile sandbox up -d` and wait ~90s." Never claim a probe passed when the stack was down.

### 2. App -> DataLens reachability gate
```bash
curl -s -w "\n%{http_code}\n" "http://localhost:5286/api/<tenant>/ai-assistant/health"
```
Expect `{"available":true}` / `200`. If `available:false`, the API cannot reach DataLens (wrong `ChatbotApi:BaseUrl`, missing/invalid `CHATBOT_API_BEARER_TOKEN`, or egress blocked) — stop here and report that; ingestion cannot work.

### 3. Tenant
Reuse a throwaway tenant if one exists, or create one (see Fixtures). Use a distinctive name like `probe-<something-static>` (no `Date.now()` — pick a fixed suffix per run).

## The probe — capture IDs from each response, don't hardcode

Set `BASE=http://localhost:5286` and `T=<tenant>`. With auth bypassed in Development no token is needed; if the deployment has Keycloak, add `-H "Authorization: Bearer <token>"`.

1. **Create + upload a PDF asset** (multipart). Capture the returned `id` and **inspect `url`**:
   ```bash
   curl -s -X POST "$BASE/api/$T/assets" \
     -F "File=@probe.pdf;type=application/pdf" -F "Filetype=pdf" -F "Description=probe"
   ```
   - **Critical check:** the `url` must be `http(s)://...`. If it is `s3://...`, ingestion WILL fail with `"The 's3' scheme is not supported"` (HttpClient can't download `s3://`). Fix: set `S3_PUBLIC_ENDPOINT` to a host reachable **both** by the API container and clients (a LAN IP like `http://192.168.1.35:9000`, NOT `localhost:9000`, which the API container resolves to itself). Then re-upload (existing assets keep their old URL).

2. **Create the AI Assistant material** with that asset id (this triggers ensure-collection + submit). Optionally add `"collectionName":"<name>"` to target a specific collection instead of the auto-assigned `aiassist_{id}`:
   ```bash
   curl -s -X POST "$BASE/api/$T/materials" -H "Content-Type: application/json" -d '{
     "name":"DataLens probe assistant","type":"ai_assistant",
     "config":{"assets":[{"id":"<assetId>","type":"pdf","name":"probe.pdf"}]}
   }'
   ```
   - Capture the material `id` and `collectionName` from the response.
   - `status:"success"` + no `warnings` = submit accepted by DataLens.
   - `status:"partial"` + `warnings[]` = the chatbot side failed; read the warning (e.g. the `s3` scheme error or a `502`). Localize from there.

3. **Per-asset job view** (after the jobId fix, jobId/status come from the job rows):
   ```bash
   curl -s "$BASE/api/$T/ai-assistant/<matId>/documents"
   ```
   Expect a real `jobId` and `status` (`pending`/`processing`/`completed`) for the submitted asset.

4. **Poll material status** until `ready` (allow generous time — DataLens processing can take tens of seconds, then the sync runs on a 15s cycle; budget ~60-120s):
   ```bash
   for i in $(seq 1 12); do
     curl -s "$BASE/api/$T/materials/<matId>/detail" | grep -oE '"aiAssistantStatus":"[^"]*"'
     sleep 10
   done
   ```
   `process` then `ready` = the status check works end-to-end. Stuck at `process` past ~2 min with a completed DataLens job points at the sync (e.g. a job row with a null `jobId` is never polled).

5. **(Optional) Ask a question** through the app (routes to DataLens inference on the bound collection):
   ```bash
   curl -s -X POST "$BASE/api/$T/ai-assistant/ask" -H "Content-Type: application/json" \
     -d '{"query":"What is this document about?"}'
   ```

## Direct DataLens cross-check (to localize a failure)

When the app side looks wrong, check DataLens itself. Base URL is `ChatbotApi:BaseUrl` (default `https://datalens.xr50.work`); read the bearer token from `.env` (`CHATBOT_API_BEARER_TOKEN`) — **never hardcode or echo it**. Use the `collectionName` captured in step 2.

```bash
DL="https://datalens.xr50.work"; TOKEN="<from .env>"; C="<collectionName>"
curl -s -o /dev/null -w "collection=%{http_code}\n" -H "Authorization: Bearer $TOKEN" "$DL/api/v1/collections/$C"
curl -s -H "Authorization: Bearer $TOKEN" "$DL/api/v1/collections/$C/documents"
curl -s -H "Authorization: Bearer $TOKEN" "$DL/api/v1/collections/$C/jobs"
```
- Collection `200` + your document listed + a `completed` job = DataLens did its part; any remaining problem is on the app's status-sync/display side.
- Missing collection / no documents = the push never reached DataLens (re-check step 1's URL and step 2's warnings).
- A `502` on the existence GET is a transient gateway error; `EnsureCollectionExistsAsync` only auto-creates on an exact `404`, so a `502` aborts without creating — re-run, or pre-create the collection.

## Fixtures

Create a throwaway tenant (sandbox MinIO bucket must exist — `xr50-test-verification` is created public by the init script):
```bash
curl -s -o /dev/null -w "%{http_code}\n" -H "Content-Type: application/json" \
  -X POST "$BASE/xr50/trainingAssetRepository/tenants" -d '{
    "tenantName":"probe-tenant","tenantGroup":"ft","description":"ai-assistant probe","storageType":"S3",
    "s3Config":{"bucketName":"xr50-test-verification","bucketRegion":"eu-west-1"},
    "owner":{"userName":"a","fullName":"a","userEmail":"a@probe.test","password":"TestPass123!","admin":true}
  }'
```

A minimal valid PDF to upload:
```bash
printf '%%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n4 0 obj<</Length 60>>stream\nBT /F1 18 Tf 72 700 Td (XR50 DataLens probe) Tj ET\nendstream endobj\n5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\ntrailer<</Size 6/Root 1 0 R>>\n%%%%EOF\n' > probe.pdf
```

## Cleanup — always
```bash
curl -s -o /dev/null -w "mat=%{http_code}\n"    -X DELETE "$BASE/api/$T/materials/<matId>"
curl -s -o /dev/null -w "asset=%{http_code}\n"  -X DELETE "$BASE/api/$T/assets/<assetId>"
curl -s -o /dev/null -w "tenant=%{http_code}\n" -X DELETE "$BASE/xr50/trainingAssetRepository/tenants/<tenant>"  # only if you created it
rm -f probe.pdf
```
Deleting the tenant cleans up its DataLens-bound rows; the probe collection on DataLens can be removed with `DELETE /api/v1/collections/<collectionName>` if you want a pristine backend.

## Windows / tooling notes
- Use the **Bash tool** for these (real `curl`, POSIX paths). In PowerShell, use `curl.exe` (plain `curl` is an `Invoke-WebRequest` alias) and Windows file paths for `-F file=@C:/path`.
- Capture status separately with `-o body.json -w "%{http_code}"` so body and status don't mix.
- Honor the close-the-loop rule: if the health gate fails, say so and stop — do not fabricate a pass.
