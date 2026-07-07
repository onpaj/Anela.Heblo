# Remove direct filesystem I/O from ExpeditionListService — Implementation Plan

**Goal:** Eliminate the direct `File.Exists`/`File.Delete`/`File.ReadAllBytesAsync` calls in `ExpeditionListService` (Application layer) by introducing an `ITemporaryFileAccessor` abstraction, implemented by a new `FileSystemTemporaryFileAccessor` in the `Anela.Heblo.Adapters.FileSystem` project, mirroring the existing `IPrintQueueSink` / `FileSystemPrintQueueSink` split. This is a pure refactor — no change to `IExpeditionListService`'s public contract, generated PDFs, email content, or print-queue behavior.

**Architecture:** `Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs` defines a two-member, `System.IO`-free contract (`ReadAllBytesAsync`, `DeleteIfExists`). `Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs` implements it with the exact same `System.IO.File` calls the service currently makes inline. `Anela.Heblo.API`'s composition root registers the implementation **unconditionally** (not inside the `PrintSink` config switch), since temp-file cleanup/read is needed regardless of which print sink is active. `ExpeditionListService` is refactored to depend on the interface instead of `System.IO.File` directly. Existing unit tests are updated to mock `ITemporaryFileAccessor` instead of touching the real filesystem.

**Tech Stack:** C# / .NET 8, xUnit, Moq, `Microsoft.Extensions.DependencyInjection`. Backend only — no frontend changes.

---

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

### task: refactor-expeditionlistservice-to-use-temporary-file-accessor

**What this task does:** Injects `ITemporaryFileAccessor` into `ExpeditionListService`, replaces the bodies of `Cleanup` and `SendEmailCopy` to delegate to it instead of calling `System.IO.File` directly, and removes all direct `File.*` usage from the file. Updates the two existing test files (`ExpeditionListServicePrintSinkTests.cs`, `ExpeditionListServiceOrderStateTests.cs`) to construct `ExpeditionListService` with a `Mock<ITemporaryFileAccessor>`, including rewriting the one test that currently depends on the real filesystem (`PrintPickingListAsync_CleanupRunsAfterSuccess`). Depends on `add-temporary-file-accessor-contract-and-adapter` being complete (needs `ITemporaryFileAccessor` and `FileSystemTemporaryFileAccessor` to exist).

**File(s) to create/modify:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs`

#### Step 1 — Refactor `ExpeditionListService.cs`

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` with:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Xcc.Services.Email;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public class ExpeditionListService : IExpeditionListService
{
    private readonly IExpeditionPickingSource _pickingSource;
    private readonly IEmailSender _emailSender;
    private readonly TimeProvider _clock;
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly IPrintQueueSink _printQueueSink;
    private readonly ITemporaryFileAccessor _temporaryFileAccessor;
    private readonly ILogger<ExpeditionListService> _logger;

    public ExpeditionListService(
        IExpeditionPickingSource pickingSource,
        IEmailSender emailSender,
        TimeProvider clock,
        IOptions<PrintPickingListOptions> options,
        IPrintQueueSink printQueueSink,
        ITemporaryFileAccessor temporaryFileAccessor,
        ILogger<ExpeditionListService> logger)
    {
        _pickingSource = pickingSource;
        _emailSender = emailSender;
        _clock = clock;
        _options = options;
        _printQueueSink = printQueueSink;
        _temporaryFileAccessor = temporaryFileAccessor;
        _logger = logger;
    }

    public async Task<ExpeditionPickingResult> PrintPickingListAsync(
        ExpeditionPickingRequest request,
        IList<string>? emailList = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating new expedition list");

        Func<IList<string>, Task>? batchCallback = null;

        if (request.SendToPrinter || (emailList != null && emailList.Any()))
        {
            batchCallback = async files =>
            {
                if (request.SendToPrinter)
                {
                    await _printQueueSink.SendAsync(files, cancellationToken);
                    _logger.LogDebug("Batch sent to print queue");
                }

                if (emailList != null && emailList.Any())
                {
                    await SendEmailCopy(files, emailList, cancellationToken);
                    _logger.LogDebug("Batch email copy sent");
                }
            };
        }

        var result = await _pickingSource.CreatePickingListAsync(request, batchCallback, cancellationToken);

        _logger.LogDebug("Expedition list complete — {Total} orders processed", result.TotalCount);

        await Cleanup(result);

        return result;
    }

    private Task Cleanup(ExpeditionPickingResult result)
    {
        foreach (var f in result.ExportedFiles)
        {
            _temporaryFileAccessor.DeleteIfExists(f);
        }

        return Task.CompletedTask;
    }

    private async Task SendEmailCopy(IList<string> files, IEnumerable<string> emailRecipients, CancellationToken cancellationToken)
    {
        var now = _clock.GetLocalNow();
        var message = new EmailMessage
        {
            From = _options.Value.EmailSender,
            Subject = $"Expedice {now:yyyy-MM-dd}",
            HtmlContent = $@"
<strong>Expedice vygenerovana {now:yyyy-MM-dd HH:mm:ss}</strong></br>
</br>
</br>
",
            To = emailRecipients.ToList()
        };

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

        await _emailSender.SendEmailAsync(message);
        _logger.LogDebug("Sent email copy");
    }
}
```

Notes on this change versus the original file:
- New constructor parameter `ITemporaryFileAccessor temporaryFileAccessor`, positioned after `printQueueSink` and before `logger` (matches the design doc's specified ordering: dependencies first, logger last).
- `Cleanup` now calls `_temporaryFileAccessor.DeleteIfExists(f)` instead of the inline `File.Exists`/`File.Delete` guard.
- `SendEmailCopy` gained a `CancellationToken cancellationToken` parameter (it previously took none) so it can forward the token into `_temporaryFileAccessor.ReadAllBytesAsync(a, cancellationToken)`. Its single call site inside `PrintPickingListAsync`'s `batchCallback` is updated to pass `cancellationToken` through. This is the one accepted minor behavior change called out in spec FR-4/Open Questions and the arch-review's risk table: cancellation now takes effect during attachment reads, not just before — not a functional regression.
- `Path.GetFileName(a)` remains inline (pure string operation, not I/O — confirmed out of scope for the accessor by both the spec and design doc).
- `using System.IO;` is not present and must not be added — confirm the file has zero remaining `File.*`/`System.IO.File` references (there is no explicit `using System.IO;` in the original file already, since `File`/`Path` were resolved via implicit global usings or the SDK's implicit usings; do not add one).
- `IExpeditionListService` (the public interface) is untouched — do not modify `IExpeditionListService.cs`.

#### Step 2 — Update `ExpeditionListServicePrintSinkTests.cs`

Modify `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs`. Add a `Mock<ITemporaryFileAccessor>` field and pass it into the constructor call in `CreateService()`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class ExpeditionListServicePrintSinkTests
{
    private readonly Mock<IExpeditionPickingSource> _pickingSource = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IPrintQueueSink> _printQueueSink = new();
    private readonly Mock<ITemporaryFileAccessor> _temporaryFileAccessor = new();

    private ExpeditionListService CreateService() => new ExpeditionListService(
        _pickingSource.Object,
        _emailSender.Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        _printQueueSink.Object,
        _temporaryFileAccessor.Object,
        NullLogger<ExpeditionListService>.Instance);

    private void SetupSourceInvokingCallback(IList<string> filesToPassToCallback)
    {
        _pickingSource
            .Setup(x => x.CreatePickingListAsync(
                It.IsAny<ExpeditionPickingRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                async (ExpeditionPickingRequest req, Func<IList<string>, Task>? cb, CancellationToken ct) =>
                {
                    if (cb != null)
                        await cb(filesToPassToCallback);
                    return new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 1 };
                });
    }

    [Fact]
    public async Task PrintPickingListAsync_SendToPrinterTrue_CallsSink()
    {
        var batchFiles = new List<string>();
        SetupSourceInvokingCallback(batchFiles);

        var request = new ExpeditionPickingRequest { SendToPrinter = true };
        var svc = CreateService();

        await svc.PrintPickingListAsync(request);

        _printQueueSink.Verify(
            x => x.SendAsync(batchFiles, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PrintPickingListAsync_SendToPrinterFalse_DoesNotCallSink()
    {
        _pickingSource
            .Setup(x => x.CreatePickingListAsync(
                It.IsAny<ExpeditionPickingRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 1 });

        var request = new ExpeditionPickingRequest { SendToPrinter = false };
        var svc = CreateService();

        await svc.PrintPickingListAsync(request);

        _printQueueSink.Verify(
            x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

Only the `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` addition, the new `_temporaryFileAccessor` field, and the extra `.Object` argument in `CreateService()` are new — the two `[Fact]` test bodies are unchanged (they don't touch cleanup/email behavior).

#### Step 3 — Update `ExpeditionListServiceOrderStateTests.cs`

Modify `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs`. Same field + constructor change as Step 2, **plus** rewrite `PrintPickingListAsync_CleanupRunsAfterSuccess` to use the mock instead of `Path.GetTempFileName()`/`File.Exists`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class ExpeditionListServiceOrderStateTests
{
    private readonly Mock<IExpeditionPickingSource> _pickingSource = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IPrintQueueSink> _printQueueSink = new();
    private readonly Mock<ITemporaryFileAccessor> _temporaryFileAccessor = new();

    private ExpeditionListService CreateService() => new ExpeditionListService(
        _pickingSource.Object,
        _emailSender.Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        _printQueueSink.Object,
        _temporaryFileAccessor.Object,
        NullLogger<ExpeditionListService>.Instance);

    private void SetupSourceInvokingCallback(IList<string> filesToPassToCallback)
    {
        _pickingSource
            .Setup(x => x.CreatePickingListAsync(
                It.IsAny<ExpeditionPickingRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                async (ExpeditionPickingRequest req, Func<IList<string>, Task>? cb, CancellationToken ct) =>
                {
                    if (cb != null)
                        await cb(filesToPassToCallback);
                    return new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 1 };
                });
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenEmailThrows_ExceptionPropagates()
    {
        SetupSourceInvokingCallback(new List<string>());
        _emailSender
            .Setup(x => x.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP failure"));

        var request = new ExpeditionPickingRequest { ChangeOrderState = true, SendToPrinter = false };
        var svc = CreateService();

        await Assert.ThrowsAsync<Exception>(() =>
            svc.PrintPickingListAsync(request, emailList: new[] { "user@example.com" }));
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenPrinterThrows_ExceptionPropagates()
    {
        SetupSourceInvokingCallback(new List<string>());
        _printQueueSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Print queue failure"));

        var request = new ExpeditionPickingRequest { ChangeOrderState = true, SendToPrinter = true };
        var svc = CreateService();

        await Assert.ThrowsAsync<Exception>(() => svc.PrintPickingListAsync(request));
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenAllSucceed_PrinterCalledBeforeEmail()
    {
        var callOrder = new List<string>();
        SetupSourceInvokingCallback(new List<string>());

        _printQueueSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("printer"))
            .Returns(Task.CompletedTask);

        _emailSender
            .Setup(x => x.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("email"))
            .Returns(Task.CompletedTask);

        var request = new ExpeditionPickingRequest { ChangeOrderState = true, SendToPrinter = true };
        var svc = CreateService();

        await svc.PrintPickingListAsync(request, emailList: new[] { "user@example.com" });

        Assert.Equal(new[] { "printer", "email" }, callOrder);
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenNeitherPrinterNorEmail_NullCallbackPassedToSource()
    {
        Func<IList<string>, Task>? capturedCallback = null;
        _pickingSource
            .Setup(x => x.CreatePickingListAsync(
                It.IsAny<ExpeditionPickingRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback(
                (ExpeditionPickingRequest req, Func<IList<string>, Task>? cb, CancellationToken ct) =>
                    capturedCallback = cb)
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>() });

        var request = new ExpeditionPickingRequest { SendToPrinter = false };
        var svc = CreateService();

        await svc.PrintPickingListAsync(request, emailList: null);

        Assert.Null(capturedCallback);
    }

    [Fact]
    public async Task PrintPickingListAsync_CleanupRunsAfterSuccess()
    {
        var exportedFile = "/tmp/expedition-export-fake.pdf";
        _pickingSource
            .Setup(x => x.CreatePickingListAsync(
                It.IsAny<ExpeditionPickingRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionPickingResult
            {
                ExportedFiles = new[] { exportedFile },
                TotalCount = 1,
            });

        var request = new ExpeditionPickingRequest { SendToPrinter = false };
        var svc = CreateService();

        await svc.PrintPickingListAsync(request);

        _temporaryFileAccessor.Verify(x => x.DeleteIfExists(exportedFile), Times.Once);
    }

    [Fact]
    public async Task PrintPickingListAsync_EmailAttachments_BuiltFromAccessorBytes()
    {
        var exportedFile = "/tmp/expedition-export-fake.pdf";
        var expectedBytes = new byte[] { 10, 20, 30 };
        SetupSourceInvokingCallback(new List<string> { exportedFile });
        _temporaryFileAccessor
            .Setup(x => x.ReadAllBytesAsync(exportedFile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedBytes);

        EmailMessage? capturedMessage = null;
        _emailSender
            .Setup(x => x.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        var request = new ExpeditionPickingRequest { SendToPrinter = false };
        var svc = CreateService();

        await svc.PrintPickingListAsync(request, emailList: new[] { "user@example.com" });

        Assert.NotNull(capturedMessage);
        var attachment = Assert.Single(capturedMessage!.Attachments);
        Assert.Equal(Convert.ToBase64String(expectedBytes), attachment.Content);
        Assert.Equal("expedition-export-fake.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
    }
}
```

Changes versus the original file:
- Added `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` and the `_temporaryFileAccessor` field, threaded into `CreateService()`.
- `PrintPickingListAsync_CleanupRunsAfterSuccess` no longer calls `Path.GetTempFileName()` or asserts `File.Exists`; it uses a fake path string and asserts `_temporaryFileAccessor.Verify(x => x.DeleteIfExists(exportedFile), Times.Once)` — satisfying spec FR-5's "at least one test asserts `DeleteIfExists` is invoked once per file in `ExportedFiles`" and the arch-review's risk-table item calling this test out by name.
- Added new test `PrintPickingListAsync_EmailAttachments_BuiltFromAccessorBytes`, satisfying spec FR-5's "at least one test asserts email attachments are built from bytes returned by the mocked `ReadAllBytesAsync`". It stubs `ReadAllBytesAsync` to return known bytes, captures the outgoing `EmailMessage` via `_emailSender`'s callback, and asserts the attachment's `Content` is the base64 of those exact bytes, with `FileName` derived correctly via `Path.GetFileName` and `ContentType` set to `"application/pdf"`. This requires `EmailMessage`/`EmailAttachment` to have `Attachments` (`IList<EmailAttachment>`), `Content`, `FileName`, `ContentType` properties matching what `ExpeditionListService.SendEmailCopy` already populates (see the current file, lines 96-105) — no changes needed to `EmailMessage`/`EmailAttachment` themselves, only reuse of existing shapes.
- All four pre-existing tests other than `PrintPickingListAsync_CleanupRunsAfterSuccess` are otherwise textually unchanged.

#### Acceptance criteria

- `dotnet build` succeeds for the whole solution.
- `dotnet format` produces no diff (or is applied) for all files touched.
- `grep -n "File\." backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` (or equivalent search) returns **zero** matches for `File.Exists`, `File.Delete`, or `File.ReadAllBytesAsync` — confirming FR-4's "no `File.*` calls" acceptance criterion. (`Path.GetFileName` is expected to remain and is not an I/O call.)
- All tests in `ExpeditionListServicePrintSinkTests.cs` pass (2 tests, unchanged assertions): `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ExpeditionListServicePrintSinkTests`.
- All tests in `ExpeditionListServiceOrderStateTests.cs` pass (6 tests: the original 5 plus the new `PrintPickingListAsync_EmailAttachments_BuiltFromAccessorBytes`): `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ExpeditionListServiceOrderStateTests`.
- `PrintPickingListAsync_CleanupRunsAfterSuccess` no longer references `Path.GetTempFileName()` or `File.Exists` — confirm by inspection of the diff.
- Full `ExpeditionList` test slice still green: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ExpeditionList` (includes `FileSystemTemporaryFileAccessorTests`, `FileSystemPrintQueueSinkTests`, `PrintExpeditionOrderHandlerTests`, etc. from the prior task and pre-existing suite — none of these should be affected by this task's changes, but must still pass).
- No changes made to `IExpeditionListService.cs`, `PrintExpeditionOrderHandler.cs`, `PrintPickingListJob.cs`, or `RunExpeditionListPrintFixHandler.cs` — confirm via `git diff --stat` that only the five files listed under "File(s) to create/modify" across both tasks are touched.

---

## Post-implementation note (FR-6, informational — no task required)

Per the architecture review's investigation (already completed, no further action needed): `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` exists but is a **namespace-reference boundary checker** (asserts module A's types don't reference module B's forbidden namespaces) — it is not a reflection-based `File.*`/`System.IO` call detector, and no such detector exists elsewhere in the test suite. Adding one is optional and explicitly out of scope for this change (spec FR-6, "Out of Scope"). When writing the PR description, state this finding ("investigated: no existing I/O-call guard exists; `ModuleBoundariesTests.cs` checks namespace references only, not applicable here; not adding a new guard, per spec Out of Scope") to satisfy FR-6's acceptance criterion — this does not require a code task.
