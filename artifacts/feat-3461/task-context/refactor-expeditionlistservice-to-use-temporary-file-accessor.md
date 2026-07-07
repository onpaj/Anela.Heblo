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
