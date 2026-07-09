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
