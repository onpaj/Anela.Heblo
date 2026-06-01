using Anela.Heblo.Adapters.Cups;
using Anela.Heblo.Adapters.Cups.Features.ExpeditionList;
using Moq;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class CupsPrintQueueSinkTests
{
    private readonly Mock<ICupsPrintingService> _printingService = new();

    private CupsPrintQueueSink CreateSink() =>
        new CupsPrintQueueSink(_printingService.Object);

    [Fact]
    public async Task SendAsync_MultipleFiles_CallsPrintAsyncForEachFile()
    {
        // Arrange
        var files = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" };
        _printingService
            .Setup(x => x.PrintAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sink = CreateSink();

        // Act
        await sink.SendAsync(files);

        // Assert
        _printingService.Verify(
            x => x.PrintAsync("/tmp/a.pdf", null, It.IsAny<CancellationToken>()),
            Times.Once);
        _printingService.Verify(
            x => x.PrintAsync("/tmp/b.pdf", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_PassesNullPrinterName_UsesConfiguredDefault()
    {
        // Arrange: verify printerName is never explicitly set (always null)
        var files = new List<string> { "/tmp/order.pdf" };
        string? capturedPrinterName = "sentinel"; // non-null sentinel

        _printingService
            .Setup(x => x.PrintAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, pn, _) => capturedPrinterName = pn)
            .Returns(Task.CompletedTask);

        var sink = CreateSink();

        // Act
        await sink.SendAsync(files);

        // Assert
        Assert.Null(capturedPrinterName);
    }

    [Fact]
    public async Task SendAsync_EmptyList_DoesNotCallPrintAsync()
    {
        // Arrange
        var sink = CreateSink();

        // Act
        await sink.SendAsync([]);

        // Assert
        _printingService.Verify(
            x => x.PrintAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
