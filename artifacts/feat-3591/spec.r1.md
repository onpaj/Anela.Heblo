# Specification: Remove unused `FileSizeBytes` from KnowledgeBase document upload

## Summary
`UploadDocumentRequest.FileSizeBytes` is set by `KnowledgeBaseController.UploadDocument` but never read by `UploadDocumentHandler` or anything downstream. This spec removes the dead property and its assignment, eliminating a misleading, unused field with no behavior change to the upload flow.

## Background
During a code audit of the KnowledgeBase module, `UploadDocumentRequest.FileSizeBytes` was found to be populated from `IFormFile.Length` in the controller but never consumed by `UploadDocumentHandler`, which only reads `FileStream`, `ContentType`, `Filename`, and `DocumentType`. The value does not propagate to `IndexDocumentRequest` or to the `KnowledgeBaseDocument` entity, which has no corresponding field. Verification against the current codebase confirms this is a true dead property (not merely under-documented): no size limit, size validation, or size persistence logic exists anywhere in the KnowledgeBase upload pipeline. Per project convention (YAGNI, no speculative future use), dead request properties should be removed rather than retained "just in case."

Note: `FileSizeBytes` is a legitimate, actively-used field in other unrelated modules (Photobank, Leaflet, FileStorage) — this change is scoped strictly to the KnowledgeBase upload request/controller and does not touch those.

## Functional Requirements

### FR-1: Remove `FileSizeBytes` from `UploadDocumentRequest`
Delete the `public long FileSizeBytes { get; set; }` property (line 11) from `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`.

**Acceptance criteria:**
- `UploadDocumentRequest` no longer declares a `FileSizeBytes` member.
- `UploadDocumentHandler` and `UploadDocumentResponse` are unaffected (they already do not reference this property).

### FR-2: Remove the corresponding assignment in the controller
Delete the `FileSizeBytes = file.Length,` line (line 144) from the `UploadDocumentRequest` object-initializer in `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` (`UploadDocument` action).

**Acceptance criteria:**
- The controller no longer references `FileSizeBytes` anywhere.
- `file.Length` is not read or used elsewhere in the action after removal (confirmed: it was only used for this assignment).

### FR-3: No behavior change to the upload API
The `POST documents/upload` endpoint's external contract (accepted `multipart/form-data` fields: `file`, `documentType`) and response shape (`UploadDocumentResponse`) must remain unchanged. This is purely an internal dead-code removal.

**Acceptance criteria:**
- No changes to `UploadDocumentResponse`, `IndexDocumentRequest`, `IndexDocumentHandler`, or `KnowledgeBaseDocument`.
- Existing upload requests/responses (as exercised by `UploadDocumentHandlerTests.cs` and `KnowledgeBaseControllerTests.cs`) continue to pass without modification, unless those tests explicitly set `FileSizeBytes` on the request (verified: they do not — no matches found in `backend/test/Anela.Heblo.Tests/KnowledgeBase`).

## Non-Functional Requirements

### NFR-1: Performance
N/A — no measurable performance impact; removing an unused field/assignment is a no-op at runtime.

### NFR-2: Security
N/A — no security-relevant behavior involved (no size validation exists before or after this change; none is being added or removed in terms of enforcement).

## Data Model
No data model changes. `KnowledgeBaseDocument` (entity) has no `FileSizeBytes` field today and none is being added.

## API / Interface Design
No public API changes. The `POST /api/knowledgebase/documents/upload` endpoint's request (multipart form: `file`, `documentType`) and response (`UploadDocumentResponse`) contracts are unchanged; this is an internal-only cleanup of an unused C# property.

## Dependencies
None. Self-contained change within `Anela.Heblo.Application` (KnowledgeBase UseCases) and `Anela.Heblo.API` (KnowledgeBaseController).

## Out of Scope
- Adding file-size validation or a maximum upload size limit (explicitly deferred per the brief's suggested fix — can be introduced later if/when needed).
- Any changes to the Photobank, Leaflet, or FileStorage modules' `FileSizeBytes` usage — those are legitimate, unrelated usages and must not be touched.
- Any changes to `IndexDocumentRequest`, `IndexDocumentHandler`, or `KnowledgeBaseDocument`.

## Open Questions
None. Verified directly against the current codebase: `UploadDocumentHandler.cs` (backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs) confirms `FileSizeBytes` is never read, and no test in `backend/test/Anela.Heblo.Tests/KnowledgeBase` references it, so no test changes are anticipated. Standard validation gates (`dotnet build`, `dotnet format`, affected tests) apply per project rules.

## Status: COMPLETE
