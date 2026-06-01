# GraphOneDriveService — Folder Management Extraction

**Date:** 2026-04-01
**Branch:** feature/sharepoint_knowledgebase_ingestion
**Scope:** Refactor `GraphOneDriveService` — extract folder management and shared HTTP helpers into separate internal classes.

## Problem

`GraphOneDriveService` currently embeds folder path resolution logic (`GetFolderItemIdAsync`, `GetOrCreateFolderAsync`, `GetDriveRootIdAsync`) as private methods that thread `HttpClient` and `token` through recursive calls. The shared HTTP utilities (`EncodePath`, `CreateRequest`, `DeserializeAsync`, `JsonOptions`, Graph model classes) are also embedded, making the service file do too much.

## Design

Three files after refactor:

### 1. `Services/GraphApiHelpers.cs` — `internal static class`

Shared HTTP and serialization utilities used by both the service and the resolver.

**Contents:**
- `static string EncodePath(string path)` — percent-encodes each path segment
- `static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)` — builds a bearer-authenticated request
- `static Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)` — deserializes Graph JSON responses
- `static JsonSerializerOptions JsonOptions` — case-insensitive options
- `internal class GraphDriveItem` — `{ Id, Name, WebUrl, File? }`
- `internal class GraphFileFacet` — `{ MimeType }`
- `internal class GraphDriveItemCollection` — `{ Value: List<GraphDriveItem> }`

### 2. `Services/GraphFolderResolver.cs` — `internal class`

Owns all folder-navigation logic. Created per-call by `GraphOneDriveService` with the request's `HttpClient` and `token`.

**Constructor:** `(HttpClient client, string token, IMemoryCache cache, ILogger logger)`

**Public method:**
- `Task<string> GetOrCreateFolderIdAsync(string driveId, string folderPath, CancellationToken ct)`
  Checks `graph:folder-id:{driveId}:{folderPath}` cache key first. On miss, delegates to `ResolveOrCreateAsync` and caches result for 60 minutes.

**Private methods:**
- `Task<string> ResolveOrCreateAsync(string driveId, string folderPath, CancellationToken ct)`
  GETs the path via `drives/{driveId}/root:/{encodedPath}:`. If 200, returns the item ID. If 404, recursively ensures the parent exists, then POSTs a new folder under the parent. Any other status throws.
- `Task<string> GetRootIdAsync(string driveId, CancellationToken ct)`
  Cached (`graph:root-id:{driveId}`, 60 min) lookup of `drives/{driveId}/root`.

Uses `GraphApiHelpers` for all HTTP request construction and JSON deserialization.

### 3. `Services/GraphOneDriveService.cs` — simplified

**Removes:** `GetFolderItemIdAsync`, `GetOrCreateFolderAsync`, `GetDriveRootIdAsync`, `EncodePath`, `CreateRequest`, `DeserializeAsync`, `JsonOptions`, all Graph model classes.

**`MoveToArchivedAsync` change:**
```csharp
var resolver = new GraphFolderResolver(client, token, _cache, _logger);
var folderItemId = await resolver.GetOrCreateFolderIdAsync(driveId, archivedPath, ct);
```

All other Graph calls in the service use `GraphApiHelpers` directly.

## File Layout

```
Services/
├── GraphApiHelpers.cs        ← new: shared HTTP utilities + Graph models
├── GraphFolderResolver.cs    ← new: folder path resolution + creation
├── GraphOneDriveService.cs   ← modified: delegates folder ops, uses helpers
├── IOneDriveService.cs       ← unchanged
└── MockOneDriveService.cs    ← unchanged
```

## What Does NOT Change

- `IOneDriveService` interface — no change
- `KnowledgeBaseIngestionJob` — no change
- `KnowledgeBaseModule` registration — no change
- Public behavior of all three `IOneDriveService` methods — no change
- Cache key format and TTLs — no change

## Out of Scope

- Changing `IOneDriveService` or its callers
- Making `GraphFolderResolver` injectable via DI
- Adding tests (existing test coverage is unchanged)
