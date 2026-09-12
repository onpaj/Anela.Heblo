# Implementation: migrate-upload-document-request-to-bytes

## What was implemented
Moved the stream-to-bytes conversion for document uploads out of the Application layer and into the API controller, so `UploadDocumentRequest` (a MediatR request/DTO) no longer carries a raw `Stream`. This keeps HTTP/IO concerns at the API boundary and out of the Application layer, consistent with the arch-review finding for issue #4086.

- `UploadDocumentRequest.Stream FileStream` was replaced with `byte[] Content`.
- `UploadDocumentHandler` no longer reads the incoming stream itself; it now reads `request.Content` directly and passes it through to `IndexDocumentRequest.Content`.
- `KnowledgeBaseController.UploadDocument` now reads the uploaded `IFormFile`'s stream into a `MemoryStream`/`byte[]` itself and populates `UploadDocumentRequest.Content` with the resulting bytes before dispatching via MediatR.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs` — replaced `Stream FileStream { get; set; } = default!;` with `byte[] Content { get; set; } = [];`.
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` — removed the `MemoryStream`/`CopyToAsync` stream-read block; handler now uses `request.Content` directly when building `IndexDocumentRequest`.
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — `UploadDocument` action now copies the uploaded file's stream into a `MemoryStream` and sets `Content = ms.ToArray()` on the `UploadDocumentRequest` instead of passing `FileStream = stream`.

## Tests
Test files are handled by a later task and were **not** touched here, as instructed. They still reference `UploadDocumentRequest.FileStream` / the old shape, so the test projects are expected to fail to compile at this point — this is expected and out of scope for this task (production code only was required to compile).

## How to verify
1. `cd backend/src/Anela.Heblo.Application && dotnet build Anela.Heblo.Application.csproj` — builds with 0 errors.
2. `cd backend/src/Anela.Heblo.API && dotnet build Anela.Heblo.API.csproj` — builds with 0 errors (this project references Application, so it also validates the DTO usage end-to-end).
3. `grep -rn "UploadDocumentRequest" backend/src/` — only three matches: the definition in `UploadDocumentRequest.cs`, the handler's use in `UploadDocumentHandler.cs`, and the construction in `KnowledgeBaseController.cs`. No remaining `FileStream` reference on this DTO (other `FileStream` hits in `backend/src/` belong to unrelated DTOs: `UploadMaterialDocumentRequest`, `UploadPifDocumentRequest`, `UploadLeafletRequest` — out of scope).
4. `git show 9679b3e` — inspect the commit diff (3 files changed, 6 insertions, 7 deletions).

## Notes
No deviations from the task spec. The task-context file's step-by-step diffs matched the pre-existing file contents exactly, so all edits were applied verbatim as specified. One pre-existing unrelated change (`artifacts/feat-4086/state.json`, modified before this task started) remains uncommitted in the working tree — it was intentionally left out of this commit since it is unrelated to the DTO migration.

## Status
DONE
