using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLotLabelCalibration;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class GetLotLabelCalibrationHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsPersistedPitch_WithBounds()
    {
        var repo = new Mock<ILotLabelCalibrationRepository>();
        repo.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LotLabelCalibration(152, 3, "admin"));

        var sut = new GetLotLabelCalibrationHandler(repo.Object);

        var result = await sut.Handle(new GetLotLabelCalibrationRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PitchDots.Should().Be(152);
        result.MinPitchDots.Should().Be(LotLabelCalibration.MinPitchDots);
        result.MaxPitchDots.Should().Be(LotLabelCalibration.MaxPitchDots);
    }

    [Fact]
    public async Task Handle_ReturnsDefault_WhenNoRowPersisted()
    {
        var repo = new Mock<ILotLabelCalibrationRepository>();
        repo.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LotLabelCalibration.CreateDefault());

        var sut = new GetLotLabelCalibrationHandler(repo.Object);

        var result = await sut.Handle(new GetLotLabelCalibrationRequest(), CancellationToken.None);

        result.PitchDots.Should().Be(LotLabelCalibration.DefaultPitchDots);
    }
}
