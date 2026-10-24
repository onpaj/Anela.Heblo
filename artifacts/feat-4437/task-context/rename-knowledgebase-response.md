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

