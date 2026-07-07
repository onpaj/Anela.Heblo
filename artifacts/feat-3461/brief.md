## Module
ExpeditionList

## Finding
`ExpeditionListService` (Application layer) directly calls filesystem APIs in two private methods:

- **`Cleanup()`** (`Services/ExpeditionListService.cs` lines 70–78): calls `File.Exists(f)` and `File.Delete(f)` to remove temporary PDF files after dispatch.
- **`SendEmailCopy()`** (`Services/ExpeditionListService.cs` lines 95–104): calls `File.ReadAllBytesAsync(a)` to read each exported PDF before attaching it to the outgoing email.

The file paths come from `ExpeditionPickingResult.ExportedFiles`, which is populated by the `IExpeditionPickingSource` adapter. The Application layer thus knows about and manipulates filesystem paths, which is an infrastructure concern.

`docs/architecture/filesystem.md` states:
> **I/O placement rule**: Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`.

All other I/O-bound services in the printing pipeline (`AzureBlobPrintQueueSink`, `CupsPrintQueueSink`, `FileSystemPrintQueueSink`) correctly live under `backend/src/Adapters/`. `ExpeditionListService` breaks this pattern.

## Why it matters
- The Application layer is supposed to be infrastructure-agnostic. Direct `File.*` calls make the service untestable without a real filesystem and couple it to the host OS's file system.
- `ExpeditionListServicePrintSinkTests` and `ExpeditionListServiceOrderStateTests` (in `backend/test/`) have to work around this — any test that exercises `PrintPickingListAsync` with email or cleanup paths needs real temp files rather than mocks.
- Any future migration to a non-filesystem picking source (e.g. in-memory byte streams) requires changing both the picking source adapter AND the Application-layer service.

## Suggested fix
Abstract temporary-file lifecycle behind a thin interface, e.g.:

```csharp
// In Application/Shared/Printing/ or Application/Features/ExpeditionList/Contracts/
public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default);
    void Delete(string path);
}
```

Register a `System.IO`-backed implementation in `Adapters/` (or in the `FileSystem` adapter). Inject it into `ExpeditionListService` instead of calling `File.*` directly. The `Cleanup` and `SendEmailCopy` methods then delegate to this abstraction and become unit-testable with a mock.

If the picking source can be changed to return `byte[]`/`Stream` instead of file paths, that is an even cleaner solution — it removes the need for the accessor entirely and stops filesystem paths from leaking into `ExpeditionPickingResult.ExportedFiles`.

---
_Filed by daily arch-review routine on 2026-07-02._
