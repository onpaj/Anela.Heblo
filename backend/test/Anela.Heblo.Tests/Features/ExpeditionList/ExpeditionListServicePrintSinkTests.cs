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

    [Fact]
    public async Task PrintPickingListAsync_SendToPrinterTrue_CallsSink()
    {
        // Arrange
        var files = new List<string> { "/tmp/order1.pdf" };
        _pickingListSource
            .Setup(x => x.CreatePickingList(It.IsAny<PrintPickingListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintPickingListResult { ExportedFiles = files, TotalCount = 1 });

        var request = new PrintPickingListRequest { SendToPrinter = true };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request);

        // Assert
        _printQueueSink.Verify(x => x.SendAsync(files, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrintPickingListAsync_SendToPrinterFalse_DoesNotCallSink()
    {
        // Arrange
        var files = new List<string> { "/tmp/order1.pdf" };
        _pickingListSource
            .Setup(x => x.CreatePickingList(It.IsAny<PrintPickingListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintPickingListResult { ExportedFiles = files, TotalCount = 1 });

        var request = new PrintPickingListRequest { SendToPrinter = false };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request);

        // Assert
        _printQueueSink.Verify(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
