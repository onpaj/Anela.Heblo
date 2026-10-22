# Architecture Review: Disambiguate UploadDocumentResponse DTO names

## Skip Design: true
Pure rename refactor with no UI change.

## Architectural Fit Assessment
Aligns with the rule that DTOs are module-owned and names must be unambiguous in the flat OpenAPI namespace. Integration points: MediatR request/handler generics, two MVC controllers, one controller unit test, NSwag-generated TS client, two frontend hooks.

## Proposed Architecture

### Component Overview
Rename-only. No new components. Each module's response type is referenced by its Request (`IRequest<T>`), Handler, and Controller.

### Key Design Decisions

#### Decision 1: Names
**Options considered:** `KnowledgeBaseUploadDocumentResponse` vs `UploadKnowledgeBaseDocumentResponse`; `CatalogDocumentsUploadDocumentResponse` vs `CatalogUploadDocumentResponse`.
**Chosen approach:** `KnowledgeBaseUploadDocumentResponse` and `CatalogDocumentsUploadDocumentResponse`.
**Rationale:** Module-name prefix matches folder/module names exactly, and keeps symmetry.

#### Decision 2: File location
**Chosen approach:** Keep each file where it is (KB: `UseCases/UploadDocument/`; Catalog: `Contracts/`), renaming the file with `git mv` to match the class. Moving KB into Contracts is out of scope (surgical changes).

#### Decision 3: Generated client
The client `api-client.ts` is tracked in git. Regenerate via the documented build step and commit the result, so the diff is consistent.

## Implementation Guidance

### Directory / Module Structure
Backend files to touch:
- KB: `UploadDocumentResponse.cs` (rename), `UploadDocumentHandler.cs`, `UploadDocumentRequest.cs`, `API/Controllers/KnowledgeBaseController.cs` (lines ~151,157,160), `Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs` (~88,117).
- Catalog: `Contracts/UploadDocumentResponse.cs` (rename, incl. constructors), `UploadMaterialDocumentHandler/Request.cs`, `UploadPifDocumentHandler/Request.cs`, `API/Controllers/CatalogDocumentsController.cs` (~51,83).
Frontend: `src/api/hooks/useKnowledgeBase.ts`, `src/api/hooks/useCatalogDocuments.ts`, regenerated `src/api/generated/api-client.ts`.

### Interfaces and Contracts
Properties and constructors unchanged. JSON wire format identical.

### Data Flow
Unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed reference breaks build | Low | `dotnet build` of whole solution; grep for old name |
| Generated client regenerated differently/with unrelated churn | Medium | Review diff; only rename-related changes should appear; if tooling unavailable, hand-edit class/interface/usages consistently |
| Handler tests in other test projects reference old name | Low | grep across `backend/test` (only KB controller test found) |

## Specification Amendments
None.

## Prerequisites
None. No migrations or config.
