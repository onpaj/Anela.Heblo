### task: migrate-upload-document-request-to-bytes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs:27-29`
- Modify: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs:135-146`

These three files are compile-coupled (the DTO's field is used by both the handler and the controller), so this task changes all three together.

- [ ] **Step 1: Change the request DTO's field from `Stream FileStream` to `byte[] Content`**

Edit `UploadDocumentRequest.cs`. Replace the whole file body with:

```csharp
using Anela.Heblo.Domain.Shared.Rag;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentRequest : IRequest<UploadDocumentResponse>
{
    public byte[] Content { get; set; } = [];
    public string Filename { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
```

(Only the first property line changes — `Stream FileStream` → `byte[] Content`. `Filename`, `ContentType`, `DocumentType` are unchanged.)

- [ ] **Step 2: Remove the stream-read from the handler and read `request.Content` directly**

Edit `UploadDocumentHandler.cs`. Replace:

```csharp
    public async Task<UploadDocumentResponse> Handle(
        UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await request.FileStream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        var contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename);
```

with:

```csharp
    public async Task<UploadDocumentResponse> Handle(
        UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename);
```

Then update the `IndexDocumentRequest` construction later in the same method — replace:

```csharp
        var indexResponse = await _mediator.Send(new IndexDocumentRequest
        {
            Filename = request.Filename,
            SourcePath = sourcePath,
            ContentType = contentType,
            Content = fileBytes,
            DocumentType = request.DocumentType,
        }, cancellationToken);
```

with:

```csharp
        var indexResponse = await _mediator.Send(new IndexDocumentRequest
        {
            Filename = request.Filename,
            SourcePath = sourcePath,
            ContentType = contentType,
            Content = request.Content,
            DocumentType = request.DocumentType,
        }, cancellationToken);
```

No other line in this file changes — the extractor capability check, the `UnsupportedFileType` short-circuit, `sourcePath` construction, and `MapToSummary` are untouched.

- [ ] **Step 3: Move the stream-to-bytes conversion into the controller**

Edit `KnowledgeBaseController.cs`. Replace the body of `UploadDocument`:

```csharp
    [HttpPost("documents/upload")]
    [FeatureAuthorize(Feature.Customer_KnowledgeBase, AccessLevel.Write)]
    public async Task<ActionResult<UploadDocumentResponse>> UploadDocument(
        IFormFile file,
        [FromForm] string documentType = "KnowledgeBase",
        CancellationToken ct = default)
    {
        if (file is null)
            return BadRequest(new UploadDocumentResponse { Success = false });

        if (!Enum.TryParse<DocumentType>(documentType, ignoreCase: true, out var parsedDocumentType))
            return BadRequest(new UploadDocumentResponse { Success = false });

        await using var stream = file.OpenReadStream();
        var request = new UploadDocumentRequest
        {
            FileStream = stream,
            Filename = file.FileName,
            ContentType = file.ContentType,
            DocumentType = parsedDocumentType,
        };
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
```

with:

```csharp
    [HttpPost("documents/upload")]
    [FeatureAuthorize(Feature.Customer_KnowledgeBase, AccessLevel.Write)]
    public async Task<ActionResult<UploadDocumentResponse>> UploadDocument(
        IFormFile file,
        [FromForm] string documentType = "KnowledgeBase",
        CancellationToken ct = default)
    {
        if (file is null)
            return BadRequest(new UploadDocumentResponse { Success = false });

        if (!Enum.TryParse<DocumentType>(documentType, ignoreCase: true, out var parsedDocumentType))
            return BadRequest(new UploadDocumentResponse { Success = false });

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);

        var request = new UploadDocumentRequest
        {
            Content = ms.ToArray(),
            Filename = file.FileName,
            ContentType = file.ContentType,
            DocumentType = parsedDocumentType,
        };
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
```

- [ ] **Step 4: Confirm no other production code references the old `FileStream` field on this DTO**

Run:
```bash
grep -rn "UploadDocumentRequest" backend/src/
```
Expected: only `UploadDocumentRequest.cs` (definition), `UploadDocumentHandler.cs` (uses `request.Content`), and `KnowledgeBaseController.cs` (constructs it with `Content = ms.ToArray()`) — no remaining `FileStream` reference in `backend/src/`.

- [ ] **Step 5: Build to confirm production code compiles (tests will still fail to compile at this point — expected, fixed in the next task)**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```
Expected: both projects build with no errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
git commit -m "refactor(knowledgebase): move upload stream-to-bytes conversion to controller

UploadDocumentRequest now carries byte[] Content instead of a raw
Stream, keeping HTTP/IO concerns out of the Application layer.
Fixes arch-review finding #4086."
```

