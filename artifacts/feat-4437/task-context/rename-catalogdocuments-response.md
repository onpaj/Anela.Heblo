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

