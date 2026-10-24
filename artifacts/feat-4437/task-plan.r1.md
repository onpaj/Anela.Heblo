# Implementation Plan: Disambiguate UploadDocumentResponse DTO names

### task: rename-knowledgebase-response

**Goal:** Rename the KnowledgeBase DTO to `KnowledgeBaseUploadDocumentResponse`.

**Files (under `backend/`):**
- `src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs` -> `git mv` to `KnowledgeBaseUploadDocumentResponse.cs`, rename class
- `.../UploadDocument/UploadDocumentHandler.cs` (class generic arg, `Handle` return type, two `new` expressions)
- `.../UploadDocument/UploadDocumentRequest.cs` (`IRequest<...>`)
- `src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` (return type, two `BadRequest(new ...)`)
- `test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs` (lines ~88, ~117)

**Steps:**
- [ ] Rename file/class and update all references listed above (no behaviour change).
- [ ] `cd backend && dotnet build` passes (note: Catalog still has its own `UploadDocumentResponse`).
- [ ] `dotnet test --filter KnowledgeBase` passes.
- [ ] `dotnet format` clean.
- [ ] Commit: `refactor(knowledgebase): rename UploadDocumentResponse to KnowledgeBaseUploadDocumentResponse (#4437)`

### task: rename-catalogdocuments-response

**Goal:** Rename the CatalogDocuments DTO to `CatalogDocumentsUploadDocumentResponse`.

**Files (under `backend/`):**
- `src/Anela.Heblo.Application/Features/CatalogDocuments/Contracts/UploadDocumentResponse.cs` -> `git mv` to `CatalogDocumentsUploadDocumentResponse.cs`; rename class and both constructors
- `.../UseCases/UploadMaterialDocument/UploadMaterialDocumentHandler.cs` and `UploadMaterialDocumentRequest.cs`
- `.../UseCases/UploadPifDocument/UploadPifDocumentHandler.cs` and `UploadPifDocumentRequest.cs`
- `src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs` (lines ~51, ~83)

**Steps:**
- [ ] Rename file/class/ctors and update references.
- [ ] `grep -rn "\bUploadDocumentResponse\b" backend` returns nothing.
- [ ] `dotnet build`, `dotnet test --filter CatalogDocuments`, `dotnet format` pass.
- [ ] Commit: `refactor(catalogdocuments): rename UploadDocumentResponse to CatalogDocumentsUploadDocumentResponse (#4437)`

### task: regenerate-client-update-hooks

**Goal:** Regenerate the TS client and update frontend hooks. Depends on both tasks above.

**Files (under `frontend/`):**
- `src/api/generated/api-client.ts` (tracked; regenerate per `docs/development/api-client-generation.md`; if the generator cannot run, hand-edit: classes `UploadDocumentResponse` -> `CatalogDocumentsUploadDocumentResponse`, `UploadDocumentResponse2` -> `KnowledgeBaseUploadDocumentResponse`, plus their `I...` interfaces, `fromJS` and method signatures)
- `src/api/hooks/useKnowledgeBase.ts` (lines ~14, ~249)
- `src/api/hooks/useCatalogDocuments.ts` (lines ~6, ~71, ~95)

**Steps:**
- [ ] Regenerate client; review diff contains only rename-related changes.
- [ ] Update hook imports and type annotations.
- [ ] `grep -rn "UploadDocumentResponse" frontend/src` shows no un-prefixed or `2` names (excluding `.artifacts` dirs).
- [ ] `npm run build` and `npm run lint` pass; run related jest tests (`npm test -- useKnowledgeBase useCatalogDocuments`).
- [ ] Commit: `refactor(frontend): use module-scoped upload response types (#4437)`
