## Module
ExpeditionListArchive

## Finding
`ReprintExpeditionListHandler` (lines 28–54 of `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`) directly uses `Path.GetTempPath()`, `File.OpenWrite()`, and `File.Delete()` to buffer a blob download to disk before handing the file path to `IPrintQueueSink.SendAsync`.

The handler is in the Application layer but performs concrete filesystem I/O — creating, writing, and deleting a temp file — with no abstraction between it and `System.IO`.

## Why it matters
`docs/architecture/filesystem.md` states: _"Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`."_

The spirit of that rule applies here: the temp-file management is I/O-bound orchestration that does not belong in an Application-layer MediatR handler. Two concrete costs:

1. **Untestability**: Unit-testing `ReprintExpeditionListHandler` in isolation is impossible without hitting the real filesystem. Mocking `IBlobStorageService` and `IPrintQueueSink` is straightforward; the inlined `File.OpenWrite` / `File.Delete` calls are not.
2. **Misplaced responsibility**: The handler is simultaneously an orchestrator and a temp-file lifecycle manager, violating SRP. If `IPrintQueueSink` is ever changed to accept a stream directly, the I/O logic will have to be hunted down inside the handler rather than in an adapter.

## Suggested fix
Change `IPrintQueueSink.SendAsync` to accept a `Stream` instead of `string[]` file paths:

```csharp
Task SendAsync(Stream content, CancellationToken cancellationToken);
```

Move the temp-file creation into the CUPS `IPrintQueueSink` adapter (which already lives in the Adapters/infrastructure layer and is allowed to do I/O). `ReprintExpeditionListHandler` then becomes:

```csharp
await using var blobStream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
await _cupsSink.SendAsync(blobStream, cancellationToken);
return new ReprintExpeditionListResponse { Success = true };
```

All filesystem I/O stays in the adapter; the handler is clean and fully unit-testable.

---
_Filed by daily arch-review routine on 2026-07-03._
