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
