# Specification: Extract temp-file I/O out of ReprintExpeditionListHandler

## Summary
`ReprintExpeditionListHandler` (Application layer, MediatR handler) currently calls `Path.GetTempPath()`, `File.OpenWrite()`, and `File.Delete()` directly to stage a downloaded blob on disk before handing its path to `IPrintQueueSink.SendAsync`. This refactor removes all direct `System.IO` calls from the handler by extending the existing `ITemporaryFileAccessor` abstraction (already used by the sibling `ExpeditionListService`) with a stream-to-temp-file creation method, whose concrete implementation lives in the `Anela.Heblo.Adapters.FileSystem` adapter project. The result is a handler that is fully unit-testable with mocks and contains no filesystem knowledge, with no change to its external behavior or to the shared `IPrintQueueSink` contract used elsewhere in the print pipeline.

## Background
`docs/architecture/filesystem.md` states that concrete `IPrintQueueSink` implementations and any I/O-bound service belong in adapter projects under `backend/src/Adapters/`, not in Application-layer `Features/{Feature}/...` code. `ReprintExpeditionListHandler` (`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`, lines 28–54) violates the spirit of that rule: it downloads a blob stream, writes it to a temp file via `File.OpenWrite`, invokes `IPrintQueueSink.SendAsync` with the file path, and deletes the temp file in a `finally`/`catch` — all inline in a MediatR handler.

This creates two concrete problems:
1. **Untestability**: the current unit test suite (`backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`) has to assert against `Path.GetTempPath()` and real `File.Exists`/`Directory.EnumerateFiles` calls to verify no temp files leak — real filesystem I/O in what should be a pure orchestration unit test.
2. **Misplaced responsibility (SRP)**: the handler is both a MediatR orchestrator and a temp-file lifecycle manager.

**Deviation from the brief's suggested fix, confirmed.** The brief proposes changing `IPrintQueueSink.SendAsync` to accept a `Stream` instead of `IEnumerable<string>` file paths, with the CUPS adapter owning temp-file creation. Investigation of the codebase shows `IPrintQueueSink` (`backend/src/Anela.Heblo.Application/Shared/Printing/IPrintQueueSink.cs`) is a much more broadly shared contract than the brief's framing suggests:
- `ExpeditionListService.PrintPickingListAsync` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs`) calls `SendAsync` with **batches of multiple file paths** per invocation (`Func<IList<string>, Task> batchCallback`), not a single stream.
- `CombinedPrintQueueSink` (`backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`) fans the **same batch of paths** out to both `AzureBlobPrintQueueSink` (archival upload) and `CupsPrintQueueSink` (physical print) in one call.
- `AzureBlobPrintQueueSink` derives the archival blob name from `Path.GetFileName(filePath)` for each file — a bare `Stream` loses that filename metadata.
- `FileSystemPrintQueueSink` (dev/test fallback) also processes a list of named files.
- `ICupsPrintingService.PrintAsync` (the CUPS SDK boundary, `Anela.Heblo.Adapters.Cups`) still takes a `string filePath`, not a stream — SharpIpp's `PrintJobRequest.Document` does accept a `Stream`, but `ICupsPrintingService`'s public contract is unrelated to `IPrintQueueSink` and out of scope here.

Changing `IPrintQueueSink.SendAsync`'s signature to a single `Stream` would therefore ripple into the batch/email print pipeline, the dual-sink archival+print fan-out, and blob-naming logic — none of which is implicated in the finding, and none of which is exercised by `ReprintExpeditionListHandler` (which only ever sends a single file). Per CLAUDE.md's "surgical changes" guidance, this spec instead targets the actual defect (raw `System.IO` calls inlined in an Application-layer handler) using the narrowest available fix: extending `ITemporaryFileAccessor`, an abstraction that already exists for exactly this purpose (temp-file lifecycle management) and is already injected into the sibling `ExpeditionListService`. `IPrintQueueSink` and all of its implementations remain untouched.

This deviation was reviewed and confirmed by the product owner: the brief's own "why it matters" section cites only untestability and misplaced responsibility in `ReprintExpeditionListHandler`, both of which are fully resolved by extending `ITemporaryFileAccessor`. The `Stream`-based `IPrintQueueSink` was a suggested mechanism in the brief, not the goal itself. If a stream-based `IPrintQueueSink` is later desired across the whole print pipeline, it is to be filed as its own arch-review finding with its own blast-radius review — it is not bundled into this fix.

**Abstraction choice, confirmed.** This spec extends the existing `ITemporaryFileAccessor` (Application interface under `Features/ExpeditionList/Contracts`, `Anela.Heblo.Adapters.FileSystem` implementation) rather than introducing a new, narrowly-scoped interface dedicated to the reprint flow (e.g. an `ExpeditionListArchive`-scoped `ITempFileStager`). `ITemporaryFileAccessor` is not archive-specific, is already injected into the sibling `ExpeditionListService`, and is already registered unconditionally in DI — a second interface with an identical purpose would be pure duplication with no testability or SRP benefit, contrary to CLAUDE.md's "surgical changes" and reuse-over-reinvention principles.

## Functional Requirements

### FR-1: Extend `ITemporaryFileAccessor` with stream-backed temp-file creation
Add a new method to `Anela.Heblo.Application.Features.ExpeditionList.Contracts.ITemporaryFileAccessor` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs`):

```csharp
Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default);
```

- `content` is copied to a new file in the OS temp directory; `fileExtension` (e.g. `".pdf"`, including the leading dot) is appended to a generated unique file name.
- Returns the absolute path of the created temp file.
- If writing fails partway through, the implementation is responsible for deleting any partially-written file before rethrowing — callers only need to clean up the path once it has been successfully returned.

**Acceptance criteria:**
- Interface compiles with the new method; existing `ReadAllBytesAsync` and `DeleteIfExists` members are unchanged.
- Method signature and XML doc (if any) follow the existing style of the interface file.

### FR-2: Implement `CreateFromStreamAsync` in `FileSystemTemporaryFileAccessor`
Implement the new method in `Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList.FileSystemTemporaryFileAccessor` (`backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs`), using `Path.GetTempPath()`, a `Guid`-based unique file name, and `File.Create`/`CopyToAsync`. On write failure, delete the partial file (best-effort, matching the existing `DeleteIfExists` semantics) before propagating the exception.

**Acceptance criteria:**
- Given a stream with known bytes, `CreateFromStreamAsync` returns a path under `Path.GetTempPath()` whose contents match the input stream byte-for-byte.
- The returned path ends with the supplied `fileExtension`.
- If `content.CopyToAsync` throws, no file is left behind at the path that would have been returned.
- This is the only class in the codebase permitted to contain the new method's `System.IO` calls (adapter layer, per `docs/architecture/filesystem.md`).

### FR-3: Refactor `ReprintExpeditionListHandler` to remove all direct `System.IO` usage
Update `ReprintExpeditionListHandler` to take `ITemporaryFileAccessor` as a constructor dependency and use it for temp-file creation and cleanup instead of `Path.GetTempPath()` / `File.OpenWrite()` / `File.Delete()`:

```csharp
public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly IPrintQueueSink _cupsSink;
    private readonly ITemporaryFileAccessor _temporaryFileAccessor;
    private readonly string _containerName;

    public ReprintExpeditionListHandler(
        IBlobStorageService blobStorageService,
        IPrintQueueSink cupsSink,
        ITemporaryFileAccessor temporaryFileAccessor,
        IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStorageService = blobStorageService;
        _cupsSink = cupsSink;
        _temporaryFileAccessor = temporaryFileAccessor;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail();
        }

        string? tempFile = null;
        try
        {
            await using var blobStream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
            tempFile = await _temporaryFileAccessor.CreateFromStreamAsync(blobStream, ".pdf", cancellationToken);

            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            if (tempFile != null)
            {
                _temporaryFileAccessor.DeleteIfExists(tempFile);
            }
        }
    }
}
```

The private `DeleteTempFile` helper and all `using System.IO`-only calls are removed from the handler. `IPrintQueueSink.SendAsync` keeps its existing `IEnumerable<string>` signature and is called exactly as before (single-element array).

**Acceptance criteria:**
- `ReprintExpeditionListHandler.cs` contains no direct references to `File`, `Path.GetTempPath`, or `Directory`.
- Behavior is unchanged from the caller's perspective: invalid blob path still short-circuits to `ReprintExpeditionListResponse.Fail()` without touching blob storage or the temp-file accessor; a failed blob download still propagates the exception without creating an orphaned temp file (nothing is created before the download completes); a successful send still deletes the temp file; a failing `SendAsync` still deletes the temp file (via `finally`) and still propagates the exception.
- `IPrintQueueSink`, `CombinedPrintQueueSink`, `AzureBlobPrintQueueSink`, `FileSystemPrintQueueSink`, `CupsPrintQueueSink`, `ICupsPrintingService`, and `ExpeditionListService` are unmodified by this change.

### FR-4: Update DI wiring in `ExpeditionListArchiveModule`
`ExpeditionListArchiveModule.AddExpeditionListArchiveModule` (`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`) manually constructs `ReprintExpeditionListHandler` via a factory delegate (to select the keyed `"cups"` `IPrintQueueSink` when available, falling back to the non-keyed sink). Update that factory to also resolve `ITemporaryFileAccessor` from the provider and pass it into the handler's constructor.

`ITemporaryFileAccessor` is already registered unconditionally in `AddPrintQueueSink` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, line 415, `services.AddFileSystemTemporaryFileAccessor()`) regardless of which `ExpeditionList:PrintSink` mode is configured, so no new registration is needed in any environment (dev/test "FileSystem", staging/prod "Cups"/"Combined"/"AzureBlob").

**Acceptance criteria:**
- `ReprintExpeditionListHandler` resolves correctly in all four `ExpeditionList:PrintSink` modes (`FileSystem`, `AzureBlob`, `Cups`, `Combined`) without a new `services.Add...` call, verified by existing/updated composition-root or integration tests.
- No change to `services.AddFileSystemTemporaryFileAccessor()` call site or its unconditional placement.

### FR-5: Update unit tests to remove real filesystem I/O
Rewrite `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` to mock `ITemporaryFileAccessor` instead of asserting against `Path.GetTempPath()` / `Directory.EnumerateFiles` / `File.Exists`. The existing filesystem-leak-detection assertions in this file are to be **deleted outright**, not preserved in any form within the handler test — confirmed decision, since once the handler depends only on the `ITemporaryFileAccessor` mock it has no filesystem behavior left to assert against, and equivalent real-I/O coverage is added at the adapter level instead (see below). This keeps concrete I/O — and its tests — in the adapter project, consistent with the I/O placement rule in `docs/architecture/filesystem.md`.

Add or update in `ReprintExpeditionListHandlerTests.cs`:
- A test verifying `CreateFromStreamAsync` is called with the downloaded blob stream and `.pdf` extension, and its returned path is the one passed to `IPrintQueueSink.SendAsync`.
- A test verifying `DeleteIfExists` is called with the created temp path after a successful `SendAsync`.
- A test verifying `DeleteIfExists` is called with the created temp path even when `SendAsync` throws (and the exception still propagates).
- A test verifying that when `DownloadAsync` throws, `CreateFromStreamAsync` and `DeleteIfExists` are never called (nothing was created).
- The existing invalid-blob-path test is retained essentially unchanged (still verifies no calls to blob storage or the sink; extend to also verify no call to `ITemporaryFileAccessor`).

Add new adapter-level unit tests for `FileSystemTemporaryFileAccessor.CreateFromStreamAsync` (real filesystem I/O is expected and acceptable there, matching the existing `DeleteIfExists`/`ReadAllBytesAsync` test pattern for that class if any exists, or the pattern used by other filesystem-adapter tests in the repo), covering: file created with contents matching the input stream, returned path ends with the supplied extension, and no partial file left behind when `CopyToAsync` throws.

**Acceptance criteria:**
- No test in `ReprintExpeditionListHandlerTests.cs` touches `Path.GetTempPath()`, `Directory.EnumerateFiles`, or any other real filesystem API.
- All new/updated tests pass under `dotnet test`.
- Adapter-level test(s) for `FileSystemTemporaryFileAccessor.CreateFromStreamAsync` exist and pass.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The refactor moves identical I/O operations (one file create/write, one file delete) behind an interface call; there is no additional buffering, copying, or network round-trip introduced. The extra virtual dispatch through `ITemporaryFileAccessor` is negligible relative to the blob download and CUPS print job it wraps.

### NFR-2: Security
No change to the security posture of the reprint flow:
- `BlobPathValidator.IsValid` continues to be the sole gate against path traversal on the *blob* path before any I/O occurs; this refactor does not touch that validator.
- The temp file name continues to be server-generated (`Guid`-based), not derived from user input, so there is no new path-injection surface in `CreateFromStreamAsync`.
- Temp files land in the OS temp directory with default OS permissions, identical to today's behavior — no permission model change.

## Data Model
No persistent data model changes. This is a pure code-structure refactor; no new entities, tables, or DTOs are introduced. `ITemporaryFileAccessor` remains a stateless service interface, not a domain type.

## API / Interface Design

**Modified interface** — `ITemporaryFileAccessor` (Application layer):
```csharp
public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default); // NEW
    void DeleteIfExists(string path);
}
```

**Modified implementation** — `FileSystemTemporaryFileAccessor` (Adapters.FileSystem):
```csharp
public async Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default)
{
    var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{fileExtension}");
    try
    {
        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);
        return path;
    }
    catch
    {
        DeleteIfExists(path);
        throw;
    }
}
```

**Modified consumer** — `ReprintExpeditionListHandler` constructor gains an `ITemporaryFileAccessor` parameter; `Handle` body as shown in FR-3. No public HTTP/MediatR contract (`ReprintExpeditionListRequest` / `ReprintExpeditionListResponse`) changes — this is an internal implementation refactor only, invisible to API consumers.

No REST endpoint, request/response DTO, or MediatR request/response shape changes as part of this work.

## Dependencies
- No new external libraries or services.
- Depends on the existing `ITemporaryFileAccessor` abstraction and its `FileSystemTemporaryFileAccessor` implementation, both of which already exist and are already wired into DI unconditionally.
- No change to `IBlobStorageService`, `IPrintQueueSink`, or any CUPS/Azure/FileSystem sink implementation.

## Out of Scope
- Changing `IPrintQueueSink.SendAsync`'s signature (from `IEnumerable<string>` file paths to `Stream`), as literally suggested in the brief. Rejected and confirmed per the Background section: the interface is shared by `ExpeditionListService`'s multi-file batch flow, `CombinedPrintQueueSink`'s dual-sink fan-out, and `AzureBlobPrintQueueSink`'s filename-based blob naming, none of which are touched by this finding. A stream-based `IPrintQueueSink`, if ever desired, is to be scoped as a separate arch-review item covering all consumers and sink implementations.
- Introducing a new, narrowly-scoped interface (e.g. `ITempFileStager`) dedicated to the reprint flow. Rejected and confirmed per the Background section: `ITemporaryFileAccessor` is extended instead, since it already exists, is already used by the sibling `ExpeditionListService`, and is already unconditionally registered in DI.
- Any change to `ExpeditionListService`, `CombinedPrintQueueSink`, `AzureBlobPrintQueueSink`, `FileSystemPrintQueueSink`, `CupsPrintQueueSink`, or `ICupsPrintingService`.
- Any change to `ICupsPrintingService.PrintAsync`'s file-path-based contract, even though SharpIpp's underlying `PrintJobRequest.Document` accepts a `Stream` — that is a separate potential refactor with its own blast radius (label printing also depends on `ICupsPrintingService`) and is not covered by this finding.
- Auditing other Application-layer handlers for similar inline `System.IO` usage. This spec addresses only `ReprintExpeditionListHandler`, the subject of the filed finding; a broader sweep would be a separate arch-review item.
- Any behavioral or user-facing change to the reprint feature — this is a structural refactor only.
- Retention policy, temp-directory location configuration, or cleanup-on-crash guarantees for orphaned temp files — unchanged from current behavior (best-effort delete; OS temp-dir cleanup remains the backstop).
- Retaining any real-filesystem-I/O assertions (`Path.GetTempPath()`, `Directory.EnumerateFiles`, `File.Exists`) in `ReprintExpeditionListHandlerTests.cs`. These are deleted outright; equivalent coverage moves to adapter-level tests for `FileSystemTemporaryFileAccessor` (see FR-5).

## Open Questions
None.

## Status: COMPLETE
