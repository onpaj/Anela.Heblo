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
}
