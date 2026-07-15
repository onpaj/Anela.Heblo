using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotCalibrationLabel;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class PrintLotCalibrationLabelHandlerTests
{
    [Fact]
    public async Task Handle_PrintsCrosshairZpl_WithCalibratedPitch()
    {
        var label = new Mock<ILabelPrintingService>();
        label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var calibration = new Mock<ILotLabelCalibrationRepository>();
        calibration.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LotLabelCalibration(152, "admin"));

        var sut = new PrintLotCalibrationLabelHandler(
            NullLogger<PrintLotCalibrationLabelHandler>.Instance, label.Object, calibration.Object);

        var result = await sut.Handle(new PrintLotCalibrationLabelRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        label.Verify(l => l.PrintZplAsync(
            It.Is<string>(z => z.Contains("^GB") && z.Contains("^LL152") && !z.Contains("^FD")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
