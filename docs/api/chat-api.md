# Chat API

Conversation endpoints for the chatbot-family material types. There are three, kept as
**separate material types** (selected by the `type` discriminator at creation), each with its own
controller:

| Material type | Backend | Controller base | Notes |
|---------------|---------|-----------------|-------|
| `chatbot` | DataLens (generic Chat API) | `/api/{tenantName}/chat` | Default chat; routes to the tenant's `DefaultAICollection` |
| `ai_assistant` | DataLens (RAG) | `/api/{tenantName}/ai-assistant` | Multi-asset ingestion, session continuity, audio |
| `innov_chatbot` | INNOV "LLM Engine" | `/api/{tenantName}/innov-chatbot` | Pilot-scoped; per-tenant connection |

> The two RAG backends are reached through a shared `IChatbotProvider` seam internally, but a client
> selects a backend purely by **material type** — there is no provider flag.

---

## Generic Chat API (DataLens)

The Chat API proxies to DataLens's inference endpoint
(`POST /api/v1/collections/{collection}/inferences`) against the **tenant's `DefaultAICollection`**.
Bearer auth and the collection are resolved server-side; the client only sends the query.

> The backend connection comes from `ChatbotApi:BaseUrl` / `ChatbotApi:BearerToken`
> (env: `CHATBOT_API_BASE_URL`, `CHATBOT_API_BEARER_TOKEN`). The bearer must be a token authorized
> for the target collection(s). For multi-collection use it should be the DataLens **admin** token.

### `POST /api/{tenantName}/chat/ask`

```jsonc
// request
{
  "query": "What does Problem 6 ask for?",
  "sessionId": "optional-session-id",
  "documents": ["PSF60_assignment3.pdf"]   // optional: restrict to specific docs (see below)
}
```

```jsonc
// response
{
  "sessionId": "…",
  "query": "What does Problem 6 ask for?",
  "response": {
    "speech": { "text": "…", "link": "…/audio/…" },
    "markdown": "## Answer …",
    "images": ["…"]
  },
  "reasoning": null,
  "sources": ["PSF60_assignment3, ContentType: text, Page 6, …"]
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| query | string | Yes | The question |
| sessionId | string | No | Conversation continuity |
| documents | string[] | No | Restrict the answer to specific source documents (see [Document-scoped chat](#document-scoped-chat)) |

### Other generic endpoints
- `POST /api/{tenantName}/chat/ask/form` — form-encoded (`query`, `session_id`, `source_files`)
- `POST /api/{tenantName}/chat/{chatbotId}/ask` (+ `/ask/form`) — same behaviour, validates the `ChatbotMaterial` exists (404 if not); DataLens has no per-material collection, so it also targets the tenant default collection
- `GET /api/{tenantName}/chat/health` — backend availability
- `GET /api/{tenantName}/chat` , `GET /api/{tenantName}/chat/{chatbotId}` — list / get chatbot materials

### Document-scoped chat

Restrict the answer to specific documents within the collection (maps to DataLens
inference `source_files`):

- **JSON**: `"documents": ["PSF60_assignment3.pdf", "PSF60_Assignment2.pdf"]`
- **Form**: `source_files=PSF60_assignment3.pdf,PSF60_Assignment2.pdf`

Pass filenames **exactly as the documents listing shows them** (with extension) — the extension is
stripped automatically before the call, because DataLens matches `source_files` on the document
**base name**. Omit `documents`/`source_files` to search the whole collection.

### Error handling
| Status | Cause |
|--------|-------|
| 400 | Empty query; backend unreachable / non-2xx (e.g. 401/404/500 from DataLens, surfaced with detail) |
| 404 | `ChatbotMaterial` not found (material-scoped endpoints) |
| 500 | Unexpected server error |

A malformed/unexpected backend response is handled gracefully and does not produce an unhandled 500.

---

## INNOV Chatbot material

The INNOV "LLM Engine" ingests documents into a **pilot** and answers queries against it.

### Per-tenant configuration
Set when creating the tenant (`POST /xr50/trainingAssetRepository/tenants`):

```jsonc
{
  "tenantName": "…",
  "storageType": "S3",
  "s3Config": { "bucketName": "…", "bucketRegion": "…" },
  "innovChatbotBaseUrl": "https://innov.example/",
  "innovChatbotApiToken": "…",          // static bearer; stored, never returned
  "innovChatbotDefaultPilot": "pilot-9"  // fallback pilot for materials without their own
}
```

The tenant response exposes `innovChatbotBaseUrl`, `innovChatbotDefaultPilot`, and
`innovChatbotConfigured` (bool). The **token is never echoed**.

### Create the material
```jsonc
POST /api/{tenantName}/materials/json
{
  "name": "Pilot assistant",
  "type": "innov_chatbot",
  "pilot": "pilot-9",            // optional; falls back to tenant default
  "expertiseLevel": "expert",    // optional: beginner | intermediate | expert
  "assetIds": [1, 2]             // optional; auto-submitted for ingestion
}
```

### Endpoints (`/api/{tenantName}/innov-chatbot`)
| Method & path | Purpose |
|---------------|---------|
| `POST /{id}/chat` (+ `/chat/form`) | Query the pilot (`query`, optional `expertiseLevel`) |
| `POST /{id}/documents` | Upload a document directly to the pilot |
| `POST /{id}/submit` | Ingest the material's associated assets into the pilot |
| `GET /{id}/documents` | List the material's assets and their ingest status |
| `GET /{id}` , `GET` (list) | Material details / list |
| `GET /{id}/health` | Backend availability for the tenant |
| `DELETE /{id}/history` | Clear the pilot's server-side chat history |

```jsonc
// POST /{id}/chat request
{ "query": "Summarize the safety procedure", "expertiseLevel": "intermediate" }

// response
{
  "query": "…",
  "text": "…",
  "pilot": "pilot-9",
  "sources": ["https://…/doc.pdf"],
  "images": ["https://…/img.png"],
  "tokensUsed": 1234,
  "processingTime": 1.8
}
```

### Error handling
| Status | Cause |
|--------|-------|
| 400 | Empty query; tenant not configured for INNOV; backend unreachable (surfaced with detail) |
| 404 | INNOV chatbot material not found |
| 500 | Unexpected server error |

---

## AI Assistant material (DataLens)

The `ai_assistant` material supports multi-asset ingestion, session continuity, and audio. See its
controller at `/api/{tenantName}/ai-assistant` (`/{id}/ask`, `/{id}/documents`, `/{id}/health`,
`/{id}/session/invalidate`, …). Asset ingest status — including the DataLens-reported
`document_name` — is surfaced per asset in the material detail response
(`GET /api/{tenantName}/materials/{id}/detail`). Each AI Assistant material gets its own
DataLens collection (`aiassist_{id}`) unless an explicit `collectionName` is supplied, so one
material's documents never surface in another's answers. (Supplying an explicit `collectionName`
lets several assistants share a curated collection when that is intended.)
