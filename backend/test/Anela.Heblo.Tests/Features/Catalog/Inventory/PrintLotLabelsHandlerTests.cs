using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotLabels;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class PrintLotLabelsHandlerTests
{
    [Fact]
    public async Task Handle_BuildsZplWithBothLines_AndCalibratedPitch_AndPrintsRequestedCount()
    {
        var label = new Mock<ILabelPrintingService>();
        label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var calibration = new Mock<ILotLabelCalibrationRepository>();
        calibration.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LotLabelCalibration(152, "admin"));

        var sut = new PrintLotLabelsHandler(
            NullLogger<PrintLotLabelsHandler>.Instance, label.Object, calibration.Object);

        var request = new PrintLotLabelsRequest { LotNumber = "2926", Expiration = "07/29", Count = 2 };

        var result = await sut.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.LotNumber.Should().Be("2926");
        result.Expiration.Should().Be("07/29");
        result.Count.Should().Be(2);
        label.Verify(l => l.PrintZplAsync(
            It.Is<string>(z =>
                z.Contains("^FD2926^FS") &&
                z.Contains("^FD07/29^FS") &&
                z.Contains("^LL152") &&   // uses the persisted calibration pitch
                System.Text.RegularExpressions.Regex.Matches(z, "\\^XA").Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
