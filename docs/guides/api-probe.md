# API Probe

Use a focused HTTP probe for a requested quick live check or behavior not covered
by an existing suite. For wider changes, select checks from
[Verification Workflow](verification-workflow.md) first. Its controls, below-API
assertions, baseline, and cleanup rules apply here too.

## Scope and prerequisites

- Diagnosis starts with read-only evidence. Creating fixtures, uploading files,
  changing roles, or deleting resources requires an authorized live-test scope.
  Use a disposable local test stack by default; a configured remote endpoint is
  not permission to modify it.
- Check `GET /health` returns 200 and anonymous `GET /api/auth/me` returns 401.
  If unavailable, report the probe skipped and the reason. Do not enable bypass.
- Authenticate according to [Authentication](authentication.md). Keycloak is
  Development-only; production uses Hub credentials. Verify the selected identity
  and tenant with authenticated `/api/auth/me`. Distinguish 401 (credential),
  403 (scope/role), and 503 (Hub unavailable) from the behavior under test.
- Use a user-designated test tenant, or create a unique disposable tenant when
  authorized. A name or description containing "probe" does not prove ownership.
  Check permissions for cleanup before creating anything: tenant deletion requires
  SystemAdmin, even when a Hub user can self-provision a tenant.
- Tenant names follow the validation and collision rules in [AGENTS.md](../../AGENTS.md).
  Use underscore-based names such as `probe_ingest_20260921`, not inferred route
  casing rules. Bucket names are separate and may contain hyphens.

## Requests and fixtures

Confirm routes, payloads, and expected responses from the current controllers,
Swagger, and `tests/functional/helpers/test-data.js`; do not copy a stale payload.

| Resource | Route |
|---|---|
| Tenants | `/xr50/trainingAssetRepository/tenants` |
| Materials and detail | `/api/{tenant}/materials`, `/api/{tenant}/materials/{id}/detail` |
| Assets | `/api/{tenant}/assets` |
| Programs / learning paths / users | `/api/{tenant}/programs`, `/learningpaths`, `/users` under the same tenant prefix |
| AI Assistant | See [AI Assistant / DataLens Probe](ai-assistant-probe.md) |

For MinIO tenants, use an existing authorized sandbox bucket (normally
`xr50-test-verification`). Tenant creation validates storage access; an invalid
bucket fails before the intended provisioning path. The S3 endpoint is as seen
by the API: `http://minio:10000` inside compose, or the published port for a host
process (the sandbox publishes `http://localhost:10000`). Follow
[Sandbox Setup](../setup/sandbox.md) and current compose configuration.

Use real binary fixtures for uploads: magic-byte detection inspects the stream,
not just the supplied MIME type. Preserve returned IDs rather than guessing them;
some responses serialize an ID as a string.

Capture the HTTP status separately from the body, use bounded timeouts, and
inspect both. For example, after setting `API_URL`, `PROBE_TENANT`, and an
appropriate `XR50_PROBE_TOKEN` securely (never print it or enable shell tracing):

```bash
probe_dir=$(mktemp -d)
status=$(curl -sS --max-time 20 -o "$probe_dir/body.json" -w '%{http_code}' \
  -H "Authorization: Bearer $XR50_PROBE_TOKEN" \
  "$API_URL/api/$PROBE_TENANT/materials")
printf 'HTTP %s\n' "$status"
```

Read only relevant fields from the captured body; redact personal data and
credentials in reports. On PowerShell, use `curl.exe`, not the `curl` alias.
Use JSON files for nontrivial request bodies and the current test-data factories
as payload references. Do not classify success from the status alone.

## Cleanup and result

Track exact resource IDs and baseline state before mutations. Delete only this
probe's fixtures, in dependency order, and verify their absence below the API when
appropriate. Do not delete an existing tenant or a shared bucket/collection. Report
any retained resources or cleanup failure, including partially created resources.

Report the request, expected versus actual status and relevant body fields,
assertion counts, control results, and cleanup evidence. Unavailable dependencies
mean skipped or blocked checks, not success. Promote recurring contract checks
into the appropriate permanent suite as described in the verification guide.
