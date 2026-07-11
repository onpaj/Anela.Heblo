# Design: Remove unused `FileSizeBytes` from KnowledgeBase document upload

## Component Design

No new or restructured components. This is a shrink-the-surface change to two existing members of the `KnowledgeBase/UseCases/UploadDocument` vertical slice; their responsibilities and collaborators are otherwise unchanged.

- **`Anela.Heblo.API.Controllers.KnowledgeBaseController.UploadDocument`** (API layer)
  Responsibility: accept the `multipart/form-data` upload (`file`, `documentType`), construct an `UploadDocumentRequest`, dispatch it via MediatR.
  Contract change: the `UploadDocumentRequest` object initializer drops the `FileSizeBytes = file.Length` line. `file.Length` is read nowhere else in the action after removal. The action's own signature, route, and accepted form fields are unchanged.

- **`Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument.UploadDocumentRequest`** (Application layer, internal MediatR request)
  Responsibility: carry `FileStream`, `ContentType`, `Filename`, `DocumentType` from controller to handler.
  Contract change: loses the `public long FileSizeBytes { get; set; }` member. Since this type is constructed server-side (not `[FromBody]`/`[FromQuery]`-bound), it is not part of the generated OpenAPI/TypeScript client surface — removing the member requires no client regeneration and has no client-visible contract.

- **`UploadDocumentHandler`, `UploadDocumentResponse`, `IndexDocumentRequest`, `IndexDocumentHandler`, `KnowledgeBaseDocument`**
  Unaffected. None of these types read or expose `FileSizeBytes` today; none gain or lose members as part of this change. They are listed here only to record that their boundaries are explicitly untouched.

No cross-module impact: `FileSizeBytes` usages in Photobank, Leaflet, and FileStorage are separate, unrelated fields on unrelated types and are out of scope.

## Data Schemas

No schema changes of any kind:

- **Database / entity schema**: unchanged. `KnowledgeBaseDocument` has no `FileSizeBytes` column today and none is introduced.
- **Public API request/response shapes**: unchanged. `POST /api/knowledgebase/documents/upload` continues to accept `multipart/form-data` with fields `file` and `documentType`, and returns the existing `UploadDocumentResponse` shape.
- **Internal DTO shape**: `UploadDocumentRequest` (server-side-only, not OpenAPI-exposed) loses the `FileSizeBytes: long` member; this is the only shape delta in the entire change, and it is invisible outside the API/Application layer boundary.
- **Event/message payloads**: none exist in this flow; none are affected.
