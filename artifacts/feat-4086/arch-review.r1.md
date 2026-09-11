# Architecture Review: KnowledgeBase UploadDocumentRequest — replace raw Stream with byte[]

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal boundary fix with no visible surface change: no new HTTP contract, no new UI, no new persistence. It aligns directly with this project's existing Application-layer boundary rule ("Application layer must not perform infrastructure I/O"; DTOs are classes that must stay serializable and side-effect-free) and with the pattern already used one layer downstream in the very same flow: `IndexDocumentRequest.Content` is already `byte[]`, populated by `UploadDocumentHandler` today. This fix simply pushes the existing `Stream → byte[]` conversion one hop earlier, from the handler into the controller, which is exactly where Clean Architecture places HTTP/IO concerns in this codebase (controllers are the infrastructure/API boundary; MediatR requests are Application-layer contracts).

There is no architectural risk or open design question here — the target shape (`byte[] Content` on the request DTO, controller does the stream read) is already the established convention elsewhere in the same codebase (`IndexDocumentRequest`, and generally any request DTO that needs binary payload). This is a mechanical, single-flow refactor.

## Proposed Architecture

### Component Overview
```
Before:
  KnowledgeBaseController.UploadDocument
    IFormFile.OpenReadStream() ──(Stream)──▶ UploadDocumentRequest.FileStream
                                                      │  (Application layer boundary — VIOLATION)
                                                      ▼
                                          UploadDocumentHandler.Handle
                                            CopyToAsync(MemoryStream) → byte[] fileBytes
                                                      │
                                                      ▼
                                          IndexDocumentRequest.Content (byte[])

After:
  KnowledgeBaseController.UploadDocument
    IFormFile.OpenReadStream() → CopyToAsync(MemoryStream) → byte[]
                                                      │  (stream I/O now stays at the infra boundary)
                                                      ▼
                                          UploadDocumentRequest.Content (byte[]) ──▶ Application layer
                                                      │
                                                      ▼
                                          UploadDocumentHandler.Handle
                                            (reads request.Content directly, no stream I/O)
                                                      │
                                                      ▼
                                          IndexDocumentRequest.Content (byte[])   [unchanged]
```

No new components. The change is confined to the existing three files in the KnowledgeBase upload use case plus their test.

### Key Design Decisions

#### Decision 1: Where the stream-to-bytes conversion moves
**Options considered:**
1. Move the exact `MemoryStream`/`CopyToAsync` block from handler to controller, unchanged (mechanical relocation).
2. Have the controller call `IFormFile.OpenReadStream()` then `.ToArray()` via a different helper (e.g. a shared `IFormFile` extension method) to avoid duplicating the `MemoryStream` boilerplate across the four sibling upload controllers.
**Chosen approach:** Option 1 — mechanical relocation, no new shared helper.
**Rationale:** The brief and spec explicitly scope this fix to the KnowledgeBase module only; the three sibling flows (`CatalogDocuments/UploadMaterialDocument`, `UploadPifDocument`, `Leaflet/UploadLeaflet`) are out of scope for issue #4086. Introducing a shared extension/helper now would touch code outside this issue's scope and is exactly the kind of "improve adjacent code" move the project's surgical-changes rule (CLAUDE.md) says not to do uninvited. If the sibling flows get their own arch-review findings later, a shared helper can be considered then, across all four call sites at once — not speculatively here.

#### Decision 2: Field name — `Content` vs `FileContent`/keeping `FileStream` name
**Options considered:**
1. Rename `FileStream` → `Content` (matches `IndexDocumentRequest.Content`, the field it flows into one hop later).
2. Rename to `FileContent` (more descriptive, avoids ambiguity with other "Content" fields like `ContentType`).
3. Keep the name `FileStream` but change only the type to `byte[]` (minimal diff, but a misleading name — a `byte[]` called "FileStream" is confusing for the next reader).
**Chosen approach:** Option 1 — rename to `Content`.
**Rationale:** The issue's own suggested fix (in the brief) explicitly names the replacement property `Content`, and the immediately-downstream `IndexDocumentRequest.Content` already uses this exact name for the identical byte payload — keeping the name consistent along the pipeline is clearer than introducing a new name (`FileContent`) or keeping a now-inaccurate one (`FileStream`).

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit in place:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs`

### Interfaces and Contracts
`UploadDocumentRequest` (Application-layer MediatR request, a class per project DTO rules — no change needed there, it is already a class):
```csharp
public class UploadDocumentRequest : IRequest<UploadDocumentResponse>
{
    public byte[] Content { get; set; } = [];
    public string Filename { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
```
No other type in the KnowledgeBase upload flow changes shape. `IndexDocumentRequest`, `UploadDocumentResponse`, `IDocumentTextExtractor`, `ContentTypeResolver` are all untouched — `IndexDocumentRequest.Content` already receives a `byte[]` today and continues to receive one, just sourced one hop earlier.

### Data Flow
1. `KnowledgeBaseController.UploadDocument` validates `file` and `documentType` exactly as today (unchanged, still `BadRequest` before touching the stream).
2. Controller opens `file.OpenReadStream()`, copies it into a `MemoryStream`, and calls `.ToArray()` — this is the same code the handler runs today, just relocated and using the controller's own `ct` (already in scope).
3. Controller constructs `UploadDocumentRequest { Content = bytes, Filename = file.FileName, ContentType = file.ContentType, DocumentType = parsedDocumentType }` and sends it via `_mediator.Send`.
4. `UploadDocumentHandler.Handle` no longer touches any stream — it resolves `contentType`, checks extractor support, builds `sourcePath`, and forwards `request.Content` straight into `IndexDocumentRequest.Content`. All of this logic is otherwise byte-for-byte unchanged.
5. Response mapping (`MapToSummary`) is untouched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missing a call site still constructing `UploadDocumentRequest` with `FileStream` (e.g. a test) causes a compile error | Low | `grep -rn "FileStream" backend/` before/after to confirm only `UploadDocumentHandlerTests.cs` and the controller reference the KnowledgeBase `UploadDocumentRequest`'s `FileStream` field; both are already identified in the spec (FR-2, FR-4). |
| Confusing this fix's scope with the three sibling `Stream FileStream` flows and touching them too | Medium | Spec explicitly marks siblings Out of Scope; this review reaffirms it (Decision 1). Stay confined to the four files listed above. |
| `dotnet build`/`dotnet format` catching an unused `using Anela.Heblo.Domain.Shared.Rag;` or similar after removing `Stream`-related code | Low | `UploadDocumentRequest.cs`'s only `System.IO`-relevant thing is the `Stream` type itself (implicit, no explicit `using System.IO;` in the current file) — no using-directive cleanup expected, but run `dotnet build` + `dotnet format` per CLAUDE.md validation step regardless. |

## Specification Amendments
None. The spec (`spec.r1.md`) is implementable as written; this review only adds the rationale for naming (`Content`) and confirms scope boundaries already stated in the spec's Out of Scope section.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — this is a same-commit, self-contained code change across the four files listed above.
