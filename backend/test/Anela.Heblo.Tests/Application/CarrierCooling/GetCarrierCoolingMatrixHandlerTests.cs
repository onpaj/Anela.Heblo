using Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CarrierCooling;

public class GetCarrierCoolingMatrixHandlerTests
{
    private readonly Mock<ICarrierCoolingRepository> _repositoryMock = new();
    private readonly Mock<IShippingMethodCatalog> _catalogMock = new();
    private readonly GetCarrierCoolingMatrixHandler _sut;

    public GetCarrierCoolingMatrixHandlerTests()
    {
        _sut = new GetCarrierCoolingMatrixHandler(_repositoryMock.Object, _catalogMock.Object);
    }

    [Fact]
    public async Task Handle_DefaultsCoolingToNone_WhenNoStoredSetting()
    {
        // Arrange
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)>
            {
                (Carriers.Zasilkovna, DeliveryHandling.NaRuky),
            }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>());

        // Act
        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        // Assert
        result.Groups.Should().HaveCount(1);
        result.Groups[0].Carrier.Should().Be(Carriers.Zasilkovna);
        result.Groups[0].Rows.Should().HaveCount(1);
        result.Groups[0].Rows[0].DeliveryHandling.Should().Be(DeliveryHandling.NaRuky);
        result.Groups[0].Rows[0].Cooling.Should().Be(Cooling.None);
    }

    [Fact]
    public async Task Handle_UsesStoredCooling_WhenSettingExists()
    {
        // Arrange
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)>
            {
                (Carriers.PPL, DeliveryHandling.Box),
            }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>
            {
                new(Carriers.PPL, DeliveryHandling.Box, Cooling.L1, "user1"),
            });

        // Act
        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        // Assert
        result.Groups[0].Rows[0].Cooling.Should().Be(Cooling.L1);
    }

    [Fact]
    public async Task Handle_GroupsByCarrier()
    {
        // Arrange
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)>
            {
                (Carriers.GLS, DeliveryHandling.NaRuky),
                (Carriers.GLS, DeliveryHandling.Box),
            }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>());

        // Act
        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        // Assert
        result.Groups.Should().HaveCount(1);
        result.Groups[0].Carrier.Should().Be(Carriers.GLS);
        result.Groups[0].Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_IgnoresStoredSettingsNotInCatalog()
    {
        // Arrange — catalog has only PPL/NaRuky, but DB has a stale Osobak entry
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)>
            {
                (Carriers.PPL, DeliveryHandling.NaRuky),
            }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>
            {
                new(Carriers.Osobak, DeliveryHandling.NaRuky, Cooling.L2, "user1"),
            });

        // Act
        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        // Assert — only the PPL row appears, Osobak is not rendered
        result.Groups.Should().HaveCount(1);
        result.Groups[0].Carrier.Should().Be(Carriers.PPL);
    }
}
