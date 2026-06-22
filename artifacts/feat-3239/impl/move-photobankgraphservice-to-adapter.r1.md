# move-photobankgraphservice-to-adapter — Implementation Summary

## Changes made

### New file
**`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs`**

- Created directory `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/`
- Namespace: `Anela.Heblo.Adapters.Microsoft365.Photobank`
- Implements `IPhotobankGraphService` (imported via `using Anela.Heblo.Application.Features.Photobank.Services;`)
- `GetThumbnailAsync` returns `Task<GetThumbnailResult>`:
  - Catches `MsalException` → returns `GetThumbnailResult.AuthUnavailable`
  - Catches `HttpRequestException` from `SendAsync` → returns `GetThumbnailResult.UpstreamError`
  - HTTP 404 → `GetThumbnailResult.NotFound`
  - HTTP 406 → `GetThumbnailResult.NotFound`
  - HTTP 429 → `GetThumbnailResult.Throttled` (with `Retry-After` header parsing)
  - Other non-success status → `GetThumbnailResult.UpstreamError`
  - HTTP 200 → `GetThumbnailResult.Success`
- No `GraphThrottledException` — removed entirely
- `GetDeltaAsync` and `ResolveItemIdAsync` copied verbatim from original
- All private inner classes (`GraphDeltaPage`, `GraphDeltaItem`, `GraphParentReference`, `GraphFileFacet`, `GraphFolderFacet`, `GraphDeletedFacet`, `GraphItemWithId`) copied verbatim

### Deleted file
**`backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs`**

## Build result

`dotnet build Anela.Heblo.Adapters.Microsoft365.csproj` — adapter project's own source compiled without errors.

3 anticipated errors remain in the Application project dependency (all in scope of subsequent tasks):
- `GetThumbnailHandler.cs(40)` — CS0029: return type mismatch (fixed by `rewrite-getthumbnailhandler`)
- `GetThumbnailHandler.cs(43)` — CS0246: `GraphThrottledException` not found (fixed by `rewrite-getthumbnailhandler`)
- `PhotobankModule.cs(52)` — CS0246: `PhotobankGraphService` not found in Application layer (fixed by `wire-adapter-di-registration`)
