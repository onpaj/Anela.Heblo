using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CarrierCooling;

public class SetCarrierCoolingHandlerTests
{
    private readonly Mock<ICarrierCoolingRepository> _repositoryMock = new();
    private readonly Mock<IShippingMethodCatalog> _catalogMock = new();

    private SetCarrierCoolingHandler CreateSut() =>
        new(_repositoryMock.Object, _catalogMock.Object);

    private void SetupValidCombo(Carriers carrier, DeliveryHandling handling)
    {
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions())
            .Returns(new List<(Carriers Carrier, DeliveryHandling Handling)> { (carrier, handling) }.AsReadOnly());
    }

    [Fact]
    public async Task Handle_CallsUpsertAndReturnsSuccess_WhenComboIsAvailable()
    {
        SetupValidCombo(Carriers.PPL, DeliveryHandling.NaRuky);
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<CarrierCoolingSetting>(s =>
                    s.Carrier == Carriers.PPL &&
                    s.DeliveryHandling == DeliveryHandling.NaRuky &&
                    s.Cooling == Cooling.L1 &&
                    s.ModifiedBy == "user-123"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PersistsCoolingText_WhenProvided()
    {
        SetupValidCombo(Carriers.PPL, DeliveryHandling.NaRuky);
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            CoolingText = "MRAZ",
            ModifiedBy = "user-123",
        };
        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<CarrierCoolingSetting>(s => s.CoolingText == "MRAZ"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenComboIsUnavailable()
    {
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions())
            .Returns(new List<(Carriers Carrier, DeliveryHandling Handling)>().AsReadOnly());

        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.Osobak,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        var result = await CreateSut().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _repositoryMock.Verify(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
