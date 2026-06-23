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

    [Theory]
    [InlineData(Carriers.Zasilkovna, DeliveryHandling.NaRuky, "Zasilkovna", "NaRuky")]
    [InlineData(Carriers.Zasilkovna, DeliveryHandling.Box, "Zasilkovna", "Box")]
    [InlineData(Carriers.PPL, DeliveryHandling.NaRuky, "PPL", "NaRuky")]
    [InlineData(Carriers.PPL, DeliveryHandling.Box, "PPL", "Box")]
    [InlineData(Carriers.GLS, DeliveryHandling.NaRuky, "GLS", "NaRuky")]
    [InlineData(Carriers.GLS, DeliveryHandling.Box, "GLS", "Box")]
    [InlineData(Carriers.Osobak, DeliveryHandling.NaRuky, "Osobak", "NaRuky")]
    [InlineData(Carriers.Osobak, DeliveryHandling.Box, "Osobak", "Box")]
    public async Task GetAllAsync_MapsEveryCarrierAndHandlingMemberToExpectedStringKey(
        Carriers carrier, DeliveryHandling handling, string expectedCarrierName, string expectedHandlingName)
    {
        // Pins the string contract shared with ShoptetApiExpeditionListSource.ResolveCarrierCooling.
        // A rename of any Carriers/DeliveryHandling member breaks the runtime lookup; this test fails it at CI time.
        var repo = new Mock<ICarrierCoolingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CarrierCoolingSetting(carrier, handling, Cooling.L1, "test") });
        var sut = new CarrierCoolingPackingCarrierCoolingAdapter(repo.Object);

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle();
        result[0].CarrierName.Should().Be(expectedCarrierName);
        result[0].DeliveryHandlingName.Should().Be(expectedHandlingName);
    }

    [Fact]
    public async Task GetAllAsync_MapsEverySetting_WhenRepositoryReturnsMultiple()
    {
        // Guards against a regression that maps only the first item (e.g. accidental FirstOrDefault).
        var repo = new Mock<ICarrierCoolingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CarrierCoolingSetting(Carriers.PPL, DeliveryHandling.NaRuky, Cooling.L1, "test"),
                new CarrierCoolingSetting(Carriers.GLS, DeliveryHandling.Box, Cooling.L2, "test"),
                new CarrierCoolingSetting(Carriers.Zasilkovna, DeliveryHandling.NaRuky, Cooling.None, "test"),
            });
        var sut = new CarrierCoolingPackingCarrierCoolingAdapter(repo.Object);

        var result = await sut.GetAllAsync();

        result.Should().BeEquivalentTo(new[]
        {
            new PackingCarrierCoolingSetting { CarrierName = "PPL", DeliveryHandlingName = "NaRuky", Cooling = Cooling.L1 },
            new PackingCarrierCoolingSetting { CarrierName = "GLS", DeliveryHandlingName = "Box", Cooling = Cooling.L2 },
            new PackingCarrierCoolingSetting { CarrierName = "Zasilkovna", DeliveryHandlingName = "NaRuky", Cooling = Cooling.None },
        });
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
