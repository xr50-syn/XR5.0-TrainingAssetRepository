# Known Issues & Workarounds

## Active Workarounds

(none)

## Resolved

### CHAT-001: Chatbot API duplicate filename rejection

**Date**: 2026-02-28
**Resolved**: 2026-04-16 (DataLens API v1.1.0 migration)
**Status**: Resolved

**Problem**:
The external AI service rejected document submissions when a file with the same filename already existed in the collection.

**Previous Workaround**:
`ChatbotApiService.SubmitDocumentAsync` caught the `400 BadRequest` and returned a synthetic job ID (`duplicate-accepted-{assetId}`), treating it as success.

**Resolution**:
The DataLens API v1.1.0 now supports `PUT /api/v1/collections/{collection_name}/documents/{document_name}` for updating existing documents. `ChatbotApiService.SubmitDocumentAsync` checks if a document exists and uses PUT (update) or POST (create) accordingly. The synthetic `duplicate-accepted-` job ID pattern and all associated caller detection code has been removed.
