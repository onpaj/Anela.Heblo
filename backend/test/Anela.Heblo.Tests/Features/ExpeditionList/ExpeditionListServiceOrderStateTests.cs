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

    private ExpeditionListService CreateService() => new ExpeditionListService(
        _pickingSource.Object,
        _emailSender.Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        _printQueueSink.Object,
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
        var tmpFile = Path.GetTempFileName();
        _pickingSource
            .Setup(x => x.CreatePickingListAsync(
                It.IsAny<ExpeditionPickingRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionPickingResult
            {
                ExportedFiles = new[] { tmpFile },
                TotalCount = 1,
            });

        var request = new ExpeditionPickingRequest { SendToPrinter = false };
        var svc = CreateService();

        await svc.PrintPickingListAsync(request);

        Assert.False(File.Exists(tmpFile));
    }
}
