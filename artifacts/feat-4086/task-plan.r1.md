# KnowledgeBase UploadDocumentRequest byte[] Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `UploadDocumentRequest.FileStream` (`System.IO.Stream`) with `UploadDocumentRequest.Content` (`byte[]`), moving the stream-to-bytes conversion from `UploadDocumentHandler` (Application layer) into `KnowledgeBaseController` (API/infrastructure layer), with no change to external behavior or the HTTP contract.

**Architecture:** The controller already owns the `IFormFile`; it will now perform the existing `OpenReadStream()` → `CopyToAsync(MemoryStream, ct)` → `ToArray()` conversion itself and pass the resulting `byte[]` on the request DTO. The handler drops all stream I/O and reads `request.Content` directly, forwarding it unchanged to `IndexDocumentRequest.Content` exactly as it does today. This is a mechanical, compile-coupled change across three production files plus their test file — no new types, no contract change.

**Tech Stack:** .NET 8, MediatR, ASP.NET Core (`IFormFile`), xUnit, Moq.

---

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

### task: update-upload-document-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs`

This task depends on `migrate-upload-document-request-to-bytes` being done first (the test file will not compile against the old `UploadDocumentRequest` shape once that task lands, and this task fixes exactly that).

- [ ] **Step 1: Update `Handle_NewDocument_IndexesAndReturnsIndexedStatus`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("pdf content"u8.ToArray()),
            Filename = "guide.pdf",
            ContentType = "application/pdf",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "pdf content"u8.ToArray(),
            Filename = "guide.pdf",
            ContentType = "application/pdf",
        };
```

- [ ] **Step 2: Update `Handle_OctetStreamWithTxtExtension_ResolvesToTextPlainAndIndexes`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("plain text"u8.ToArray()),
            Filename = "readme.txt",
            ContentType = "application/octet-stream",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "plain text"u8.ToArray(),
            Filename = "readme.txt",
            ContentType = "application/octet-stream",
        };
```

- [ ] **Step 3: Update `Handle_OctetStreamWithDocxExtension_ResolvesToDocxContentType`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("docx bytes"u8.ToArray()),
            Filename = "document.docx",
            ContentType = "application/octet-stream",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "docx bytes"u8.ToArray(),
            Filename = "document.docx",
            ContentType = "application/octet-stream",
        };
```

- [ ] **Step 4: Update `Handle_UnsupportedFileType_ReturnsUnsupportedFileTypeErrorWithoutThrowing`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("binary"u8.ToArray()),
            Filename = "archive.zip",
            ContentType = "application/zip",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "binary"u8.ToArray(),
            Filename = "archive.zip",
            ContentType = "application/zip",
        };
```

- [ ] **Step 5: Update `Handle_IndexDocumentRequest_ContainsUploadSourcePath`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("content"u8.ToArray()),
            Filename = "doc.pdf",
            ContentType = "application/pdf",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "content"u8.ToArray(),
            Filename = "doc.pdf",
            ContentType = "application/pdf",
        };
```

- [ ] **Step 6: Confirm no remaining `FileStream` reference in this test file**

Run:
```bash
grep -n "FileStream" backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs
```
Expected: no output (no matches).

- [ ] **Step 7: Run the updated test file and confirm all five tests pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UploadDocumentHandlerTests"
```
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`.

- [ ] **Step 8: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs
git commit -m "test(knowledgebase): update UploadDocumentRequest construction to byte[] Content"
```

### task: verify-full-build-and-format

**Files:** none modified — verification only, per CLAUDE.md's "Validation before completion" rule.

- [ ] **Step 1: Full backend build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build succeeds with 0 errors (warnings pre-existing elsewhere in the solution are not introduced by this change).

- [ ] **Step 2: Format check**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: no formatting violations reported for the four files touched in this plan. If it reports violations, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to apply fixes, then re-stage and amend/commit only the touched files.

- [ ] **Step 3: Run the full KnowledgeBase test suite (not just the one test class) to catch any missed reference**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBase"
```
Expected: all KnowledgeBase-related tests pass, 0 failed.

- [ ] **Step 4: grep-confirm no `FileStream` reference remains anywhere in the KnowledgeBase module (production or test)**

Run:
```bash
grep -rn "FileStream" backend/src/Anela.Heblo.Application/Features/KnowledgeBase backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs backend/test/Anela.Heblo.Tests/KnowledgeBase
```
Expected: no output (no matches). (The three sibling modules — `CatalogDocuments/UploadMaterialDocument`, `CatalogDocuments/UploadPifDocument`, `Leaflet/UploadLeaflet` — are explicitly out of scope per the spec and will still show `FileStream`; do not touch them.)
