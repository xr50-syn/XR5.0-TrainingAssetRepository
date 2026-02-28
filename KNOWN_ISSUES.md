# Known Issues & Workarounds

## Active Workarounds

### CHAT-001: Chatbot API duplicate filename rejection

**Date**: 2026-02-28
**Status**: Active workaround — revert when upstream is fixed
**Affected files**:
- `Services/ChatbotApiService.cs`
- `Services/XR50AssetService.cs`
- `Services/Materials/AIAssistantMaterialService.cs`

**Problem**:
The external AI service rejects document submissions when a file with the same filename already exists in the collection, regardless of whether the content is different. The API returns:

```
400 BadRequest
{"detail": "Document '<filename>' already exists in collection '<collection>'. Use PUT to update."}
```

This blocks re-uploading or uploading different files that happen to share a name.

**Workaround**:
The `ChatbotApiService.SubmitDocumentAsync` method catches this specific `400 BadRequest` response and treats it as a successful submission, returning a synthetic job ID (`duplicate-accepted-{assetId}`). The callers in `XR50AssetService` and `AIAssistantMaterialService` detect this synthetic ID and mark the asset as `"ready"` immediately instead of `"process"`.

**This is not correct behavior.** It assumes the previously uploaded document is acceptable, which may not be true if the user intended to replace it with different content.

**Resolution**:
Once the upstream AI service supports `PUT` for updating existing documents, this workaround should be removed and replaced with proper update logic. Search for `duplicate-accepted-` across the codebase to find all related code.
