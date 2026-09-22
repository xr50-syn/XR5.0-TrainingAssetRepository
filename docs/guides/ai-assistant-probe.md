# AI Assistant / DataLens Probe

Diagnose or verify upload → AI Assistant material → DataLens ingestion →
background status reconciliation → document-scoped chat. Read
[AGENTS.md](../../AGENTS.md), [API Probe](api-probe.md), and the relevant
[Verification Workflow](verification-workflow.md) checks first. Diagnosis alone
does not authorize creating resources or sending documents to an external service.

## Pipeline and invariants

- `AIAssistantMaterialService` ensures the bound collection and submits each asset,
  recording an `AIAssistantMaterialAssetJob` per `(material, asset)` with the
  returned job ID. Existing tracked jobs may be reused; inspect job rows rather
  than assuming each request triggers a new submission.
- Stored uploads are read through `IAssetContentReader` / `IStorageService` and
  sent with `SubmitDocumentContentAsync`. Their display URL and
  `S3_PUBLIC_ENDPOINT` do not drive ingestion. Only reference-only assets use URL
  submission (`SubmitDocumentAsync`). The same split applies to standalone
  asset submission and INNOV chatbot ingestion.
- `AiStatusSyncService` defaults to 15-second active polling and 5-minute idle
  checks, configurable under `AiStatusSync`. Material status is
  `notready | process | ready`; document jobs use
  `pending | processing | completed | failed`. There is no material-level
  `failed` status. Observe both levels when diagnosing a stuck material.
- New implicit collection names come from `AIAssistantCollections`:
  `aiassist_{id}_{tenant}`, with a sanitized lowercase tenant component. Explicit
  bindings and existing legacy `aiassist_{id}` bindings are preserved. Do not
  rename existing collections as part of a probe. The tenant's `DefaultAICollection`
  is for the generic Chat/default endpoint, not material-bound assistants, even
  when they have no assets.
- `EnsureCollectionExistsAsync` creates only after an exact 404. A 502 is not
  evidence that a collection is missing; do not pre-create a collection merely to
  hide that failure.

## Preconditions

Use an authorized disposable tenant and nonsensitive, valid PDF fixture. Obtain
the needed role and ensure cleanup is possible before creating fixtures. Keep
authentication enabled, confirm `/health` returns 200 and anonymous `/api/auth/me`
returns 401, and verify the actual signed-in tenant and role. Every tenant request
below needs the authentication headers described in [Authentication](authentication.md).

Check authenticated `GET /api/{tenant}/ai-assistant/health`: require both HTTP 200
and `available: true`. HTTP 200 alone does not prove DataLens availability. A
failure here may indicate provider configuration, credentials, or connectivity;
report it before attempting ingestion. A healthy provider does not prove the
target collection is writable.

Record the initial material/asset list and collection state. If testing a
deployment or an existing reported problem, inspect its current jobs and binding
read-only before deciding whether a new ingest is needed.

## App-driven probe

Capture HTTP status and body separately with bounded request timeouts, following
the API probe guide. Substitute the tenant and returned IDs below.

1. Upload a real PDF using `POST /api/{tenant}/assets` with multipart fields
   `File` (file content), `Filetype=pdf`, and `Description`. Record the returned
   asset ID. Confirm the storage-backed asset can be read; changing its public
   URL is not a fix for storage-backed ingestion.
2. Create the material with `POST /api/{tenant}/materials` and a JSON payload:

   ```json
   {
     "name": "DataLens probe assistant",
     "type": "ai_assistant",
     "config": {
       "assets": [{"id": "RETURNED_ASSET_ID", "type": "pdf", "name": "probe.pdf"}]
     }
   }
   ```

   Capture the returned material ID and `collectionName`. Omitting the collection
   exercises automatic tenant-scoped binding; use an explicit `collectionName`
   only when that is the behavior being tested, and only for an authorized
   collection. Inspect `status` and warnings: `partial` means a side effect failed,
   not a fully successful ingestion. Even `success` is not proof of completion.
3. Read `GET /api/{tenant}/ai-assistant/{materialId}/documents`. Expect a real
   `jobId` and the correct asset and collection association. An empty/missing job
   ID is not proof that DataLens accepted the document.
4. Poll that documents endpoint and
   `GET /api/{tenant}/materials/{materialId}/detail` until completion or a declared
   deadline. Use short bounded polls; account for the configured idle interval as
   well as active polling and provider processing time. An initial 120-second
   observation window can time out legitimately while the sync is idle. Record
   the last job/material state and elapsed time rather than retrying indefinitely
   or declaring a sync bug from elapsed time alone.
5. If question answering is in scope, call
   `POST /api/{tenant}/ai-assistant/{materialId}/ask` with
   `{"query":"What is this document about?"}`. This is the material-bound route.
   The route without `{materialId}` addresses the default endpoint and cannot
   verify this material's collection. Use known content to check the answer, not
   just a successful status.

For collection-isolation changes, include another material and (when authorized)
another tenant with the same local material ID. Assert distinct auto-bindings and
that documents/answers do not cross them. Include an unchanged explicit or legacy
binding as a control; do not modify real user bindings to create that control.

## Direct DataLens cross-check

When authorized and useful, use the configured `ChatbotApi:BaseUrl` and its
provider token (`CHATBOT_API_BEARER_TOKEN`), not the app's Hub/Keycloak token.
Never hardcode, echo, or include either credential in a report. Use the actual
captured collection name, not one reconstructed from assumptions.

- `GET /api/v1/collections/{collection}` checks existence.
- `GET /api/v1/collections/{collection}/documents` checks document registration.
- `GET /api/v1/collections/{collection}/jobs` checks the matching job's state.

A completed provider job but a pending local job suggests reconciliation trouble;
check the local job ID/binding, sync configuration, and logs. A missing collection
or document suggests a failed push: inspect creation warnings, source asset
readability, and provider errors. Only investigate asset URL reachability for
reference-only assets. Keep retries bounded and avoid duplicate submissions.

## Cleanup and reporting

Delete only this run's material and uploaded asset, then its tenant if this run
created it and tenant deletion was authorized. Local row deletion does not prove
external DataLens cleanup. Check whether the collection/documents/jobs remain;
remove an external collection only if it was created by this run and is not
shared. Otherwise report the retained external resources and needed follow-up.
Compare final state with the baseline; preserve pre-existing resources.

Report IDs/binding, accepted submission versus completed processing, per-document
and aggregate status, optional chat result, controls, skipped steps, and verified
cleanup. Use [Testing](testing.md) for `test:ai-assistant` and promote recurring
regressions into hermetic or functional tests rather than growing a permanent
scratch probe.
