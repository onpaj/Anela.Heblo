using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotCalibrationLabel;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class PrintLotCalibrationLabelHandlerTests
{
    private static Mock<ICurrentUserService> UserMock()
    {
        var user = new Mock<ICurrentUserService>();
        user.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser(Id: "1", Name: "admin", Email: "admin@example.com", IsAuthenticated: true));
        return user;
    }

    private static Mock<ILotLabelCalibrationRepository> CalibrationMock()
    {
        var calibration = new Mock<ILotLabelCalibrationRepository>();
        calibration.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LotLabelCalibration(152, 3, "admin"));
        return calibration;
    }

    [Fact]
    public async Task Handle_PrintsCrosshairZpl_WithCalibratedPitch()
    {
        var label = new Mock<ILabelPrintingService>();
        label.Setup(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var media = new Mock<IPrinterMediaStateRepository>();
        media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrinterMediaState.CreateDefault());

        var sut = new PrintLotCalibrationLabelHandler(
            NullLogger<PrintLotCalibrationLabelHandler>.Instance, label.Object, CalibrationMock().Object, media.Object, UserMock().Object);

        var result = await sut.Handle(new PrintLotCalibrationLabelRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RequiresMediaChangeConfirmation.Should().BeFalse();
        label.Verify(l => l.PrintZplAsync(
            It.Is<string>(z => z.Contains("^GB") && z.Contains("^LL152") && !z.Contains("^FD")),
            It.IsAny<CancellationToken>()), Times.Once);
        media.Verify(m => m.SaveAsync(
            It.Is<PrinterMediaState>(s => s.LastMediaType == LabelMediaType.LotRound),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RequiresConfirmation_WhenSwitchingMedia_AndNotConfirmed_DoesNotPrint()
    {
        var label = new Mock<ILabelPrintingService>();

        var previous = PrinterMediaState.CreateDefault();
        previous.RecordPrint(LabelMediaType.MaterialContainer, "admin");
        var media = new Mock<IPrinterMediaStateRepository>();
        media.Setup(m => m.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(previous);

        var sut = new PrintLotCalibrationLabelHandler(
            NullLogger<PrintLotCalibrationLabelHandler>.Instance, label.Object, CalibrationMock().Object, media.Object, UserMock().Object);

        var result = await sut.Handle(new PrintLotCalibrationLabelRequest(), CancellationToken.None);

        result.RequiresMediaChangeConfirmation.Should().BeTrue();
        label.Verify(l => l.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        media.Verify(m => m.SaveAsync(It.IsAny<PrinterMediaState>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
