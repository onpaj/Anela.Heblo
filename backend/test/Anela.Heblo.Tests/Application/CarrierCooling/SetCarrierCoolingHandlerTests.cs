using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CarrierCooling;

public class SetCarrierCoolingHandlerTests
{
    private readonly Mock<ICarrierCoolingRepository> _repositoryMock = new();
    private readonly SetCarrierCoolingHandler _sut;

    public SetCarrierCoolingHandlerTests()
    {
        _sut = new SetCarrierCoolingHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_CallsUpsertAndReturnsSuccess()
    {
        // Arrange
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

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
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
}
