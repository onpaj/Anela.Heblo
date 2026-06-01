using Anela.Heblo.Adapters.Cups;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class CupsLabelPrintingServiceTests
{
    [Fact]
    public async Task PrintZplAsync_WritesTempFile_SendsRawToLabelPrinter_ThenDeletes()
    {
        var cups = new Mock<ICupsPrintingService>();
        string? capturedPath = null;
        cups.Setup(c => c.PrintAsync(It.IsAny<string>(), "Zebra-Raw", "application/octet-stream", It.IsAny<CancellationToken>()))
            .Callback<string, string?, string, CancellationToken>((p, _, _, _) =>
            {
                capturedPath = p;
                File.Exists(p).Should().BeTrue();
            })
            .Returns(Task.CompletedTask);

        var options = Options.Create(new CupsOptions { LabelPrinterName = "Zebra-Raw" });
        var sut = new CupsLabelPrintingService(cups.Object, options, NullLogger<CupsLabelPrintingService>.Instance);

        await sut.PrintZplAsync("^XA^XZ");

        cups.VerifyAll();
        File.Exists(capturedPath!).Should().BeFalse(); // temp cleaned up
    }

    [Fact]
    public async Task PrintZplAsync_DeletesTempFile_WhenPrintThrows()
    {
        var cups = new Mock<ICupsPrintingService>();
        string? capturedPath = null;
        cups.Setup(c => c.PrintAsync(It.IsAny<string>(), "Zebra-Raw", "application/octet-stream", It.IsAny<CancellationToken>()))
            .Callback<string, string?, string, CancellationToken>((p, _, _, _) =>
            {
                capturedPath = p;
                File.Exists(p).Should().BeTrue();
            })
            .ThrowsAsync(new InvalidOperationException("boom"));

        var options = Options.Create(new CupsOptions { LabelPrinterName = "Zebra-Raw" });
        var sut = new CupsLabelPrintingService(cups.Object, options, NullLogger<CupsLabelPrintingService>.Instance);

        var act = () => sut.PrintZplAsync("^XA^XZ");

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(capturedPath!).Should().BeFalse(); // finally cleanup runs on exception
    }

    [Fact]
    public async Task PrintZplAsync_Throws_WhenLabelPrinterNameBlank()
    {
        var cups = new Mock<ICupsPrintingService>();

        var options = Options.Create(new CupsOptions { LabelPrinterName = "" });
        var sut = new CupsLabelPrintingService(cups.Object, options, NullLogger<CupsLabelPrintingService>.Instance);

        var act = () => sut.PrintZplAsync("^XA^XZ");

        await act.Should().ThrowAsync<InvalidOperationException>();
        cups.Verify(c => c.PrintAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
