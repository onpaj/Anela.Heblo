### task: remove-filesizebytes-dead-property

**Goal:** Delete the dead `FileSizeBytes` property from `UploadDocumentRequest` and its corresponding assignment in `KnowledgeBaseController.UploadDocument`, with no other behavior change to the upload flow.

**Context:** `UploadDocumentRequest.FileSizeBytes` is populated in `KnowledgeBaseController.UploadDocument` from `IFormFile.Length` but is never read by `UploadDocumentHandler` (which only consumes `FileStream`, `ContentType`, `Filename`, `DocumentType`), never propagated to `IndexDocumentRequest`, and has no corresponding field on the `KnowledgeBaseDocument` entity. Repo-wide verification (spec + arch-review) confirms this is genuinely dead code: no size validation, size limit, or size persistence exists anywhere in the KnowledgeBase upload pipeline, and no test references the property. This is a pure two-line deletion across two files within a single vertical slice (`KnowledgeBase/UseCases/UploadDocument`) — no module boundary crossing, no persistence change, no public API contract change.

`FileSizeBytes` is a separate, legitimate, actively-used field in unrelated modules (Photobank, Leaflet, FileStorage) and in EF Core migration snapshots for the `Photo` entity — do not touch any of those; this change is scoped strictly to the KnowledgeBase upload request/controller.

`UploadDocumentRequest` is a server-side-constructed MediatR request (not `[FromBody]`/`[FromQuery]`-bound), so it is not part of the generated OpenAPI/TypeScript client surface — no client regeneration is needed.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs` — delete the `public long FileSizeBytes { get; set; }` property (line 11).
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — delete the `FileSizeBytes = file.Length,` line (line 144) from the `UploadDocumentRequest` object initializer inside the `UploadDocument` action. Confirm `file.Length` is not used elsewhere in the action after this removal; if it becomes unused, no further action is needed since it's a property access, not a variable declaration.

**Implementation steps:**
1. Open `UploadDocumentRequest.cs` and remove the `FileSizeBytes` property declaration.
2. Open `KnowledgeBaseController.cs`, locate the `UploadDocument` action, and remove the `FileSizeBytes = file.Length,` line from the `UploadDocumentRequest` object initializer.
3. Search the codebase (`backend/`) for any other reference to `UploadDocumentRequest.FileSizeBytes` to confirm none remain (none are expected per spec/arch-review verification).
4. Run `dotnet build` to confirm no compile errors (a stray reference would fail the build).
5. Run `dotnet format` to ensure formatting stays clean after the deletions.

**Tests to write:**
No new tests are required; this is pure removal of a property nothing reads. Verify the existing KnowledgeBase test suite (`UploadDocumentHandlerTests.cs`, `KnowledgeBaseControllerTests.cs` in `backend/test/Anela.Heblo.Tests/KnowledgeBase`) still passes unmodified, since no test references `FileSizeBytes`.

**Acceptance criteria:**
- `UploadDocumentRequest` no longer declares a `FileSizeBytes` member (FR-1).
- `UploadDocumentHandler` and `UploadDocumentResponse` are unaffected and unchanged (FR-1).
- The controller no longer references `FileSizeBytes` anywhere, and `file.Length` is not read or used elsewhere in the `UploadDocument` action after removal (FR-2).
- The `POST /api/knowledgebase/documents/upload` endpoint's external contract (accepted `multipart/form-data` fields: `file`, `documentType`) and response shape (`UploadDocumentResponse`) remain unchanged (FR-3).
- No changes made to `UploadDocumentResponse`, `IndexDocumentRequest`, `IndexDocumentHandler`, or `KnowledgeBaseDocument` (FR-3).
- Existing tests in `UploadDocumentHandlerTests.cs` and `KnowledgeBaseControllerTests.cs` pass without modification (FR-3).
- `dotnet build` and `dotnet format` succeed with no errors.
