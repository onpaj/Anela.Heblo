# Design: Remove direct filesystem I/O from ExpeditionListService

## Component Design

### `ITemporaryFileAccessor` (new, Application layer)
- **Location:** `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs`
- **Responsibility:** Consumer-owned abstraction over temp-file read/delete operations needed by `ExpeditionListService`. Mirrors the existing intra-module split already used for `IPrintQueueSink` (Application defines the contract, `Adapters.FileSystem` implements it) — not the cross-module "consumer owns the contract" pattern, since both sides live inside `Features/ExpeditionList`.
- **Contract:**
  ```csharp
  namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

  public interface ITemporaryFileAccessor
  {
      Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
      void DeleteIfExists(string path);
  }
  ```
- **Design notes:**
  - No `System.IO` types (`File`, `Path`, `Directory`, `Stream`) appear in the signature — only `string`/`byte[]`/`CancellationToken`.
  - `DeleteIfExists` is a single idempotent method rather than separate `Exists`/`Delete` members, avoiding a check-then-act race and keeping the surface minimal for mocking (`Verify(x => x.DeleteIfExists(path), Times.Once)`).
  - `Path.GetFileName(a)` in `SendEmailCopy` remains inline in `ExpeditionListService` — it's a pure string operation, not filesystem I/O, and stays out of the abstraction.

### `FileSystemTemporaryFileAccessor` (new, Adapter layer)
- **Location:** `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs`
- **Responsibility:** `System.IO.File`-backed implementation of `ITemporaryFileAccessor`. Sits alongside the existing `FileSystemPrintQueueSink` in the same feature folder.
- **Behavior:**
  - `ReadAllBytesAsync` passes through to `File.ReadAllBytesAsync(path, cancellationToken)` — same exceptions propagate on failure (`FileNotFoundException`, `IOException`) as today; no new error handling added.
  - `DeleteIfExists` guards with `File.Exists` before calling `File.Delete`, preserving current no-op-if-absent semantics.
  - Stateless; no constructor dependencies.

### `ExpeditionListService` (modified, Application layer)
- **Location:** `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs`
- **Change:** Gains a new required constructor dependency, `ITemporaryFileAccessor temporaryFileAccessor`, inserted after `IPrintQueueSink printQueueSink` and before `ILogger` (preserving the existing dependencies-first/logger-last convention):
  ```csharp
  public ExpeditionListService(
      IExpeditionPickingSource pickingSource,
      IEmailSender emailSender,
      TimeProvider clock,
      IOptions<PrintPickingListOptions> options,
      IPrintQueueSink printQueueSink,
      ITemporaryFileAccessor temporaryFileAccessor,
      ILogger<ExpeditionListService> logger)
  ```
- `Cleanup(ExpeditionPickingResult result)` and `SendEmailCopy(IList<string> files, IEnumerable<string> emailRecipients)` delegate to `_temporaryFileAccessor.DeleteIfExists(...)` / `_temporaryFileAccessor.ReadAllBytesAsync(...)` instead of calling `System.IO.File` directly. No other logic in either method changes.
- Public contract `IExpeditionListService.PrintPickingListAsync` is unaffected — this is purely an internal implementation detail.
- Minor accepted behavior change: `SendEmailCopy` now forwards `CancellationToken` into `ReadAllBytesAsync` (current code does not pass a token to `File.ReadAllBytesAsync`). This only makes cancellation take effect earlier/more precisely and is not a functional regression.

### `FileSystemAdapterServiceCollectionExtensions` (modified, Adapter layer)
- **Location:** `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs`
- **Change:** New method `AddFileSystemTemporaryFileAccessor(this IServiceCollection services)` registering `services.AddScoped<ITemporaryFileAccessor, FileSystemTemporaryFileAccessor>();` — `Scoped` lifetime to match `IPrintQueueSink`'s existing registration, for consistency (the type is stateless, but no functional benefit to diverging).

### Composition root wiring (modified)
- **Location:** `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, inside `AddPrintQueueSink`.
- **Change:** Call `services.AddFileSystemTemporaryFileAccessor();` **once, unconditionally, before** the `switch (printSink)` block — not inside the `default:` case. Temp-file read/delete is required regardless of which `PrintSink` (`FileSystem` / `AzureBlob` / `Cups` / `Combined`) is active, since exported PDFs always land on local disk first via `IExpeditionPickingSource.CreatePickingListAsync`. Placing the call inside `default:` would leave `IExpeditionListService` unresolvable under non-default `PrintSink` configs — this is the corrected anchor point per the architecture review.

### Test updates
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs` and `ExpeditionListServiceOrderStateTests.cs`: add a `Mock<ITemporaryFileAccessor>` field to each `CreateService()` helper, pass `.Object` into the constructor. Replace any real-filesystem setup (e.g. `Path.GetTempFileName()` + `Assert.False(File.Exists(tmpFile))` in the cleanup test) with mock setup/verification (`DeleteIfExists` invoked once per `ExportedFiles` entry; `ReadAllBytesAsync` stubbed to return known bytes for attachment assertions).
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemTemporaryFileAccessorTests.cs` (new): exercises the real filesystem in isolation — `DeleteIfExists` on a non-existent path is a no-op; `ReadAllBytesAsync` returns actual file bytes. This is the one place real I/O in tests is appropriate, since it is now confined to the adapter.

## Data Schemas

No persistent data model, database schema, or HTTP/REST/MediatR contract changes. This is an internal DI/interface refactor. Summary of type-level changes:

| Type | Layer | Change |
|---|---|---|
| `ITemporaryFileAccessor` | `Anela.Heblo.Application` (`Features/ExpeditionList/Contracts/`) | New interface: `Task<byte[]> ReadAllBytesAsync(string path, CancellationToken)`, `void DeleteIfExists(string path)` |
| `FileSystemTemporaryFileAccessor` | `Anela.Heblo.Adapters.FileSystem` (`Features/ExpeditionList/`) | New class implementing the above via `System.IO.File` |
| `ExpeditionListService` | `Anela.Heblo.Application` (`Features/ExpeditionList/Services/`) | Modified: new constructor parameter `ITemporaryFileAccessor temporaryFileAccessor`; `Cleanup`/`SendEmailCopy` bodies delegate to it instead of `File.*` |
| `FileSystemAdapterServiceCollectionExtensions` | `Anela.Heblo.Adapters.FileSystem` | New method `AddFileSystemTemporaryFileAccessor()` registering `ITemporaryFileAccessor` as `Scoped` |
| `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | `Anela.Heblo.API` | Modified: `AddPrintQueueSink` calls `AddFileSystemTemporaryFileAccessor()` unconditionally, before the `PrintSink` switch |

`ExpeditionPickingResult.ExportedFiles` (`IList<string>`) is unchanged — file paths remain the transport between `IExpeditionPickingSource` and `ExpeditionListService`; no move to `byte[]`/`Stream` in this change (deferred, see spec Out of Scope).

No HTTP endpoints, MediatR request/response DTOs, or event payloads are introduced or modified.
