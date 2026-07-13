# Architecture Review: Remove unused `FileSizeBytes` from KnowledgeBase document upload

## Skip Design: true

## Architectural Fit Assessment
This is a pure dead-code removal within a single vertical slice (`KnowledgeBase/UseCases/UploadDocument`). It touches exactly two files — the request DTO and the controller that constructs it — and does not cross module boundaries, change persistence, or alter the public API contract. It aligns cleanly with the project's Vertical Slice Architecture and the "DTOs are never shared or global" rule in `docs/architecture/development_guidelines.md`: `UploadDocumentRequest` is owned entirely by the KnowledgeBase module, so shrinking its surface has zero blast radius on other modules.

Repo-wide verification confirms the spec's claims:
- `FileSizeBytes` on `UploadDocumentRequest` (`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs:11`) is set only once, in `KnowledgeBaseController.UploadDocument` (`backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs:144`), and read nowhere — `UploadDocumentHandler.cs` only consumes `FileStream`, `ContentType`, `Filename`, and `DocumentType`.
- No test in `backend/test/Anela.Heblo.Tests/KnowledgeBase/**` references `FileSizeBytes` (confirmed via direct grep against `UploadDocumentHandlerTests.cs` and `KnowledgeBaseControllerTests.cs` — zero matches).
- All other repo-wide `FileSizeBytes` hits belong to unrelated, actively-used fields: `Photobank.Photo`/`PhotoDto`/`PhotobankGraphService`/`PhotobankIndexJob`, `Leaflet.UploadLeafletRequest`, `FileStorage.DownloadFromUrlResponse`/`Handler`, `Catalog.ProductExportDownloadJob`, plus EF Core migration snapshots for the `Photo` entity's own `FileSizeBytes` column (unrelated to this change, must not be touched). None of these are in the KnowledgeBase upload path.

No other consumer, serializer, or downstream type (`IndexDocumentRequest`, `KnowledgeBaseDocument`) references the field. Removal is safe and behavior-neutral.

## Proposed Architecture

### Component Overview
No new components. The change removes one property and one assignment inside the existing `UploadDocument` use case slice:
- `Anela.Heblo.API.Controllers.KnowledgeBaseController` (API layer, constructs `UploadDocumentRequest`)
- `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument.UploadDocumentRequest` (Application layer, MediatR request DTO)

### Key Design Decisions

#### Decision 1: Remove vs. keep-for-future-use
**Options considered:**
1. Remove the property and its assignment now (per brief/spec).
2. Keep it as a documented placeholder for a future max-upload-size check.

**Chosen approach:** Remove it now (option 1), matching the spec.

**Rationale:** Project convention is explicit YAGNI — speculative future use is not a valid reason to retain dead code (per the brief and general project norms). If size validation is needed later, it can be reintroduced at that point with an actual consumer, avoiding drift between the stored value and its (currently nonexistent) usage.

## Implementation Guidance

### Directory / Module Structure
No structural changes. Modify exactly two existing files:
1. `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs` — delete line 11 (`public long FileSizeBytes { get; set; }`).
2. `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — delete line 144 (`FileSizeBytes = file.Length,`) inside the `UploadDocument` action's `UploadDocumentRequest` initializer.

### Interfaces and Contracts
- `UploadDocumentRequest` (internal MediatR request, not an OpenAPI-exposed type since it's constructed server-side from `IFormFile`) loses one member. No client-facing contract changes: the `multipart/form-data` endpoint still accepts `file` and `documentType`; `UploadDocumentResponse` is untouched.
- No OpenAPI/TypeScript client regeneration is needed — `UploadDocumentRequest` is not bound via `[FromBody]`/`[FromQuery]` from the client, it's built server-side, so it isn't part of the generated client surface.

### Data Flow
Unchanged except for the removal of a value that was never read: `IFormFile` → controller (drops `file.Length`) → `UploadDocumentRequest` (no longer carries size) → `UploadDocumentHandler` → `IndexDocumentRequest`/`KnowledgeBaseDocument` (already had no size field). No downstream step observes a behavior difference.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden reflection-based or serialization-based consumer of `FileSizeBytes` not caught by static grep | Low | `dotnet build` will fail loudly if any C# reference remains; grep across `.cs`/`.ts`/`.tsx` already found none in the KnowledgeBase path. |
| Future need for upload size limits reintroduces the same pattern | Low | Out of scope per spec; noted as a natural follow-up if/when size validation is required — should be added with real enforcement logic, not a dormant field. |

## Specification Amendments
None. The spec is accurate, self-contained, and already verified against the codebase (FR-1/FR-2/FR-3 match the actual file contents and line numbers found during this review).

## Prerequisites
None. Implementation can start immediately — it is a two-line deletion in two files with no dependencies.
