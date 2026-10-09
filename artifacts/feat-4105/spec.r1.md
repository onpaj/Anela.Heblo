# Specification: Move `LeafletDocumentSummary` to the Leaflet module's shared `Contracts/` folder

## Summary
`LeafletDocumentSummary` is a DTO currently declared inside the `GetLeafletDocuments` use-case folder, but it is also consumed by the `UploadLeaflet` use case, which must import it from a sibling use case. This violates the module's `Contracts/` convention (shared DTOs across use cases live in `Contracts/`, not inside a single use case's folder). This change relocates the class to `Features/Leaflet/Contracts/LeafletDocumentSummary.cs` and updates all consumers to import from the new namespace. It is a pure code-organization change with no behavior change.

## Background
`docs/architecture/filesystem.md` states that a module's `Contracts/` folder holds "Shared DTOs across use cases." The Leaflet module already has a `Contracts/` folder (`ILeafletKnowledgeSource.cs`, `KnowledgeSearchResult.cs`). `LeafletDocumentSummary` is currently defined in `GetLeafletDocuments/GetLeafletDocumentsRequest.cs` (as a second top-level class in that file) but is also used by `UploadLeafletResponse.cs` and `UploadLeafletHandler.cs`, both in the `UploadLeaflet` use-case folder. `UploadLeaflet` currently has:

```csharp
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
```

purely to reach this shared type — a direct use-case-to-use-case dependency that the `Contracts/` convention exists to prevent. This was flagged by the daily arch-review routine (issue #4105).

## Functional Requirements

### FR-1: Relocate `LeafletDocumentSummary` to `Contracts/`
Move the `LeafletDocumentSummary` class out of `GetLeafletDocumentsRequest.cs` into a new file `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs`, under namespace `Anela.Heblo.Application.Features.Leaflet.Contracts`. The class's members, types, and default values must be unchanged (`Id`, `Filename`, `Status`, `ContentType`, `IngestedAt`, `IndexedAt`, `FirstChunkId`).

**Acceptance criteria:**
- `LeafletDocumentSummary` no longer appears in `GetLeafletDocumentsRequest.cs`.
- `LeafletDocumentSummary` exists in a new file under `Features/Leaflet/Contracts/`, in namespace `Anela.Heblo.Application.Features.Leaflet.Contracts`.
- No properties, types, or default values on the class changed.

### FR-2: Update all consumers to the new namespace
Every file that references `LeafletDocumentSummary` must import `Anela.Heblo.Application.Features.Leaflet.Contracts` instead of (or in addition to, where the use-case namespace is still needed for other types) `Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments`.

Known consumers (from repo-wide search):
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs` — declares `GetLeafletDocumentsResponse.Documents` as `List<LeafletDocumentSummary>`; same file/namespace as the use case, needs the new `using`.
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs` — constructs `LeafletDocumentSummary` instances; same namespace already implicitly available via the request file today, needs the new `using` after the move.
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs` — currently `using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` solely for this type; replace with `using Anela.Heblo.Application.Features.Leaflet.Contracts;`.
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` — currently `using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` for this type only (it also separately imports `UseCases.IndexLeaflet`); replace with `using Anela.Heblo.Application.Features.Leaflet.Contracts;`.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs` — constructs `LeafletDocumentSummary` at lines 156 and 294; currently relies on the `using ...UseCases.GetLeafletDocuments;` import (still needed for `GetLeafletDocumentsRequest`/`Response`/handler types used elsewhere in the file); add `using Anela.Heblo.Application.Features.Leaflet.Contracts;`.

**Acceptance criteria:**
- No file in the solution references `LeafletDocumentSummary` via the `UseCases.GetLeafletDocuments` namespace after the change (i.e., no file relies on the type being re-exported from that namespace).
- `UploadLeafletResponse.cs` and `UploadLeafletHandler.cs` no longer import `Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments` unless they use another type from it (`UploadLeafletHandler.cs` does not; `UploadLeafletResponse.cs` does not).
- Solution builds with no new warnings introduced by this change.

### FR-3: No behavior change
This is a structural/namespace-only change. No renaming of properties, no change to serialization output (JSON property names are unaffected by C# namespace, class name and members stay identical), no change to any HTTP contract, and no change to the generated TypeScript API client's shape.

**Acceptance criteria:**
- `GetLeafletDocumentsResponse` and `UploadLeafletResponse` JSON shapes are byte-for-byte identical before and after the change.
- Regenerating the OpenAPI/TypeScript client produces no diff in `frontend/src/api/generated/api-client.ts` for the affected DTOs (class name `LeafletDocumentSummary` is unchanged; only its C# namespace moved, which does not affect the OpenAPI schema name).

## Non-Functional Requirements

### NFR-1: Maintainability
The change directly restores the intended module boundary described in `docs/architecture/filesystem.md`: use cases should not import DTOs from sibling use-case folders when those DTOs are shared. After this change, `UploadLeaflet` depends only on `Contracts/`, not on `GetLeafletDocuments`.

### NFR-2: No security impact
No auth, data-sensitivity, or access-control surface is touched by this change.

## Data Model
No data model changes. `LeafletDocumentSummary` remains a plain DTO with the same shape:
```csharp
public class LeafletDocumentSummary
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
    public Guid? FirstChunkId { get; set; }
}
```

## API / Interface Design
No API surface changes. The class is a DTO consumed by `GetLeafletDocumentsResponse.Documents` and `UploadLeafletResponse.Document`; both HTTP contracts are unchanged. Per project rule, DTOs remain plain C# classes (not records) since this affects OpenAPI client generation ordering — the class was already a class and stays one.

## Dependencies
- No external service or library dependency changes.
- Depends on the existing `Features/Leaflet/Contracts/` folder already present in the module (holds `ILeafletKnowledgeSource.cs`, `KnowledgeSearchResult.cs`).

## Out of Scope
- No changes to `GetLeafletDocumentsResponse`, `GetLeafletDocumentsRequest`, `UploadLeafletRequest`, or any other DTO in the Leaflet module.
- No changes to the frontend beyond what the auto-generated OpenAPI TypeScript client regeneration produces (expected to be a no-op diff, per FR-3).
- No changes to any other module's `Contracts/` organization (this issue is scoped to the Leaflet module only).
- No changes to persistence, repository, or indexing behavior.

## Open Questions

None.

## Status: COMPLETE
