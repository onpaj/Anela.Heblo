# Specification: Remove direct filesystem I/O from ExpeditionListService (Application layer)

## Summary
`ExpeditionListService` in `Anela.Heblo.Application.Features.ExpeditionList.Services` currently calls `File.Exists`, `File.Delete`, and `File.ReadAllBytesAsync` directly, violating the repo's documented I/O placement rule that concrete I/O-bound work must live in `backend/src/Adapters/`. This spec defines a new `ITemporaryFileAccessor` abstraction — owned by the ExpeditionList module's contracts and implemented by the existing FileSystem adapter project — so the Application-layer service becomes filesystem-agnostic and fully unit-testable with mocks, matching the pattern already used for `IPrintQueueSink`.

## Background
`docs/architecture/filesystem.md` states the I/O placement rule explicitly:
> Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`.

The printing pipeline already follows this correctly: `FileSystemPrintQueueSink`, `AzureBlobPrintQueueSink`, and `CupsPrintQueueSink` all live under `backend/src/Adapters/*` and implement the Application-owned `IPrintQueueSink` contract (`Anela.Heblo.Application.Shared.Printing.IPrintQueueSink`). `ExpeditionListService`, however, bypasses this pattern in two private methods:

- `Cleanup(ExpeditionPickingResult result)` (lines 70–79): iterates `result.ExportedFiles` and calls `File.Exists(f)` / `File.Delete(f)` directly to remove temporary PDFs after a batch is dispatched.
- `SendEmailCopy(IList<string> files, IEnumerable<string> emailRecipients)` (lines 81–109): calls `File.ReadAllBytesAsync(a)` directly to read each exported PDF before base64-encoding it into an `EmailAttachment`.

Both file lists originate as `IList<string>` paths supplied by `IExpeditionPickingSource.CreatePickingListAsync` (via `ExpeditionPickingResult.ExportedFiles` and the `onBatchFilesReady` callback), which today is implemented by `LogisticsExpeditionPickingAdapter` delegating to `IPickingListSource` (bound to `ShoptetApiExpeditionListSource`). The Application layer therefore both receives filesystem paths from an adapter *and* performs raw filesystem I/O on them itself — the second half of that is the violation this spec fixes.

Consequences of the current state, per the architecture-review finding:
- The service cannot be unit tested without a real filesystem for any path that exercises email or cleanup behavior; `ExpeditionListServicePrintSinkTests` and `ExpeditionListServiceOrderStateTests` must currently work around this using real temp files rather than pure mocks.
- The service is coupled to the host OS's filesystem semantics, which blocks any future migration of the picking source to return in-memory byte streams instead of file paths without also touching the Application-layer service.

This is a **pure refactor**: no user-facing behavior, business logic, or output should change. `PrintPickingListAsync`'s external contract (`IExpeditionListService`), the generated PDFs, the email content, and print-queue behavior must all remain identical.

## Functional Requirements

### FR-1: Introduce `ITemporaryFileAccessor` contract in the Application layer
Define a new interface that captures exactly the temporary-file operations `ExpeditionListService` needs — read bytes, and delete-if-exists — with no leakage of `System.IO` types beyond primitive `string`/`byte[]`.

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    void DeleteIfExists(string path);
}
```

Placement: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs`, following the existing consumer-owns-the-contract pattern used for `IExpeditionPickingSource` (see `docs/architecture/development_guidelines.md`, "Cross-Module Communication" pattern, applied here intra-module for I/O rather than cross-module data access).

**Acceptance criteria:**
- Interface has exactly two members: an async byte-read and a delete-if-exists operation (naming may be adjusted during implementation review, but the shape — no `File.Exists` exposed separately, no `Stream` leakage — must match).
- Interface lives under `Application/Features/ExpeditionList/Contracts/`, not `Services/`.
- No `System.IO` (`File`, `Path`, `Directory`) types appear in the interface signature.

### FR-2: Implement `ITemporaryFileAccessor` in the FileSystem adapter project
Add a concrete implementation backed by `System.IO.File` in `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/`, alongside the existing `FileSystemPrintQueueSink` for the same feature.

```csharp
namespace Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;

public class FileSystemTemporaryFileAccessor : ITemporaryFileAccessor
{
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(path, cancellationToken);

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
```

**Acceptance criteria:**
- Class lives in `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs`.
- Behavior is byte-identical to the current inline code: `ReadAllBytesAsync` passes through `CancellationToken`; `DeleteIfExists` is a no-op when the file is already absent (matches current `File.Exists` guard, avoiding a `FileNotFoundException` from a bare `File.Delete`).
- No behavior change: same exceptions propagate on read failure (e.g., `FileNotFoundException`, `IOException`) as today, since callers currently do not catch around `File.ReadAllBytesAsync`.

### FR-3: Register the new implementation in DI
Extend `FileSystemAdapterServiceCollectionExtensions` (or add a sibling extension method) so the composition root (`Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`) can register `ITemporaryFileAccessor` the same way it registers `IPrintQueueSink` today via `AddFileSystemPrintQueueSink()`.

**Acceptance criteria:**
- A new method (e.g. `AddFileSystemTemporaryFileAccessor()`) or an addition to the existing registration method registers `services.AddScoped<ITemporaryFileAccessor, FileSystemTemporaryFileAccessor>();` (lifetime should match `IPrintQueueSink`'s existing `Scoped` registration for consistency, unless implementation review determines `Singleton` is safe/preferable — the type is stateless).
- `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` calls this registration unconditionally alongside (or as part of) the existing `services.AddFileSystemPrintQueueSink();` call at line ~436, since — unlike the print sink, which has environment-specific alternatives (Azure Blob, CUPS) — temporary-file read/delete is not swapped per environment today.
- Application starts successfully and `IExpeditionListService` resolves with no DI errors in all existing environments (local, staging).

### FR-4: Refactor `ExpeditionListService` to use `ITemporaryFileAccessor` instead of direct `File.*` calls
Inject `ITemporaryFileAccessor` into `ExpeditionListService`'s constructor. Replace the bodies of `Cleanup` and `SendEmailCopy` to delegate to the injected accessor instead of calling `System.IO.File` directly. Remove the now-unused `File.Exists`/`File.Delete`/`File.ReadAllBytesAsync` calls entirely from this file.

`Cleanup` becomes:
```csharp
private Task Cleanup(ExpeditionPickingResult result)
{
    foreach (var f in result.ExportedFiles)
    {
        _temporaryFileAccessor.DeleteIfExists(f);
    }

    return Task.CompletedTask;
}
```

`SendEmailCopy`'s attachment loop becomes:
```csharp
foreach (var a in files)
{
    var bytes = await _temporaryFileAccessor.ReadAllBytesAsync(a, cancellationToken);
    message.Attachments.Add(new EmailAttachment
    {
        FileName = Path.GetFileName(a),
        Content = Convert.ToBase64String(bytes),
        ContentType = "application/pdf"
    });
}
```

**Acceptance criteria:**
- `ExpeditionListService.cs` no longer contains any `File.*` calls (verified by absence of `using System.IO;`-only members like `File.Exists`, `File.Delete`, `File.ReadAllBytesAsync` in the file, and by a grep for `System.IO.File` returning nothing in this file).
- `Path.GetFileName(a)` (a pure string operation, not I/O) may remain inline — it is not filesystem access and is out of scope for the abstraction (confirm during implementation; if reviewers prefer full purity, this can also move behind the accessor, see Open Questions).
- `SendEmailCopy` passes `CancellationToken` through to `ReadAllBytesAsync` (note: current code does **not** pass the token to `File.ReadAllBytesAsync` — this is a minor behavior addition; confirm acceptable, see Open Questions).
- Constructor signature adds `ITemporaryFileAccessor temporaryFileAccessor` as a new required parameter; existing parameters and their order are otherwise preserved as closely as practical (exact position is an implementation detail, not user-visible).
- `IExpeditionListService` public interface is unchanged — this is an internal implementation detail of `ExpeditionListService`.

### FR-5: Update or add unit tests to use a mocked `ITemporaryFileAccessor`
Update `ExpeditionListServicePrintSinkTests` and `ExpeditionListServiceOrderStateTests` (both in `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/`) to construct `ExpeditionListService` with a `Mock<ITemporaryFileAccessor>` instead of relying on real temporary files on disk for any test path that exercises `Cleanup` or `SendEmailCopy`.

**Acceptance criteria:**
- Every existing test in both files continues to pass after the refactor, with the same assertions.
- Any test that previously created real temp files specifically to satisfy `File.Exists`/`File.ReadAllBytesAsync` inside `ExpeditionListService` is rewritten to instead set up `Mock<ITemporaryFileAccessor>` return values (e.g., `_temporaryFileAccessor.Setup(x => x.ReadAllBytesAsync(path, It.IsAny<CancellationToken>())).ReturnsAsync(someBytes)`), and no longer touches the real filesystem for that purpose.
- At least one test asserts `DeleteIfExists` is invoked once per file in `ExpeditionPickingResult.ExportedFiles` after `PrintPickingListAsync` completes (covering `Cleanup`).
- At least one test asserts email attachments are built from bytes returned by the mocked `ReadAllBytesAsync`, without depending on real file content (covering `SendEmailCopy`).
- A new unit test exists for `FileSystemTemporaryFileAccessor` itself (in the adapter's own test project, if one exists, or a new lightweight test) verifying `DeleteIfExists` on a non-existent path is a no-op and `ReadAllBytesAsync` returns the file's actual bytes — this is the one place real filesystem interaction is appropriate, since it is now isolated to the adapter.

### FR-6: Preserve architectural boundary going forward
No new test is strictly required beyond FR-5, but implementers should check whether `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (or a sibling architecture test) already has a reflection-based check for I/O calls in `Features/*/Services/`. If such a check does not exist, adding one is **optional** for this change (see Out of Scope) but recommended as a follow-up to prevent regression.

**Acceptance criteria:**
- Investigation is done and documented in the PR description (found existing check / no check exists), even if a new automated guard is not added in this change.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression versus current behavior. File read/delete operations are the same OS-level calls, just routed through one extra interface indirection (negligible overhead, sub-microsecond per call). No additional I/O round-trips are introduced. `AddScoped` DI resolution cost is negligible relative to the PDF generation and printing work already being done in this code path.

### NFR-2: Security
No change to the security posture. The accessor operates only on paths already produced internally by `IExpeditionPickingSource` (not user-supplied input), so no new path-traversal or injection surface is introduced. No new secrets, credentials, or external endpoints are involved.

### NFR-3: Testability
This is the primary driver of the change. After this refactor, `ExpeditionListService` unit tests must be constructible entirely with mocks (`Mock<IExpeditionPickingSource>`, `Mock<IEmailSender>`, `Mock<IPrintQueueSink>`, `Mock<ITemporaryFileAccessor>`) with zero dependency on the real filesystem, temp directories, or `File.*` static calls in test setup/teardown.

### NFR-4: Backward compatibility
`IExpeditionListService.PrintPickingListAsync` signature and behavior are unchanged from the caller's perspective (`PrintExpeditionOrderHandler`, `PrintPickingListJob`, `RunExpeditionListPrintFixHandler` if applicable). No API contract, request/response DTO, or MediatR handler changes are required.

## Data Model
No persistent data model changes. This is a pure DI/interface refactor affecting only the following types:

| Type | Layer | Change |
|---|---|---|
| `ITemporaryFileAccessor` | `Anela.Heblo.Application` (`Features/ExpeditionList/Contracts/`) | New interface |
| `FileSystemTemporaryFileAccessor` | `Anela.Heblo.Adapters.FileSystem` (`Features/ExpeditionList/`) | New class |
| `ExpeditionListService` | `Anela.Heblo.Application` (`Features/ExpeditionList/Services/`) | Modified: new constructor dependency, `Cleanup`/`SendEmailCopy` bodies delegate to accessor instead of `File.*` |
| `FileSystemAdapterServiceCollectionExtensions` | `Anela.Heblo.Adapters.FileSystem` | Modified/extended: new DI registration method |
| `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | `Anela.Heblo.API` | Modified: calls new registration method |

`ExpeditionPickingResult.ExportedFiles` (`IList<string>`) is unchanged — this spec does not attempt the "even cleaner" alternative of eliminating file paths from the contract entirely (see Out of Scope).

## API / Interface Design

```csharp
// Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs
public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    void DeleteIfExists(string path);
}
```

```csharp
// Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs
public class FileSystemTemporaryFileAccessor : ITemporaryFileAccessor { /* System.IO-backed */ }
```

```csharp
// Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs
public static IServiceCollection AddFileSystemTemporaryFileAccessor(this IServiceCollection services);
// or fold into the existing AddFileSystemPrintQueueSink(), renamed/expanded if scope allows
```

No HTTP/REST endpoints, MediatR requests, or frontend surfaces are affected by this change — it is entirely internal to the backend Application/Adapter layers.

## Dependencies
- Existing `Anela.Heblo.Adapters.FileSystem` project (already referenced by `Anela.Heblo.API` for `AddFileSystemPrintQueueSink()`; no new project reference needed).
- Existing `Anela.Heblo.Application` → `Anela.Heblo.Adapters.FileSystem` DI wiring pattern (composition happens in `Anela.Heblo.API`, consistent with Clean Architecture — Application never references Adapters directly).
- No third-party library additions.
- No changes to `IExpeditionPickingSource`, `LogisticsExpeditionPickingAdapter`, or the Shoptet/Logistics picking pipeline are required for this change.

## Out of Scope
- Changing `IExpeditionPickingSource`/`ExpeditionPickingResult` to return `byte[]`/`Stream` instead of file paths (the brief's "even cleaner" alternative). This is a larger, cross-cutting change affecting the picking source adapter chain (`LogisticsExpeditionPickingAdapter`, `IPickingListSource`, `ShoptetApiExpeditionListSource`) and print-queue sinks (`IPrintQueueSink.SendAsync` also takes file paths). Worth a separate architecture discussion, not bundled into this fix.
- Adding a new automated architecture test (reflection-based boundary check) to prevent future regressions of this kind — investigation is required (FR-6) but adding the guard itself is optional for this change.
- Any change to `IPrintQueueSink` or its existing implementations (`FileSystemPrintQueueSink`, `AzureBlobPrintQueueSink`, `CupsPrintQueueSink`, `CombinedPrintQueueSink`) — these already comply with the I/O placement rule and are untouched.
- Any change to email content, PDF generation, print-queue behavior, or the `PrintExpeditionOrderHandler`/`PrintPickingListJob`/`RunExpeditionListPrintFixHandler` call sites.
- Retry/resilience behavior for file read or delete failures — current behavior (exceptions propagate uncaught) is preserved as-is.

## Open Questions
- FR-4 proposes passing `CancellationToken` through to `ReadAllBytesAsync` in `SendEmailCopy`, which the current code does not do (`File.ReadAllBytesAsync(a)` with no token). This is a minor behavior change (cancellation now takes effect during attachment reads, not just before). Confirm this is acceptable, or should the accessor's `ReadAllBytesAsync` be called without forwarding the token to preserve exact current behavior?
- Should `DeleteIfExists` naming/shape match exactly what the brief proposed (`Delete(string path)` with an internal `Exists` check hidden, vs. separate `Exists`/`Delete` members as two calls)? This spec assumes a single `DeleteIfExists` method to keep the interface minimal and avoid exposing a race-prone `Exists`-then-`Delete` pattern to callers — confirm this is preferred over a literal `File.Exists`/`File.Delete` mirror.
- Should the DI registration for `ITemporaryFileAccessor` be folded into the existing `AddFileSystemPrintQueueSink()` method (renaming it to something like `AddFileSystemAdapter()`), or added as a clearly separate `AddFileSystemTemporaryFileAccessor()` call? Both achieve the same runtime result; this is a naming/organization preference for the implementer to confirm, given `AddFileSystemPrintQueueSink` is currently named specifically for the sink.
- Should `Path.GetFileName(a)` in `SendEmailCopy` (a pure string operation, no filesystem access) also move behind the accessor for full purity, or is it acceptable to leave inline since it does not touch the filesystem? This spec assumes it stays inline (it is not I/O).

## Status: HAS_QUESTIONS
