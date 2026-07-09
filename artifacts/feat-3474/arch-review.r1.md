# Architecture Review: Extract temp-file I/O out of ReprintExpeditionListHandler

## Skip Design: true

## Architectural Fit Assessment
The spec is architecturally sound and correctly scoped. I verified every file it names against the actual codebase state:

- `ReprintExpeditionListHandler.cs` (lines 8–56) does indeed inline `Path.GetTempPath()`, `File.OpenWrite()`, and `File.Delete()` in an Application-layer MediatR handler — exactly as the brief and spec describe.
- `ITemporaryFileAccessor` (`Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs`) currently exposes only `ReadAllBytesAsync` and `DeleteIfExists`; `FileSystemTemporaryFileAccessor` (`Adapters.FileSystem/Features/ExpeditionList/`) is its sole implementation and is already the pattern used by `ExpeditionListService` for the same class of concern (temp-file lifecycle management for print artifacts).
- `services.AddFileSystemTemporaryFileAccessor()` is registered unconditionally inside `AddPrintQueueSink` (`Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:415`), called from `Program.cs:133` — independent of `AddExpeditionListArchiveModule` (called from `ApplicationModule.cs:104`). Because the handler is resolved lazily via a transient factory delegate, the two registrations' call order in composition does not matter; `ITemporaryFileAccessor` will be resolvable by the time the handler factory runs.
- `ExpeditionListArchiveModule.AddExpeditionListArchiveModule` manually constructs `ReprintExpeditionListHandler` via `services.AddTransient<IRequestHandler<...>>(provider => ...)` specifically to select the keyed `"cups"` sink — confirming FR-4's claim that this factory (not MediatR auto-registration) is the single wiring point that needs updating.
- An adapter-level test file, `FileSystemTemporaryFileAccessorTests.cs`, already exists with exactly the "real filesystem I/O is acceptable here" pattern FR-5 asks new tests to follow (per-test temp dir, `IDisposable` cleanup, direct `File.*` assertions) — so FR-5 isn't inventing a new test convention, it's extending an established one.
- The rejected brief alternative (`IPrintQueueSink.SendAsync(Stream, ...)`) would in fact ripple into `ExpeditionListService`'s batch print flow, `CombinedPrintQueueSink`'s dual-sink fan-out, and `AzureBlobPrintQueueSink`'s filename-derived blob naming — the spec's blast-radius argument for rejecting it holds up against the actual call graph, not just the spec's prose.

This is a textbook case of the `docs/architecture/filesystem.md` I/O placement rule ("Concrete... any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`") being violated by a handler, and the fix is the minimal-diff way to restore compliance: extend an existing, already-adjacent abstraction rather than invent a new one or touch a widely shared contract. No architectural objection.

## Proposed Architecture
### Component Overview
No new components. Three existing types change:
- `ITemporaryFileAccessor` (Application/Contracts) — new method `CreateFromStreamAsync`.
- `FileSystemTemporaryFileAccessor` (Adapters.FileSystem) — implements it.
- `ReprintExpeditionListHandler` (Application/UseCases) — consumes it instead of raw `System.IO`.
- `ExpeditionListArchiveModule` — DI factory updated to resolve and inject the accessor.

### Key Design Decisions

#### Decision 1: Extend `ITemporaryFileAccessor` vs. introduce a new interface
**Options considered:** (a) extend `ITemporaryFileAccessor`; (b) introduce a reprint-scoped interface (e.g. `ITempFileStager`); (c) the brief's `IPrintQueueSink(Stream)` signature change.
**Chosen approach:** (a) — extend the existing interface with `CreateFromStreamAsync(Stream, string fileExtension, CancellationToken)`.
**Rationale:** `ITemporaryFileAccessor` already exists for exactly this purpose, already lives in the correct layer split (Application contract / Adapters.FileSystem implementation), and is already injected into a sibling use case (`ExpeditionListService`). A second interface would duplicate it with zero benefit. Option (c) is rejected on blast-radius grounds verified above.

#### Decision 2: Where the "delete partial file on failure" responsibility lives
**Options considered:** handler catches and deletes on any failure (status quo); accessor guarantees no partial file is left behind and the handler only cleans up a path it successfully received.
**Chosen approach:** the accessor owns cleanup of its own partial write (`catch { DeleteIfExists(path); throw; }` inside `CreateFromStreamAsync`); the handler's `finally` only fires `DeleteIfExists` on a `tempFile` that is non-null, i.e. only after `CreateFromStreamAsync` returned successfully.
**Rationale:** This is a strict behavioral improvement, not just a refactor-for-refactor's-sake move: today, if `File.OpenWrite` or the copy throws, `DeleteTempFile(tempFile)` still races against a path that may or may not have been created — harmless today only because `File.Delete` on a non-existent path is a silent no-op. Moving that responsibility into the adapter is more correct (the component that knows the write's true state owns its own cleanup) and matches the "callers only need to clean up the path once it has been successfully returned" contract stated in FR-1. This also removes the double-delete-attempt shape entirely (no `finally` at the handler racing against a `catch` in the same code region).

#### Decision 3: Keep `IPrintQueueSink.SendAsync(IEnumerable<string>)` unchanged
**Options considered:** brief's `Stream`-based signature; status quo path-array signature.
**Chosen approach:** status quo, untouched.
**Rationale:** Verified against `ExpeditionListService` (multi-file batch), `CombinedPrintQueueSink` (dual-sink fan-out over the same batch), and `AzureBlobPrintQueueSink` (derives archival blob name from `Path.GetFileName`). None of these are implicated in the finding; changing the shared contract for a single-file caller would be scope creep with real regression risk in the batch/email pipeline. Correctly deferred to a separate arch-review item if ever pursued.

## Implementation Guidance
### Directory / Module Structure
No new files or folders. Modified files only:
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` (rewritten)
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemTemporaryFileAccessorTests.cs` (extended with `CreateFromStreamAsync` cases, following its existing per-test-temp-dir pattern)

This is a pure horizontal change within one existing vertical slice pairing (`ExpeditionListArchive` consumer, `ExpeditionList` contract owner, `Adapters.FileSystem` implementer) — no new slice, no new module boundary.

### Interfaces and Contracts
```csharp
// Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs
public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default); // NEW
    void DeleteIfExists(string path);
}
```
This is an additive, non-breaking interface change. `ITemporaryFileAccessor` has exactly one implementation (`FileSystemTemporaryFileAccessor`) and one other consumer (`ExpeditionListService`), which does not need the new method and is unaffected.

### Data Flow
1. `ReprintExpeditionListHandler.Handle` validates `request.BlobPath` (unchanged, `BlobPathValidator.IsValid`).
2. Downloads blob stream via `IBlobStorageService.DownloadAsync` (unchanged call).
3. Delegates temp-file creation to `_temporaryFileAccessor.CreateFromStreamAsync(blobStream, ".pdf", cancellationToken)` — all `File`/`Path` calls now live inside the adapter.
4. Passes the returned path to `_cupsSink.SendAsync(new[] { tempFile }, cancellationToken)` — `IPrintQueueSink` contract and call shape unchanged.
5. `finally` cleans up via `_temporaryFileAccessor.DeleteIfExists(tempFile)`, guarded by `tempFile != null` so a failed `CreateFromStreamAsync` (which already cleaned up after itself) isn't double-deleted.

No change to the request/response DTOs, the MediatR contract, or any HTTP-visible behavior — confirmed purely internal.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `ExpeditionListArchiveModule`'s manual factory is easy to forget when adding a constructor parameter (it bypasses MediatR auto-DI, so a missed update is a runtime `NullReferenceException`/DI resolution failure, not a compile error against the interface) | Low | Spec's FR-4 explicitly calls this out; a resolution test across all four `PrintSink` modes (already required by FR-4's acceptance criteria) catches it before merge. |
| Deleting FR-5's real-filesystem leak-detection tests from `ReprintExpeditionListHandlerTests.cs` could look like a coverage regression at a glance | Low | Equivalent-and-better coverage moves to `FileSystemTemporaryFileAccessorTests.cs` (byte-for-byte content check, extension check, no-partial-file-on-throw check) plus mock-based verification in the handler test that `DeleteIfExists`/`CreateFromStreamAsync` are called with the right arguments in the right order. Net coverage is equal or stronger and now targets the right layer. |
| `CreateFromStreamAsync`'s `catch { DeleteIfExists(path); throw; }` swallows the delete's own failure only via `DeleteIfExists`'s pre-existing best-effort semantics (no explicit try/catch shown in the spec's sample) | Low | `DeleteIfExists` already wraps `File.Exists`/`File.Delete` without its own try/catch today (see current implementation) — this is pre-existing best-effort-by-convention, not a new gap introduced by this spec. Worth a one-line implementer note (see Specification Amendments) but not a blocker. |

## Specification Amendments
None required to proceed. One implementer note, not a spec defect: `DeleteIfExists`'s current implementation (`if (File.Exists(path)) File.Delete(path)`) is not itself wrapped in a try/catch — it's "best effort" only in the sense that a missing file is a no-op, not in the sense that it swallows a `File.Delete` `IOException` (e.g. file locked by another process). FR-2's "best-effort, matching the existing `DeleteIfExists` semantics" language should be read as "same level of effort as today," not "guaranteed to never throw." This matches current production behavior and is out of scope to change here.

## Prerequisites
None. All dependencies (`ITemporaryFileAccessor`, `FileSystemTemporaryFileAccessor`, the DI registration, the adapter test file/pattern) already exist on this branch's base. Implementation can proceed directly from `spec.r1.md`.
