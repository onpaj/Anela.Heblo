# Specification: KnowledgeBase UploadDocumentRequest — replace raw Stream with byte[]

## Summary
`UploadDocumentRequest` in the KnowledgeBase module currently carries a raw `System.IO.Stream` (`FileStream`) into the Application layer, where `UploadDocumentHandler` reads it via `CopyToAsync`. This is an Application-layer boundary violation: HTTP/IO-stream handling belongs at the controller (infrastructure) boundary, not inside a MediatR request DTO. This spec covers converting `UploadDocumentRequest` to carry `byte[] Content` instead, with the stream-to-bytes conversion moved into `KnowledgeBaseController`.

## Background
`KnowledgeBaseController.UploadDocument` receives an `IFormFile`, opens its read stream, and passes that live `Stream` directly on `UploadDocumentRequest.FileStream` into `_mediator.Send(...)`. `UploadDocumentHandler.Handle` then performs `await request.FileStream.CopyToAsync(ms, cancellationToken)` to materialize `byte[] fileBytes`, which it forwards unchanged to `IndexDocumentRequest.Content` (already a `byte[]`).

This was flagged by the arch-review routine (issue #4086) because:
- The Application layer performs HTTP-stream I/O, which is an infrastructure-layer concern under this project's Clean Architecture rules (`docs/architecture/development_guidelines.md`).
- `UploadDocumentRequest` is non-serializable and cannot safely cross a process boundary.
- Any pipeline behavior that runs before the handler and touches the stream (logging, validation, caching) would silently exhaust or break it, leaving the handler with empty/broken content and no error.

The downstream shape is already correct — `IndexDocumentRequest.Content` is `byte[]` — so this is a boundary-only fix: move the existing `CopyToAsync` conversion from the handler up into the controller, and change the request DTO's field from `Stream` to `byte[]`.

Note: the identical `Stream FileStream` pattern also exists in three sibling upload flows (`CatalogDocuments/UploadMaterialDocument`, `CatalogDocuments/UploadPifDocument`, `Leaflet/UploadLeaflet`). Issue #4086 is scoped to the KnowledgeBase module only; the sibling flows are explicitly out of scope for this change (see Out of Scope).

## Functional Requirements

### FR-1: Replace `UploadDocumentRequest.FileStream` (Stream) with `Content` (byte[])
`UploadDocumentRequest` (`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`) must expose `byte[] Content` instead of `Stream FileStream`. The DTO must contain no `System.IO` types.

**Acceptance criteria:**
- `UploadDocumentRequest` has a `byte[] Content { get; set; }` property (default `Array.Empty<byte>()` or `[]`, matching the sibling `IndexDocumentRequest.Content` default style).
- `UploadDocumentRequest` no longer references `Stream` or `System.IO`.
- `Filename`, `ContentType`, and `DocumentType` properties are unchanged.

### FR-2: Move stream-to-bytes conversion into the controller
`KnowledgeBaseController.UploadDocument` (`backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`) must open the `IFormFile` stream, copy it into a `MemoryStream`, and pass the resulting `byte[]` as `UploadDocumentRequest.Content`.

**Acceptance criteria:**
- The controller performs the `OpenReadStream()` → `CopyToAsync(MemoryStream)` → `ToArray()` sequence (same sequence currently in the handler) before constructing `UploadDocumentRequest`.
- The controller passes the `CancellationToken` (`ct`) already in scope to `CopyToAsync`.
- `UploadDocumentRequest.Content` is set from the resulting `byte[]`; no other controller behavior changes (same `BadRequest` validation for missing file / bad `documentType` stays before the stream read).

### FR-3: Update the handler to consume `byte[]` directly
`UploadDocumentHandler.Handle` (`.../UploadDocumentHandler.cs`) must no longer perform stream I/O. It reads `request.Content` directly and forwards it unchanged to `IndexDocumentRequest.Content`, exactly as it does today after materializing `fileBytes`.

**Acceptance criteria:**
- The `using var ms = new MemoryStream(); await request.FileStream.CopyToAsync(ms, cancellationToken); var fileBytes = ms.ToArray();` block is removed.
- `IndexDocumentRequest.Content` is populated from `request.Content`.
- All other handler logic (content-type resolution, extractor capability check, `UnsupportedFileType` short-circuit, `sourcePath` construction, response mapping) is unchanged.

### FR-4: Update existing unit tests to the new DTO shape
`UploadDocumentHandlerTests` (`backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs`) constructs `UploadDocumentRequest` with `FileStream = new MemoryStream(...)` in all five test cases. These must be updated to set `Content = "...".u8 ToArray()`-style byte arrays directly (no `MemoryStream`).

**Acceptance criteria:**
- All five existing tests (`Handle_NewDocument_IndexesAndReturnsIndexedStatus`, `Handle_OctetStreamWithTxtExtension_ResolvesToTextPlainAndIndexes`, `Handle_OctetStreamWithDocxExtension_ResolvesToDocxContentType`, `Handle_UnsupportedFileType_ReturnsUnsupportedFileTypeErrorWithoutThrowing`, `Handle_IndexDocumentRequest_ContainsUploadSourcePath`) compile and pass unchanged in behavior/assertions, only the request construction changes.
- No test asserts on `Stream`-specific behavior (e.g. stream position, disposal) that would need to be dropped or reworked — confirm none currently do.

## Non-Functional Requirements

### NFR-1: Behavior parity
The upload endpoint's externally observable behavior (HTTP request/response shape, success/error responses, indexed document content) must be unchanged. This is a pure internal refactor of a layer boundary, not a functional or API contract change.

### NFR-2: No new allocations concern
Moving the `MemoryStream` copy from handler to controller does not change the number of times the file content is buffered in memory (still exactly once, still as a `byte[]` for the lifetime of the request) — so no meaningful performance or memory regression is introduced.

### NFR-3: Architecture compliance
After this change, no Application-layer type in the KnowledgeBase upload flow references `System.IO.Stream`. This satisfies the Clean Architecture boundary rule that flagged the finding.

## Data Model
No persistent data model changes. `UploadDocumentRequest` is a transient MediatR request DTO; its shape changes from `{ Stream FileStream, string Filename, string ContentType, DocumentType DocumentType }` to `{ byte[] Content, string Filename, string ContentType, DocumentType DocumentType }`. `IndexDocumentRequest` (downstream) and `UploadDocumentResponse` are unchanged.

## API / Interface Design
No public HTTP API contract change. `POST api/KnowledgeBase/documents/upload` keeps its existing multipart-form request shape (`IFormFile file`, `[FromForm] string documentType`) and existing `UploadDocumentResponse` response shape. The change is entirely internal to the Application-layer request DTO and its handler; it is not visible to API consumers or the generated OpenAPI/TypeScript client.

## Dependencies
None beyond the existing KnowledgeBase upload flow (`UploadDocumentRequest`, `UploadDocumentHandler`, `KnowledgeBaseController`, `IndexDocumentRequest`, `UploadDocumentHandlerTests`). No new packages, migrations, or feature flags required.

## Out of Scope
- The three sibling upload flows with the identical `Stream FileStream` pattern (`CatalogDocuments/UploadMaterialDocument`, `CatalogDocuments/UploadPifDocument`, `Leaflet/UploadLeaflet`) are **not** part of this change. Issue #4086 names only the KnowledgeBase module. Fixing the siblings would be a separate, follow-up arch-review finding/issue.
- No change to `IndexDocumentRequest`, `IndexDocumentHandler`, document extractors, or any indexing/storage logic downstream of `UploadDocumentHandler`.
- No change to the public HTTP contract, OpenAPI spec, or generated TypeScript client.
- No new tests beyond updating the five existing ones to the new DTO shape (no net-new test scenarios required by this fix, since behavior is unchanged).

## Open Questions
None.

## Status: COMPLETE
