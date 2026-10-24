# Specification: Disambiguate UploadDocumentResponse DTO names (KnowledgeBase vs CatalogDocuments)

## Summary
Two backend DTOs share the class name `UploadDocumentResponse`, causing NSwag to emit `UploadDocumentResponse2` for the KnowledgeBase variant. Rename both DTOs to module-scoped unique names so the generated TypeScript client has no numeric suffixes. No behaviour change.

## Background
Issue #4437 (arch-review finding). DTOs are module-owned; the OpenAPI namespace is flat, so names must be unambiguous. The suffix `2` is fragile and meaningless. Current state:
- `Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs` (namespace `...KnowledgeBase.UseCases.UploadDocument`, has `Document`)
- `Features/CatalogDocuments/Contracts/UploadDocumentResponse.cs` (has `UploadedFilename`, ctor with ErrorCodes)
- Frontend uses `UploadDocumentResponse2` in `useKnowledgeBase.ts` and `UploadDocumentResponse` in `useCatalogDocuments.ts`; `api-client.ts` is tracked in git and regenerated on build.

## Functional Requirements

### FR-1: Rename KnowledgeBase DTO
Rename class and file to `KnowledgeBaseUploadDocumentResponse` and update `UploadDocumentHandler`, `UploadDocumentRequest` (`IRequest<...>`), `KnowledgeBaseController` (return type and two `BadRequest` constructions), and `KnowledgeBaseControllerTests`.
**Acceptance criteria:**
- No type named `UploadDocumentResponse` remains under `Features/KnowledgeBase`.
- Public properties (`Document`, BaseResponse members) unchanged.

### FR-2: Rename CatalogDocuments DTO
Rename class and file to `CatalogDocumentsUploadDocumentResponse` and update `UploadMaterialDocumentHandler/Request`, `UploadPifDocumentHandler/Request`, `CatalogDocumentsController`.
**Acceptance criteria:**
- No type named `UploadDocumentResponse` remains in the backend.
- Constructors and `UploadedFilename` unchanged.

### FR-3: Regenerate frontend client and update hooks
Regenerate `frontend/src/api/generated/api-client.ts` (per `docs/development/api-client-generation.md`) and update `useKnowledgeBase.ts` and `useCatalogDocuments.ts` imports/types to the new names.
**Acceptance criteria:**
- `grep UploadDocumentResponse2` finds nothing; generated client has no `UploadDocumentResponse`/`...2` classes.
- `npm run build` and `npm run lint` pass.

## Non-Functional Requirements
### NFR-1: Performance
None affected.
### NFR-2: Security
None affected; wire JSON shape is identical (only OpenAPI schema names change).
### NFR-3: Compatibility
MCP tools/other consumers must not reference the old names (verified by grep: none found).

## Data Model
No change.

## API / Interface Design
HTTP routes, request and response JSON unchanged. Only OpenAPI component schema names change (`UploadDocumentResponse` -> `CatalogDocumentsUploadDocumentResponse`, `UploadDocumentResponse` (2) -> `KnowledgeBaseUploadDocumentResponse`).

## Dependencies
NSwag client generation on build; docs/development/api-client-generation.md.

## Out of Scope
Renaming requests/handlers; behaviour changes; historical plan docs under `docs/` and `.artifacts/`.

## Open Questions
None. Assumption: chosen names `KnowledgeBaseUploadDocumentResponse` and `CatalogDocumentsUploadDocumentResponse` (issue's primary suggestions).
