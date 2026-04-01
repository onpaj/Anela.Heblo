using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class ExpeditionListServicePrintSinkTests
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
    public async Task PrintPickingListAsync_SendToPrinterTrue_CallsSink()
    {
        // Arrange
        var batchFiles = new List<string>();
        SetupSourceInvokingCallback(batchFiles);

        var request = new PrintPickingListRequest { SendToPrinter = true };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request);

        // Assert
        _printQueueSink.Verify(
            x => x.SendAsync(batchFiles, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PrintPickingListAsync_SendToPrinterFalse_DoesNotCallSink()
    {
        // Arrange
        _pickingListSource
            .Setup(x => x.CreatePickingList(
                It.IsAny<PrintPickingListRequest>(),
                It.IsAny<Func<IList<string>, Task>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintPickingListResult { ExportedFiles = new List<string>(), TotalCount = 1 });

        var request = new PrintPickingListRequest { SendToPrinter = false };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request);

        // Assert
        _printQueueSink.Verify(
            x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
