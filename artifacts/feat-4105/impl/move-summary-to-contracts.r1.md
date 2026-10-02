# Implementation: move-summary-to-contracts

## What was implemented
Moved the `LeafletDocumentSummary` class out of `GetLeafletDocumentsRequest.cs` into its own file under `Features/Leaflet/Contracts/`, matching the existing convention for other Leaflet contract types (`ILeafletKnowledgeSource.cs`, `KnowledgeSearchResult.cs`). Added the corresponding `using` directive to both the request file and the handler that constructs `LeafletDocumentSummary` instances.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs` — new file, byte-for-byte copy of the class body previously at the bottom of `GetLeafletDocumentsRequest.cs`, now in the `Anela.Heblo.Application.Features.Leaflet.Contracts` namespace.
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs` — removed the `LeafletDocumentSummary` class definition; added `using Anela.Heblo.Application.Features.Leaflet.Contracts;`.
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs` — added `using Anela.Heblo.Application.Features.Leaflet.Contracts;` so `new LeafletDocumentSummary { ... }` resolves from the new namespace; `Handle` method body otherwise unchanged.

## Tests
No tests required — mechanical namespace move with no behavioral change.

## How to verify
```
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expect exactly 2 errors (CS0246 `LeafletDocumentSummary` not found), both in `Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` and `UploadLeafletResponse.cs` — these are addressed by the next task (`update-leaflet-consumers`), not this one. No other errors should appear.

## Notes
Build was run and confirmed: 2 errors total, both CS0246 for `LeafletDocumentSummary`, located exactly in `UploadLeafletHandler.cs(57,20)` and `UploadLeafletResponse.cs(8,12)` — matching the task's expected/acceptable failure set exactly. No unrelated errors. 88 pre-existing warnings (nullable reference / obsolete API) unrelated to this change.

## PR Summary
This is a pure mechanical refactor: `LeafletDocumentSummary` moves from being nested inside `GetLeafletDocumentsRequest.cs` to its own file in `Features/Leaflet/Contracts/`, consistent with how other shared Leaflet contract types are organized. The class body is unchanged (same properties, same defaults). Consumers in the same folder (`GetLeafletDocumentsRequest.cs`, `GetLeafletDocumentsHandler.cs`) were updated with a `using` to the new namespace. The `UploadLeaflet` use case still references the type from its old location and will fail to compile until the follow-up task (`update-leaflet-consumers`) updates it — this was called out and accepted as in-scope-for-next-task per the task specification.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs` — new file containing the moved class.
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs` — class removed, using added.
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs` — using added.

## Status
DONE
