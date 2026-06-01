# Multi-Folder OneDrive Ingestion Design

## Context

The KB embedding pipeline is fully implemented: OneDrive polling, document extraction, chunking, LLM summarization, OpenAI embedding, pgvector storage, and archival. However, the ingestion job currently polls a single inbox folder and always creates documents with `DocumentType.KnowledgeBase`. The `Conversation` indexing strategy exists but is only reachable via manual upload.

**Goal:** Support multiple OneDrive inbox folders, each mapped to a `DocumentType`, so that dropping a file into `/Conversation/Inbox` automatically triggers the conversation indexing strategy, and `/KnowledgeBase/Inbox` triggers the standard strategy. Processed files move to type-specific archived folders.

## Design

### Configuration

New `OneDriveFolderMapping` class added to `KnowledgeBaseOptions`:

```csharp
public class OneDriveFolderMapping
{
    public string InboxPath { get; set; } = string.Empty;
    public string ArchivedPath { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
```

`KnowledgeBaseOptions` changes:
- **Add** `List<OneDriveFolderMapping> OneDriveFolderMappings` with default mappings for both types
- **Remove** `OneDriveInboxPath` and `OneDriveArchivedPath` (replaced by mappings)

`appsettings.json`:
```json
"KnowledgeBase": {
  "OneDriveFolderMappings": [
    { "InboxPath": "/KnowledgeBase/Inbox", "ArchivedPath": "/KnowledgeBase/Archived", "DocumentType": "KnowledgeBase" },
    { "InboxPath": "/Conversation/Inbox", "ArchivedPath": "/Conversation/Archived", "DocumentType": "Conversation" }
  ]
}
```

### IOneDriveService Interface

Parameterize path-dependent methods:

```csharp
public interface IOneDriveService
{
    Task<List<OneDriveFile>> ListInboxFilesAsync(string inboxPath, CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);
    Task MoveToArchivedAsync(string fileId, string filename, string archivedPath, CancellationToken ct = default);
}
```

- `ListInboxFilesAsync` — accepts `inboxPath` instead of reading from `_options`
- `MoveToArchivedAsync` — accepts `archivedPath` instead of reading from `_options`
- `DownloadFileAsync` — unchanged (uses file ID, path-independent)

### GraphOneDriveService

Use the new path parameters in Graph API URL construction instead of `_options.OneDriveInboxPath` / `_options.OneDriveArchivedPath`. No other logic changes.

### MockOneDriveService

Update method signatures to match. Continues returning empty results.

### IndexDocumentRequest

Add `DocumentType` property:

```csharp
public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
```

### IndexDocumentHandler

Set `document.DocumentType = request.DocumentType` when creating the `KnowledgeBaseDocument` entity. Currently this defaults to `DocumentType.KnowledgeBase` via the entity default — now it's explicitly set from the request.

### KnowledgeBaseIngestionJob

Replace single-folder polling with iteration over `OneDriveFolderMappings`:

```
for each mapping in options.OneDriveFolderMappings:
    files = ListInboxFilesAsync(mapping.InboxPath)
    for each file:
        // existing SHA-256 dedup logic — unchanged
        IndexDocumentRequest { ..., DocumentType = mapping.DocumentType }
        MoveToArchivedAsync(fileId, filename, mapping.ArchivedPath)
```

Change `DefaultIsEnabled` from `false` to `true`.

### What Stays the Same

- All text extraction (PDF, DOCX, TXT/MD)
- `DocumentIndexingService` — already routes by `document.DocumentType` to the correct strategy
- `KnowledgeBaseDocIndexingStrategy` and `ConversationIndexingStrategy` — untouched
- Chunking, summarization, embedding logic
- Search, Q&A, feedback, MCP tools
- Frontend UI
- Database schema — no migration needed (`DocumentType` column already exists)
- Manual upload path — already supports `DocumentType` selection

## Files to Modify

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` | Add `OneDriveFolderMapping` class, `OneDriveFolderMappings` list; remove `OneDriveInboxPath`, `OneDriveArchivedPath` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs` | Add `inboxPath` param to `ListInboxFilesAsync`, `archivedPath` param to `MoveToArchivedAsync` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs` | Use path params instead of `_options` fields |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/MockOneDriveService.cs` | Update method signatures |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentRequest.cs` | Add `DocumentType` property |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` | Set `document.DocumentType` from request |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs` | Iterate folder mappings; pass type and archived path; set `DefaultIsEnabled = true` |
| `backend/src/Anela.Heblo.API/appsettings.json` | Replace `OneDriveInboxPath`/`OneDriveArchivedPath` with `OneDriveFolderMappings` array |

## Verification

1. **Build**: `dotnet build` passes
2. **Unit tests**: Existing KB tests pass; add test for ingestion job iterating multiple mappings
3. **Config validation**: Verify `OneDriveFolderMappings` binds correctly from `appsettings.json`
4. **Manual test**: Drop a file into each OneDrive folder, verify correct `DocumentType` is set on the indexed document and file moves to the correct archived folder
