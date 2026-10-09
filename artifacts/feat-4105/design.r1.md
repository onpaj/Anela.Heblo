# Design: Move `LeafletDocumentSummary` to the Leaflet module's shared `Contracts/` folder

## Component Design

### `Features/Leaflet/Contracts/LeafletDocumentSummary.cs` (new file)
- Responsibility: shared DTO representing a single leaflet document's summary metadata, returned by both the `GetLeafletDocuments` listing use case and the `UploadLeaflet` upload use case.
- No behavior/logic — plain data holder (properties only), consistent with the other files already in `Features/Leaflet/Contracts/` (`KnowledgeSearchResult.cs`, `ILeafletKnowledgeSource.cs`).
- Namespace: `Anela.Heblo.Application.Features.Leaflet.Contracts`.

### `Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs` (modified)
- Responsibility unchanged: defines `GetLeafletDocumentsRequest` and `GetLeafletDocumentsResponse` for the paged document-listing use case.
- Change: no longer declares `LeafletDocumentSummary` itself; references it via `using Anela.Heblo.Application.Features.Leaflet.Contracts;`. `GetLeafletDocumentsResponse.Documents` keeps its type `List<LeafletDocumentSummary>`.

### `Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs` (modified)
- Responsibility unchanged: queries the repository and maps domain documents into `LeafletDocumentSummary` instances.
- Change: adds `using Anela.Heblo.Application.Features.Leaflet.Contracts;` to resolve the (now relocated) type; mapping logic is untouched.

### `Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs` (modified)
- Responsibility unchanged: response DTO for the upload use case, exposing `Document` of type `LeafletDocumentSummary?`.
- Change: `using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` is replaced with `using Anela.Heblo.Application.Features.Leaflet.Contracts;` — this removes the response's only dependency on the sibling `GetLeafletDocuments` use case.

### `Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` (modified)
- Responsibility unchanged: extracts uploaded file content, delegates indexing to `IndexLeafletRequest` via `IMediator`, maps the result to a `LeafletDocumentSummary` in its private `MapToSummary` method.
- Change: `using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` is replaced with `using Anela.Heblo.Application.Features.Leaflet.Contracts;`. Its existing `using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;` (used for `IndexLeafletRequest`/`IndexLeafletResponse`) is untouched. `MapToSummary`'s body is unchanged.

### `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs` (modified)
- Responsibility unchanged: existing controller test suite.
- Change: adds `using Anela.Heblo.Application.Features.Leaflet.Contracts;` so the two existing `LeafletDocumentSummary` object-initializer usages (lines 156, 294) still resolve. Its existing `using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` is kept, since the file still uses `GetLeafletDocumentsRequest`/`GetLeafletDocumentsResponse`/handler types from that namespace elsewhere.

## Data Schemas

No schema changes. `LeafletDocumentSummary`'s shape (property names, types, defaults) is identical before and after — only its C# namespace changes. Both consuming HTTP response contracts keep their exact JSON shape:

```json
// GetLeafletDocumentsResponse (unchanged JSON shape)
{
  "documents": [
    {
      "id": "guid",
      "filename": "string",
      "status": "string",
      "contentType": "string",
      "ingestedAt": "datetime",
      "indexedAt": "datetime|null",
      "firstChunkId": "guid|null"
    }
  ],
  "totalCount": 0,
  "pageNumber": 0,
  "pageSize": 0,
  "totalPages": 0
}

// UploadLeafletResponse (unchanged JSON shape)
{
  "document": { /* same LeafletDocumentSummary shape as above, or null */ }
}
```

No database schema, migration, or event payload involved.
