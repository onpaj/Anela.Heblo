# Implementation Task Plan: Extract temp-file I/O out of ReprintExpeditionListHandler

Source spec: `artifacts/feat-3474/spec.r1.md` (Status: COMPLETE). Source architecture review: `artifacts/feat-3474/arch-review.r1.md` (approved, no objections, Skip Design: true). Both are authoritative; resolve any ambiguity in this plan by re-reading them.

This is a pure structural refactor: no HTTP/MediatR contract changes, no behavior changes visible to callers, no new external dependencies.

---

### task: extend-temporary-file-accessor

## Goal
Extend the existing `ITemporaryFileAccessor` abstraction with a new stream-to-temp-file creation method, implement it in the filesystem adapter, wire it into DI for the `ReprintExpeditionListHandler` factory, and add adapter-level tests for the new method. This is groundwork consumed by the handler refactor (see `task: refactor-reprint-handler-and-tests`), but is independently buildable, testable, and reviewable: after this task, the codebase compiles, all existing tests pass unchanged, and the new capability exists but is not yet consumed by the handler.

## Why
`ReprintExpeditionListHandler` currently inlines raw `System.IO` calls (`Path.GetTempPath()`, `File.OpenWrite()`, `File.Delete()`), which violates the I/O-placement rule in `docs/architecture/filesystem.md` (I/O-bound logic belongs in `backend/src/Adapters/`, not in Application-layer `Features/{Feature}/...` handlers) and makes the handler hard to unit-test without touching the real filesystem. `ITemporaryFileAccessor` already exists for exactly this purpose and is already used by the sibling `ExpeditionListService`; this task extends it rather than introducing a new interface (confirmed decision in spec Background section, "Abstraction choice, confirmed").

## Files to touch
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs` (add method to interface)
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs` (implement method)
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` (DI factory: resolve and pass `ITemporaryFileAccessor` — but see Note below on sequencing with task 2)
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemTemporaryFileAccessorTests.cs` (add new test cases for `CreateFromStreamAsync`)

**Note on `ExpeditionListArchiveModule.cs` sequencing:** the DI factory currently constructs `new ReprintExpeditionListHandler(blobStorage, cupsSink, options)` — a 3-arg constructor. `task: refactor-reprint-handler-and-tests` changes that constructor to 4 args (adds `ITemporaryFileAccessor`). To keep this task independently compilable, update the factory in `ExpeditionListArchiveModule.cs` to resolve `ITemporaryFileAccessor` from the provider (`provider.GetRequiredService<ITemporaryFileAccessor>()`) but do NOT pass it into the constructor call yet if the handler constructor hasn't changed — instead, if this task runs before the handler refactor, leave the constructor call as-is (3 args) and let `task: refactor-reprint-handler-and-tests` add the 4th argument when it changes the constructor. If both tasks are implemented together/in sequence by the same developer, it is simplest to make both edits to `ExpeditionListArchiveModule.cs` as part of whichever task lands second. **Whichever task actually changes the `new ReprintExpeditionListHandler(...)` call site to pass the accessor, the end state after both tasks must match exactly the DI wiring described below.**

## Current state (for reference)

`ITemporaryFileAccessor.cs`:
```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    void DeleteIfExists(string path);
}
```

`FileSystemTemporaryFileAccessor.cs`:
```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;

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

`ExpeditionListArchiveModule.cs` (relevant excerpt):
```csharp
services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
{
    var blobStorage = provider.GetRequiredService<IBlobStorageService>();
    var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
        ?? provider.GetRequiredService<IPrintQueueSink>();
    var options = provider.GetRequiredService<IOptions<ExpeditionListArchiveOptions>>();
    return new ReprintExpeditionListHandler(blobStorage, cupsSink, options);
});
```

`ITemporaryFileAccessor` is already registered unconditionally via `services.AddFileSystemTemporaryFileAccessor()` inside `AddPrintQueueSink` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:415`), called from `Program.cs:133`, independent of `AddExpeditionListArchiveModule`. **No new `services.Add...` registration is needed** — do not add one.

## Required changes

### 1. `ITemporaryFileAccessor.cs` — add method
```csharp
Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default);
```
Place it between `ReadAllBytesAsync` and `DeleteIfExists` (matching the ordering shown in the spec's "Modified interface" section), keeping existing members unchanged. Add `using System.IO;` if not already implicitly available (check for existing implicit usings in the project; the file currently has none, so `Stream` may need `System.IO` — verify via build).

Resulting interface:
```csharp
public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default);
    void DeleteIfExists(string path);
}
```

### 2. `FileSystemTemporaryFileAccessor.cs` — implement method
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
- `fileExtension` is appended as-is (caller supplies the leading dot, e.g. `.pdf`).
- On any exception during file creation or copy, the partially-written file (if any) is deleted via the existing `DeleteIfExists` before rethrowing.

### 3. `ExpeditionListArchiveModule.cs` — DI wiring
Update the factory to resolve `ITemporaryFileAccessor` and pass it to the handler constructor:
```csharp
services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
{
    var blobStorage = provider.GetRequiredService<IBlobStorageService>();
    var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
        ?? provider.GetRequiredService<IPrintQueueSink>();
    var temporaryFileAccessor = provider.GetRequiredService<ITemporaryFileAccessor>();
    var options = provider.GetRequiredService<IOptions<ExpeditionListArchiveOptions>>();
    return new ReprintExpeditionListHandler(blobStorage, cupsSink, temporaryFileAccessor, options);
});
```
This requires the `ReprintExpeditionListHandler` constructor to accept `ITemporaryFileAccessor` as its 3rd parameter (before `IOptions<...>`) — see `task: refactor-reprint-handler-and-tests` for that change. Add `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` to `ExpeditionListArchiveModule.cs` if not already present (needed for the `ITemporaryFileAccessor` type reference).

### 4. `FileSystemTemporaryFileAccessorTests.cs` — new adapter tests
Follow the existing per-test-temp-dir + `IDisposable` pattern already in this file. Add test cases covering:
- `CreateFromStreamAsync` with a stream of known bytes returns a path under `Path.GetTempPath()` whose file contents match the input stream byte-for-byte.
- The returned path ends with the supplied `fileExtension`.
- If the source stream throws during `CopyToAsync` (e.g. a custom `Stream` subclass or a `Mock<Stream>`/wrapper stream that throws on `CopyToAsync`/`Read`), no file is left behind at the path that would have been returned, and the original exception propagates.

Example shape (adapt to repo's existing Moq/Xunit conventions used elsewhere in this test file — note this file currently uses no mocking library, only real I/O, so a throwing-stream test will need either a small custom `Stream` subclass or a `MemoryStream`-wrapping decorator that throws on read):
```csharp
[Fact]
public async Task CreateFromStreamAsync_ValidStream_CreatesFileWithMatchingContentAndExtension()
{
    var contentBytes = new byte[] { 1, 2, 3, 4, 5 };
    using var stream = new MemoryStream(contentBytes);
    var accessor = new FileSystemTemporaryFileAccessor();

    var path = await accessor.CreateFromStreamAsync(stream, ".pdf");

    try
    {
        Assert.EndsWith(".pdf", path);
        Assert.StartsWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), path);
        Assert.Equal(contentBytes, await File.ReadAllBytesAsync(path));
    }
    finally
    {
        accessor.DeleteIfExists(path);
    }
}

[Fact]
public async Task CreateFromStreamAsync_StreamThrows_LeavesNoPartialFileAndPropagatesException()
{
    var accessor = new FileSystemTemporaryFileAccessor();
    using var throwingStream = new ThrowingStream(); // small test-only Stream subclass whose Read/CopyToAsync throws IOException

    await Assert.ThrowsAsync<IOException>(() => accessor.CreateFromStreamAsync(throwingStream, ".pdf"));

    // Cannot assert on the exact generated path (it's a fresh Guid each call), so instead
    // verify no NEW .pdf file appears in Path.GetTempPath() as a result of this call:
    // snapshot before/after, or refactor CreateFromStreamAsync's path-generation to be
    // injectable/observable if a more precise assertion is needed. At minimum, assert the
    // exception type/propagation; a before/after directory-listing diff (scoped to files
    // matching a GUID-hex + ".pdf" pattern, mirroring the pattern already deleted from the
    // handler test) is acceptable here since this IS the adapter-level I/O test file.
}
```
Tests must clean up any file they create (use `try/finally` with `accessor.DeleteIfExists(path)`, since `CreateFromStreamAsync` always writes to the shared OS temp dir, not the per-test `_testDir` used by other tests in this file).

## Acceptance criteria
- `ITemporaryFileAccessor` compiles with the new `CreateFromStreamAsync` method; `ReadAllBytesAsync` and `DeleteIfExists` signatures are unchanged.
- `FileSystemTemporaryFileAccessor.CreateFromStreamAsync`: given a stream with known bytes, returns a path under `Path.GetTempPath()` whose contents match the input byte-for-byte; returned path ends with the supplied `fileExtension`; if `content.CopyToAsync` throws, no file is left behind at the path that would have been returned, and the exception propagates.
- `FileSystemTemporaryFileAccessor` is the only class containing the new method's `System.IO` calls.
- New/updated adapter tests in `FileSystemTemporaryFileAccessorTests.cs` pass under `dotnet test`.
- `ExpeditionListArchiveModule`'s factory resolves `ITemporaryFileAccessor` via `provider.GetRequiredService<ITemporaryFileAccessor>()` — no new `services.Add...` registration introduced.
- Full solution builds (`dotnet build`) and `dotnet format` produces no diff.
- No change to `services.AddFileSystemTemporaryFileAccessor()` call site or its unconditional placement in `ServiceCollectionExtensions.cs`.

## Dependencies
None — this task can be implemented first. `task: refactor-reprint-handler-and-tests` depends on this task (it consumes `ITemporaryFileAccessor.CreateFromStreamAsync` and requires the DI factory to construct the handler with the accessor).

---

### task: refactor-reprint-handler-and-tests

## Goal
Refactor `ReprintExpeditionListHandler` to remove all direct `System.IO` usage by delegating temp-file creation and cleanup to `ITemporaryFileAccessor`, and rewrite its unit tests to mock the accessor instead of asserting against real filesystem state. This resolves the SRP violation and untestability problem described in the spec: the handler becomes a pure MediatR orchestrator with no filesystem knowledge.

## Why
`ReprintExpeditionListHandler` currently calls `Path.GetTempPath()`, `File.OpenWrite()`, and `File.Delete()` directly — inline `System.IO` usage in an Application-layer handler, violating `docs/architecture/filesystem.md`'s I/O-placement rule. Its existing test suite has to assert against real `Directory.EnumerateFiles`/`File.Exists` to verify no temp files leak, which is real filesystem I/O in what should be a pure orchestration unit test. `IPrintQueueSink.SendAsync`'s existing `IEnumerable<string>` signature is unchanged and out of scope (see spec's "Out of Scope" section — this was a deliberate, PO-confirmed deviation from the original brief).

## Depends on
`task: extend-temporary-file-accessor` — this task requires `ITemporaryFileAccessor.CreateFromStreamAsync` to exist (interface + `FileSystemTemporaryFileAccessor` implementation) and requires `ExpeditionListArchiveModule`'s DI factory to be able to resolve `ITemporaryFileAccessor`. If that task has not yet made the `ExpeditionListArchiveModule.cs` edit (per the sequencing note in that task), this task must make it as part of changing the handler's constructor signature — the end state must match the "Required DI wiring" section below regardless of which task actually applies the edit.

## Files to touch
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` (only if not already updated by `task: extend-temporary-file-accessor` — verify the `new ReprintExpeditionListHandler(...)` call site passes 4 args including the accessor; update if it still passes 3)
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` (full rewrite of body; class/namespace stay)

## Current handler (to be replaced)
```csharp
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly IPrintQueueSink _cupsSink;
    private readonly string _containerName;

    public ReprintExpeditionListHandler(IBlobStorageService blobStorageService, IPrintQueueSink cupsSink, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStorageService = blobStorageService;
        _cupsSink = cupsSink;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail();
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        try
        {
            await using var blobStream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
            await using var fileStream = File.OpenWrite(tempFile);
            await blobStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch
        {
            DeleteTempFile(tempFile);
            throw;
        }

        try
        {
            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    private static void DeleteTempFile(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
```

## Required new handler
```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

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
Note the behavioral nuance (confirmed in arch-review Decision 2): the `finally` only calls `DeleteIfExists` when `tempFile` is non-null, i.e. only after `CreateFromStreamAsync` has returned successfully. If `DownloadAsync` throws, `tempFile` stays `null` and nothing is created or deleted. If `CreateFromStreamAsync` itself throws, it has already cleaned up its own partial file internally (per `task: extend-temporary-file-accessor`), so the handler's `finally` correctly does nothing extra. The private `DeleteTempFile` helper is removed entirely; no direct `File`/`Path`/`Directory` references remain in this file.

## Required DI wiring (verify or apply)
`ExpeditionListArchiveModule.cs` factory must construct the handler with 4 args, in this order: `blobStorage, cupsSink, temporaryFileAccessor, options`:
```csharp
services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
{
    var blobStorage = provider.GetRequiredService<IBlobStorageService>();
    var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
        ?? provider.GetRequiredService<IPrintQueueSink>();
    var temporaryFileAccessor = provider.GetRequiredService<ITemporaryFileAccessor>();
    var options = provider.GetRequiredService<IOptions<ExpeditionListArchiveOptions>>();
    return new ReprintExpeditionListHandler(blobStorage, cupsSink, temporaryFileAccessor, options);
});
```
If `task: extend-temporary-file-accessor` already applied this exact change, verify it and move on. If not, apply it here (including the `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` import if missing).

## Required test rewrite
Rewrite `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`. Delete all real-filesystem-I/O assertions outright (no `Path.GetTempPath()`, `Directory.EnumerateFiles`, `File.Exists`, regex-based leak detection, or `using System.IO`/`using System.Text.RegularExpressions` needed anymore in this file). Add a `Mock<ITemporaryFileAccessor>` field alongside the existing `Mock<IBlobStorageService>` and `Mock<IPrintQueueSink>`, and construct the handler with 4 args.

Required test cases (replace the 4 existing `[Fact]`s with these 5):
1. **`Handle_ValidBlobPath_DownloadsAndSendsToCupsSink`** (keep, adapt): verifies `CreateFromStreamAsync` is called with the downloaded blob stream and `.pdf` extension, and the path it returns is the one passed to `IPrintQueueSink.SendAsync`.
2. **New — successful send deletes temp file**: verifies `DeleteIfExists` is called with the created temp path after a successful `SendAsync`.
3. **New — failing send still deletes temp file and propagates**: mock `SendAsync` to throw; verify `DeleteIfExists` is still called with the created temp path (via `finally`), and the exception still propagates out of `Handle`.
4. **New — failed download creates nothing**: mock `DownloadAsync` to throw; verify `CreateFromStreamAsync` and `DeleteIfExists` are never called, and the exception propagates.
5. **`Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob`** (keep, extend): unchanged assertions (no call to blob storage or the sink) plus a new assertion that `ITemporaryFileAccessor` (`CreateFromStreamAsync` and `DeleteIfExists`) is never called.

Example shape for the mock setup and one new case (adapt exact Moq syntax to match the file's existing style):
```csharp
private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
private readonly Mock<IPrintQueueSink> _cupsSinkMock;
private readonly Mock<ITemporaryFileAccessor> _temporaryFileAccessorMock;
private readonly ReprintExpeditionListHandler _handler;
private const string ContainerName = "expedition-lists";

public ReprintExpeditionListHandlerTests()
{
    _blobStorageServiceMock = new Mock<IBlobStorageService>();
    _cupsSinkMock = new Mock<IPrintQueueSink>();
    _temporaryFileAccessorMock = new Mock<ITemporaryFileAccessor>();
    _handler = new ReprintExpeditionListHandler(
        _blobStorageServiceMock.Object,
        _cupsSinkMock.Object,
        _temporaryFileAccessorMock.Object,
        Options.Create(new ExpeditionListArchiveOptions()));
}

[Fact]
public async Task Handle_SendAsyncThrows_StillDeletesTempFileAndPropagates()
{
    var blobPath = "2026-03-25/picking-list-002.pdf";
    var blobStream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    const string tempPath = "/tmp/generated-guid.pdf";

    _blobStorageServiceMock
        .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
        .ReturnsAsync(blobStream);
    _temporaryFileAccessorMock
        .Setup(a => a.CreateFromStreamAsync(blobStream, ".pdf", default))
        .ReturnsAsync(tempPath);
    _cupsSinkMock
        .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
        .ThrowsAsync(new IOException("cups unavailable"));

    var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

    await Assert.ThrowsAsync<IOException>(() => _handler.Handle(request, default));

    _temporaryFileAccessorMock.Verify(a => a.DeleteIfExists(tempPath), Times.Once);
}
```

## Acceptance criteria
- `ReprintExpeditionListHandler.cs` contains no direct references to `File`, `Path.GetTempPath`, or `Directory`; no `using System.IO;` needed for those APIs (a `using` for `Stream`/`IOException` types is not required since the handler no longer constructs streams directly — verify via build).
- Behavior unchanged from the caller's perspective: invalid blob path still short-circuits to `ReprintExpeditionListResponse.Fail()` without touching blob storage or the temp-file accessor; a failed blob download still propagates without creating an orphaned temp file; a successful send still deletes the temp file; a failing `SendAsync` still deletes the temp file (via `finally`) and still propagates the exception.
- `IPrintQueueSink`, `CombinedPrintQueueSink`, `AzureBlobPrintQueueSink`, `FileSystemPrintQueueSink`, `CupsPrintQueueSink`, `ICupsPrintingService`, and `ExpeditionListService` remain unmodified.
- No REST/MediatR contract change: `ReprintExpeditionListRequest`/`ReprintExpeditionListResponse` shapes are untouched.
- `ReprintExpeditionListHandler` resolves correctly (via `ExpeditionListArchiveModule`) in all four `ExpeditionList:PrintSink` modes (`FileSystem`, `AzureBlob`, `Cups`, `Combined`) — verify via existing/updated composition-root or integration tests, or a targeted DI-resolution test if none currently exists for this handler.
- No test in `ReprintExpeditionListHandlerTests.cs` touches `Path.GetTempPath()`, `Directory.EnumerateFiles`, `File.Exists`, or any other real filesystem API.
- All 5 required test cases exist and pass under `dotnet test`.
- Full solution builds (`dotnet build`) and `dotnet format` produces no diff.

## Dependencies
Depends on `task: extend-temporary-file-accessor` (requires `ITemporaryFileAccessor.CreateFromStreamAsync` to exist before the handler can consume it, and the DI factory must be able to resolve `ITemporaryFileAccessor`).
