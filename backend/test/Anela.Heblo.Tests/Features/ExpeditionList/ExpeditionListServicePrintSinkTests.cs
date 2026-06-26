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
