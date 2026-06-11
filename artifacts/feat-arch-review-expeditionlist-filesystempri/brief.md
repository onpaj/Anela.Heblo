## Module
ExpeditionList

## Finding
`FileSystemPrintQueueSink` in `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs` (lines 7–45) is a concrete infrastructure implementation — it calls `Directory.CreateDirectory`, `File.Copy`, and `Path.Combine` — but lives in the Application layer.

The other two `IPrintQueueSink` implementations are placed correctly in their respective adapter projects:
- `AzureBlobPrintQueueSink` → `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`
- `CupsPrintQueueSink` → `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`

`FileSystemPrintQueueSink` is the exception: direct filesystem I/O living one layer too high.

## Why it matters
Clean Architecture places infrastructure concerns (filesystem, database, external services) in the outer rings (Adapters/Infrastructure), not in Application. The Application layer should reference only the `IPrintQueueSink` interface (`Application/Shared/Printing/`), not provide concrete implementations that touch the filesystem. The inconsistency with the other two sinks also means any future developer looking at the Application layer will be surprised by I/O operations there.

## Suggested fix
Move `FileSystemPrintQueueSink` to an adapter project following the pattern of the other sinks. The simplest option is a new `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs`, or add it to an existing adapter that doesn't require a third-party dependency (the class is self-contained). The `IPrintQueueSink` interface and `PrintPickingListOptions` remain in Application; only the concrete file-copying class moves.

---
_Filed by daily arch-review routine on 2026-06-07._