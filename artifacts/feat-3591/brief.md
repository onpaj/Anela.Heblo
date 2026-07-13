## Module
KnowledgeBase

## Finding
`UploadDocumentRequest.FileSizeBytes` is populated by the controller but never consumed anywhere in the handler or downstream code.

**Set in the controller** (`backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`, line 144):
```csharp
var request = new UploadDocumentRequest
{
    FileStream = stream,
    Filename = file.FileName,
    ContentType = file.ContentType,
    FileSizeBytes = file.Length,   // ← set here
    DocumentType = parsedDocumentType,
};
```

**Declared in the request** (`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`, line 11):
```csharp
public long FileSizeBytes { get; set; }
```

`UploadDocumentHandler` reads `request.FileStream`, `request.ContentType`, `request.Filename`, and `request.DocumentType` — but never `request.FileSizeBytes`. The `KnowledgeBaseDocument` entity has no `FileSizeBytes` field, nor does `IndexDocumentRequest` or any other type in the pipeline.

## Why it matters
Dead request properties violate YAGNI, mislead future readers into thinking the value is used (or cause them to check multiple files looking for its consumer), and will silently drift from the actual file size if the controller code is ever refactored. Per project rules, speculative future use is not a reason to keep dead code.

## Suggested fix
Remove the property from `UploadDocumentRequest` and the corresponding assignment in `KnowledgeBaseController.UploadDocument`. If file-size validation (e.g. a maximum upload limit) is added in the future, it can be introduced at that point.

---
_Filed by daily arch-review routine on 2026-07-10._
