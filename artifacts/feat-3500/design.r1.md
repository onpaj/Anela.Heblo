# Design: Close Coverage Gap in GraphCatalogDocumentsStorage

## Component Design

No production components are added or changed. This section covers the test-only doubles that the new tests will introduce or extend inside `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs`.

### `CreateStorage(responder)` (existing, reused as-is)
Factory used by every test to build a `GraphCatalogDocumentsStorage` wired to:
- `Mock<ITokenAcquisition>` returning fixed `"app-token"` / `"delegated-token"` strings.
- `Mock<IHttpClientFactory>` returning an `HttpClient` backed by `RecordingHandler`.

No changes to this helper's signature or internals are needed. New tests call it exactly as existing tests do; all new behavior (multi-request sequencing, URL branching) lives in the `responder` argument each test supplies, not in the factory.

### `RecordingHandler` (existing, unchanged)
Fake `HttpMessageHandler` that appends every outgoing `HttpRequestMessage` to an ordered `List<HttpRequestMessage> Requests` and returns `responder(request)` as the `HttpResponseMessage`.

- **Contract:** one handler instance per test (fresh via `CreateStorage`), so `Requests` accumulates only the calls made within that single test — no shared state across tests, no thread-safety concerns since each `[Fact]` builds its own instance.
- **New usage pattern (not a class change):** tests that need multi-request sequencing (upload-session-then-chunks, paginated folder search) implement `responder` as a closure that branches on the incoming request rather than ignoring it:
  - By call count: a local `int callCount` mutated on each invocation, first call returns the `createUploadSession` body, subsequent calls return chunk-response bodies.
  - By request shape: `request.Method == HttpMethod.Post` vs `HttpMethod.Put`, or `request.RequestUri!.ToString().Contains("createUploadSession")` / `.Contains("next-page-2")` / equality against a mocked `@odata.nextLink` URL.
- Assertions read `Requests[i].Method`, `Requests[i].RequestUri`, and `Requests[i].Content!.Headers.ContentRange` (typed `ContentRangeHeaderValue`, not string-parsed) to pin routing, pagination, and chunk-offset behavior.

### `ThrottledReadStream` (new, test-only, private nested class)
A minimal `Stream` subclass added under the existing `// ─── Recording infrastructure ───` region, alongside `RecordingHandler`. Used only by the FR-2 "multi-read fill of a single chunk" scenario, where the production `ReadAsync` inner-fill loop must be exercised against a source that deliberately under-fills the caller's buffer.

**Contract:**
- Constructed from a backing byte source (e.g. `byte[]` or wraps a `MemoryStream`) plus a `maxBytesPerRead` cap (an int much smaller than `chunkSize`, e.g. 64 KB).
- `ReadAsync(buffer, offset, count, ...)` returns `min(count, maxBytesPerRead, remainingBytes)` bytes per call — i.e., it must never fill the caller's full request in one call while bytes remain, forcing the production code's `while (totalRead < chunkSize)` fill loop to execute multiple iterations.
- Returns `0` once the backing content is exhausted (standard EOF contract), so the production code's outer `while (offset < sizeBytes)` loop terminates correctly.
- `Length` must reflect the total declared size the test passes as `sizeBytes` to `UploadFileAsync`, since the production code does not re-derive size from the stream independently in the paths under test.
- Only `Read`/`ReadAsync`, `Length`, and `Position` need real behavior; `Write`/`Seek`/`CanWrite` etc. can throw `NotSupportedException` or be no-ops, since production code never calls them on the upload content stream.

No other test doubles are required: `FindFolderAsync` tests use the existing `CreateStorage`/`RecordingHandler` pair with URL-branching responders (no new stream double needed, since folder-search responses are JSON bodies, not content streams).

## Data Schemas

All schemas below are wire-shape JSON string literals the tests fabricate as mocked Graph API HTTP response bodies (via the `responder` callback), matching the internal shapes already consumed by `GraphCatalogDocumentsStorage.cs` (`CatalogGraphDriveItem`, `CatalogGraphDriveItemCollection`, `CatalogGraphUploadSession`, folder/file facets). Tests never instantiate these C# types directly — they are `internal` to the Application assembly and are only ever produced/consumed as raw JSON across the fake HTTP boundary.

### Small-file upload response (single `PUT .../content?...`)
```json
{
  "id": "item-id-small-1",
  "name": "uploaded-file.pdf",
  "folder": null
}
```
Returned once, `200 OK`, in response to the single `PUT` issued by `UploadSmallFileAsync`.

### `createUploadSession` response (large-file path, first request)
```json
{
  "uploadUrl": "https://graph.microsoft.com/upload-session/abc"
}
```
Returned `200 OK` in response to `POST .../createUploadSession`. All subsequent chunk `PUT`s in the test must target this exact `uploadUrl` string verbatim (production code issues `new HttpRequestMessage(HttpMethod.Put, session.UploadUrl)` directly — no `Authorization` header on these requests; tests must not assert one).

### Chunk upload response (large-file path, each chunk `PUT`)
```json
{
  "id": "item-id-chunk-final",
  "name": "uploaded-file.pdf",
  "folder": null
}
```
Returned `200 OK` (or `201 Created`) for each chunk `PUT`. Tests assert the method's returned filename equals the `name` from the **last** chunk response (confirms `uploadedName` is updated from the final chunk, not a stale first-chunk value) — so intermediate chunk responses may use a distinguishable `name` (e.g. `"chunk-1-name"`) versus the final chunk's `name` (e.g. `"final-name.pdf"`) to make the assertion meaningful.

Chunk request assertions read `Content-Range` via `request.Content!.Headers.ContentRange` (typed `ContentRangeHeaderValue`):
- `.From` / `.To` — inclusive byte offsets of the chunk within the file.
- `.Length` — the **total** declared `sizeBytes`, not the chunk's own length.

### Folder listing page response (`FindFolderAsync`, `CatalogGraphDriveItemCollection`)
```json
{
  "value": [
    {
      "id": "folder-id-1",
      "name": "PIF-2024",
      "folder": { "childCount": 0 }
    },
    {
      "id": "file-id-1",
      "name": "PIF-2024-notes.txt",
      "folder": null,
      "file": { "mimeType": "text/plain" }
    }
  ],
  "@odata.nextLink": "https://graph.microsoft.com/v1.0/next-page-2"
}
```
- Items with a non-null `folder` facet are folder candidates; items with `folder: null` (and typically a `file` facet present) are excluded from match consideration regardless of name match, per FR-3's "non-folder items excluded" case.
- `@odata.nextLink`, when present and non-null, is followed verbatim: the responder must recognize a subsequent request whose `RequestUri` equals this exact string and return the next page.
- Second/last page omits `@odata.nextLink` (or sets it to `null`) to terminate pagination.

### Folder listing — 404 short-circuit response
An `HttpResponseMessage` with `StatusCode = HttpStatusCode.NotFound` and no body (or an empty JSON object), returned for the first-page request only, to assert `FolderStatus.NotFound` without any pagination attempt.

### `FolderSearchResult` shapes under test (existing type, not redefined; assertions target these field combinations)
- No matches → `{ Status: FolderStatus.NotFound }` (`FolderId`/`FolderName` default/empty).
- Exactly one match → `{ Status: FolderStatus.Found, FolderId: "<matching id>", FolderName: "<matching name>" }`.
- Multiple matches, `allowMultiple: false` → `{ Status: FolderStatus.MultipleMatches, FolderId: string.Empty, FolderName: string.Empty }`.
- Multiple matches, `allowMultiple: true` → `{ Status: FolderStatus.Found, FolderId/FolderName: <alphabetically-first match by StringComparer.OrdinalIgnoreCase> }`.
