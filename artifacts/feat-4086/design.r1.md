# Design: KnowledgeBase UploadDocumentRequest — replace raw Stream with byte[]

## Component Design

### `UploadDocumentRequest` (Application layer)
- File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`
- Responsibility: MediatR request DTO for uploading a document into the KnowledgeBase.
- Change: `Stream FileStream` property replaced by `byte[] Content`, defaulted to `[]`. No other property changes (`Filename`, `ContentType`, `DocumentType` stay as-is). No `System.IO` reference remains in this file.

### `KnowledgeBaseController.UploadDocument` (API/infrastructure layer)
- File: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`
- Responsibility: HTTP endpoint (`POST api/KnowledgeBase/documents/upload`), owns all IO/infrastructure concerns for this use case.
- Change: gains the stream-to-`byte[]` conversion (`OpenReadStream()` → `CopyToAsync(MemoryStream, ct)` → `ToArray()`) that currently lives in the handler. Existing validation (`file is null` / bad `documentType` → `BadRequest`) stays before this conversion, unchanged in order and behavior.

### `UploadDocumentHandler.Handle` (Application layer)
- File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`
- Responsibility: content-type resolution, extractor-capability check, delegating to `IndexDocumentRequest` via `IMediator`, response mapping.
- Change: no longer performs any stream I/O. Reads `request.Content` (already `byte[]`) directly and forwards it to `IndexDocumentRequest.Content`, exactly as `fileBytes` is forwarded today. All other logic (content-type resolution, unsupported-type short-circuit, `sourcePath` construction, `MapToSummary`) is unchanged.

### `UploadDocumentHandlerTests` (test project)
- File: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs`
- Change: all five test cases construct `UploadDocumentRequest` with `Content = <byte[]>` instead of `FileStream = new MemoryStream(<byte[]>)`. No assertion or setup logic changes — tests validate the same behaviors (indexing success, content-type resolution from octet-stream + extension, unsupported-type error, source-path construction).

## Data Schemas

### `UploadDocumentRequest` (before → after)
```csharp
// Before
public class UploadDocumentRequest : IRequest<UploadDocumentResponse>
{
    public Stream FileStream { get; set; } = default!;
    public string Filename { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}

// After
public class UploadDocumentRequest : IRequest<UploadDocumentResponse>
{
    public byte[] Content { get; set; } = [];
    public string Filename { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
```

No change to `IndexDocumentRequest`, `UploadDocumentResponse`, or any HTTP request/response wire shape — `POST api/KnowledgeBase/documents/upload` keeps its existing multipart-form request (`IFormFile file`, `[FromForm] string documentType`) and `UploadDocumentResponse` response body. This DTO change is entirely internal to the Application layer and invisible to API consumers, the OpenAPI spec, and the generated TypeScript client.
