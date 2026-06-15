## Module
KnowledgeBase

## Finding

`ResolveContentType` is a private static method that maps generic `application/octet-stream` MIME types to proper MIME types based on file extension. It exists as two identical copies:

- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` lines 73–84
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` lines 141–152

Both implementations are identical — same 5 extensions (`.pdf`, `.docx`, `.doc`, `.txt`, `.md`), same fallback, same `OrdinalIgnoreCase` comparison.

Additionally, when a file is uploaded via `UploadDocumentHandler`, the method runs twice: `UploadDocumentHandler` resolves the type before constructing `IndexDocumentRequest`, then `IndexDocumentHandler` resolves it again on the already-resolved value (where it becomes a no-op). The double call is harmless today but obscures intent.

## Why it matters

Adding support for a new extension (e.g. `.xlsx`) requires editing both files independently. If only one is updated, `UploadDocumentHandler` and `IndexDocumentHandler` (called directly by the ingestion job) will return different results for the same input — a silent correctness divergence.

## Suggested fix

Extract to a single static helper, e.g. a `ContentTypeResolver` static class at `Application/Features/KnowledgeBase/`:

```csharp
internal static class ContentTypeResolver
{
    public static string Resolve(string contentType, string filename) =>
        string.IsNullOrEmpty(contentType) || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? Path.GetExtension(filename).ToLowerInvariant() switch
            {
                ".pdf"  => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc"  => "application/msword",
                ".txt"  => "text/plain",
                ".md"   => "text/markdown",
                _       => contentType
            }
            : contentType;
}
```

Both handlers call `ContentTypeResolver.Resolve(...)`. Since `UploadDocumentHandler` already resolves before dispatching `IndexDocumentRequest`, `IndexDocumentHandler` can simply trust `request.ContentType` is already resolved and drop its own call.

---
_Filed by daily arch-review routine on 2026-06-11._