### task: add-temporary-file-accessor-contract-and-adapter


**What this task does:** Introduces the new `ITemporaryFileAccessor` contract in the Application layer, its `System.IO`-backed implementation in the FileSystem adapter project, registers it in DI (unconditionally, not gated by the `PrintSink` switch), and adds a new unit test for the adapter implementation. `ExpeditionListService` is **not** modified in this task — it keeps using `File.*` directly for now. This task must leave the build green and all existing tests passing, with the new type present but unused by production code except via DI registration.

**File(s) to create/modify:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemTemporaryFileAccessorTests.cs`

#### Step 1 — Create the `ITemporaryFileAccessor` contract

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs` with exactly this content:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    void DeleteIfExists(string path);
}
```

This mirrors the existing `IExpeditionPickingSource.cs` in the same folder (see that file for style precedent — plain interface, no XML doc header, file-scoped namespace). No `System.IO` types appear in the signature, per spec FR-1.

#### Step 2 — Create the `FileSystemTemporaryFileAccessor` adapter implementation

Create `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs` with exactly this content:

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

This sits alongside the existing `FileSystemPrintQueueSink.cs` in the same folder and preserves identical semantics to the code currently inline in `ExpeditionListService` (same guard-then-delete pattern, same pass-through read with cancellation token).

#### Step 3 — Register the new implementation in `FileSystemAdapterServiceCollectionExtensions`

Modify `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs`. Current file:

```csharp
using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.FileSystem;

public static class FileSystemAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the filesystem-based <see cref="IPrintQueueSink"/> implementation.
    /// PrintPickingListOptions is bound by ExpeditionListModule in the Application layer,
    /// so this extension takes no IConfiguration parameter.
    /// </summary>
    public static IServiceCollection AddFileSystemPrintQueueSink(this IServiceCollection services)
    {
        services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
        return services;
    }
}
```

Replace its contents with:

```csharp
using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.FileSystem;

public static class FileSystemAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the filesystem-based <see cref="IPrintQueueSink"/> implementation.
    /// PrintPickingListOptions is bound by ExpeditionListModule in the Application layer,
    /// so this extension takes no IConfiguration parameter.
    /// </summary>
    public static IServiceCollection AddFileSystemPrintQueueSink(this IServiceCollection services)
    {
        services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
        return services;
    }

    /// <summary>
    /// Registers the filesystem-based <see cref="ITemporaryFileAccessor"/> implementation.
    /// Used by ExpeditionListService to read/delete exported PDFs regardless of which
    /// print sink (ExpeditionList:PrintSink) is configured, since exported files always
    /// land on local disk first.
    /// </summary>
    public static IServiceCollection AddFileSystemTemporaryFileAccessor(this IServiceCollection services)
    {
        services.AddScoped<ITemporaryFileAccessor, FileSystemTemporaryFileAccessor>();
        return services;
    }
}
```

#### Step 4 — Wire the registration into the composition root, outside the `PrintSink` switch

Modify `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. Find the `AddPrintQueueSink` method (around line 406):

```csharp
    public static IServiceCollection AddPrintQueueSink(this IServiceCollection services, IConfiguration configuration)
    {
        // The CUPS label-printing infrastructure (ILabelPrintingService) is always available —
        // it is used by MaterialContainer label printing regardless of the expedition print sink.
        services.AddCupsPrinting(configuration);

        var printSink = configuration["ExpeditionList:PrintSink"];
        switch (printSink)
        {
```

Change it to add the new unconditional registration right after `AddCupsPrinting`, **before** the `switch`:

```csharp
    public static IServiceCollection AddPrintQueueSink(this IServiceCollection services, IConfiguration configuration)
    {
        // The CUPS label-printing infrastructure (ILabelPrintingService) is always available —
        // it is used by MaterialContainer label printing regardless of the expedition print sink.
        services.AddCupsPrinting(configuration);

        // Temp-file read/delete is needed regardless of which PrintSink is configured — exported
        // PDFs always land on local disk first (see IExpeditionPickingSource.CreatePickingListAsync),
        // so this is registered unconditionally rather than inside the switch below.
        services.AddFileSystemTemporaryFileAccessor();

        var printSink = configuration["ExpeditionList:PrintSink"];
        switch (printSink)
        {
```

Leave the rest of the method (the `switch` block and its four cases, lines ~413–438) completely unchanged — do **not** add `AddFileSystemTemporaryFileAccessor()` inside the `default:` case; it must only appear once, before the switch. `Anela.Heblo.API` already references `Anela.Heblo.Adapters.FileSystem` (used for `AddFileSystemPrintQueueSink` in the same file), so no new project reference or `using` is needed for the new call itself — `FileSystemAdapterServiceCollectionExtensions` is a `public static class` in a namespace already in scope where `AddFileSystemPrintQueueSink()` is called.

#### Step 5 — Add a unit test for `FileSystemTemporaryFileAccessor`

Create `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemTemporaryFileAccessorTests.cs`:

```csharp
using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class FileSystemTemporaryFileAccessorTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public FileSystemTemporaryFileAccessorTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task ReadAllBytesAsync_ExistingFile_ReturnsFileContent()
    {
        var path = Path.Combine(_testDir, "file.pdf");
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(path, expectedBytes);

        var accessor = new FileSystemTemporaryFileAccessor();
        var bytes = await accessor.ReadAllBytesAsync(path);

        Assert.Equal(expectedBytes, bytes);
    }

    [Fact]
    public async Task ReadAllBytesAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_testDir, "does-not-exist.pdf");
        var accessor = new FileSystemTemporaryFileAccessor();

        await Assert.ThrowsAsync<FileNotFoundException>(() => accessor.ReadAllBytesAsync(path));
    }

    [Fact]
    public void DeleteIfExists_ExistingFile_DeletesIt()
    {
        var path = Path.Combine(_testDir, "file.pdf");
        File.WriteAllText(path, "content");
        var accessor = new FileSystemTemporaryFileAccessor();

        accessor.DeleteIfExists(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteIfExists_NonExistentFile_DoesNotThrow()
    {
        var path = Path.Combine(_testDir, "never-existed.pdf");
        var accessor = new FileSystemTemporaryFileAccessor();

        var exception = Record.Exception(() => accessor.DeleteIfExists(path));

        Assert.Null(exception);
    }
}
```

This follows the same `IDisposable` + `Path.GetTempPath()`/`Guid.NewGuid()` temp-directory pattern already used in `FileSystemPrintQueueSinkTests.cs` in the same folder.

#### Acceptance criteria

- `dotnet build` succeeds for the whole solution (run from `backend/`: `dotnet build`).
- `dotnet format` produces no diff (or is applied) for all files touched in this task.
- New tests in `FileSystemTemporaryFileAccessorTests.cs` (4 tests) pass: run `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~FileSystemTemporaryFileAccessorTests`.
- All pre-existing tests still pass (this task does not touch `ExpeditionListService.cs` or its tests): run `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ExpeditionList`.
- Starting the API locally (or via the existing test harness) with no `ExpeditionList:PrintSink` config set, and separately with `ExpeditionList:PrintSink=AzureBlob`/`Cups`/`Combined`, must not throw a DI resolution error — verify by inspecting that `AddFileSystemTemporaryFileAccessor()` is called unconditionally (i.e., visually confirm in the diff that the call sits before the `switch (printSink)` line, not inside any `case`). If an integration/smoke test already exercises `AddPrintQueueSink` for multiple `PrintSink` values, it must still pass; if none exists, this is verified by code inspection only (no new test is required for this — out of scope per spec FR-3's acceptance criteria, which is satisfied by correct placement).
- `ITemporaryFileAccessor.cs` contains no `System.IO` types in its member signatures (visually confirm: only `string`, `byte[]`, `Task`, `CancellationToken`).

---
