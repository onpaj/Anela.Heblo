using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class ExpeditionListServiceOrderStateTests
{
    private readonly Mock<IPickingListSource> _pickingListSource = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IPrintQueueSink> _printQueueSink = new();

    private ExpeditionListService CreateService() => new ExpeditionListService(
        _pickingListSource.Object,
        _emailSender.Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        _printQueueSink.Object,
        NullLogger<ExpeditionListService>.Instance);

    /// <summary>
    /// Sets up the source mock to invoke the callback with the given files, simulating
    /// per-batch processing inside PrintPickingListScenario.
    /// </summary>
    private void SetupSourceInvokingCallback(IList<string> filesToPassToCallback)
    {
        _pickingListSource
            .Setup(x => x.CreatePickingList(
                It.IsAny<PrintPickingListRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                async (PrintPickingListRequest req, Func<IList<string>, Task>? cb, CancellationToken ct) =>
                {
                    if (cb != null)
                        await cb(filesToPassToCallback);
                    return new PrintPickingListResult { ExportedFiles = new List<string>(), TotalCount = 1 };
                });
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenEmailThrows_ExceptionPropagates()
    {
        // Arrange — callback invokes email, email throws
        SetupSourceInvokingCallback(new List<string>());
        _emailSender
            .Setup(x => x.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP failure"));

        var request = new PrintPickingListRequest { ChangeOrderState = true, SendToPrinter = false };
        var svc = CreateService();

        // Act & Assert — the exception must NOT be silently swallowed
        await Assert.ThrowsAsync<Exception>(() =>
            svc.PrintPickingListAsync(request, emailList: new[] { "user@example.com" }));
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenPrinterThrows_ExceptionPropagates()
    {
        // Arrange — callback invokes printer, printer throws
        SetupSourceInvokingCallback(new List<string>());
        _printQueueSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Print queue failure"));

        var request = new PrintPickingListRequest { ChangeOrderState = true, SendToPrinter = true };
        var svc = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => svc.PrintPickingListAsync(request));
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenAllSucceed_PrinterCalledBeforeEmail()
    {
        // Arrange
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

        var request = new PrintPickingListRequest { ChangeOrderState = true, SendToPrinter = true };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request, emailList: new[] { "user@example.com" });

        // Assert
        Assert.Equal(new[] { "printer", "email" }, callOrder);
    }

    [Fact]
    public async Task PrintPickingListAsync_WhenNeitherPrinterNorEmail_NullCallbackPassedToSource()
    {
        // Arrange
        Func<IList<string>, Task>? capturedCallback = null;
        _pickingListSource
            .Setup(x => x.CreatePickingList(
                It.IsAny<PrintPickingListRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback(
                (PrintPickingListRequest req, Func<IList<string>, Task>? cb, CancellationToken ct) =>
                    capturedCallback = cb)
            .ReturnsAsync(new PrintPickingListResult { ExportedFiles = new List<string>() });

        var request = new PrintPickingListRequest { SendToPrinter = false };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request, emailList: null);

        // Assert — no callback built when there's nothing to do per batch
        Assert.Null(capturedCallback);
    }

    [Fact]
    public async Task PrintPickingListAsync_CleanupRunsAfterSuccess()
    {
        // Arrange — real temp file so cleanup can verify deletion
        var tmpFile = Path.GetTempFileName();
        _pickingListSource
            .Setup(x => x.CreatePickingList(
                It.IsAny<PrintPickingListRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintPickingListResult
            {
                ExportedFiles = new[] { tmpFile },
                TotalCount = 1,
            });

        var request = new PrintPickingListRequest { SendToPrinter = false };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request);

        // Assert
        Assert.False(File.Exists(tmpFile));
    }
}
