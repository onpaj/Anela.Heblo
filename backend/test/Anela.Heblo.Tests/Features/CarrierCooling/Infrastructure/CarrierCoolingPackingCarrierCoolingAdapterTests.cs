using Anela.Heblo.Application.Features.CarrierCooling.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.CarrierCooling.Infrastructure;

public class CarrierCoolingPackingCarrierCoolingAdapterTests
{
    [Fact]
    public async Task GetAllAsync_MapsCarrierNameAsEnumString()
    {
        var repo = new Mock<ICarrierCoolingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CarrierCoolingSetting(Carriers.PPL, DeliveryHandling.NaRuky, Cooling.L1, "test") });
        var sut = new CarrierCoolingPackingCarrierCoolingAdapter(repo.Object);

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle();
        result[0].CarrierName.Should().Be("PPL");
        result[0].DeliveryHandlingName.Should().Be("NaRuky");
        result[0].Cooling.Should().Be(Cooling.L1);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyListWhenRepositoryEmpty()
    {
        var repo = new Mock<ICarrierCoolingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CarrierCoolingSetting>());
        var sut = new CarrierCoolingPackingCarrierCoolingAdapter(repo.Object);

        var result = await sut.GetAllAsync();

        result.Should().BeEmpty();
    }
}
