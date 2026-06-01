using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShippingMethodCatalogTests
{
    private readonly ShippingMethodCatalog _sut = new();

    [Fact]
    public void GetAvailableDeliveryOptions_ReturnsExactlySixDistinctPairs()
    {
        var result = _sut.GetAvailableDeliveryOptions();

        result.Should().HaveCount(6);
        result.Should().Contain((Carriers.Zasilkovna, DeliveryHandling.NaRuky));
        result.Should().Contain((Carriers.Zasilkovna, DeliveryHandling.Box));
        result.Should().Contain((Carriers.PPL, DeliveryHandling.NaRuky));
        result.Should().Contain((Carriers.PPL, DeliveryHandling.Box));
        result.Should().Contain((Carriers.GLS, DeliveryHandling.NaRuky));
        result.Should().Contain((Carriers.GLS, DeliveryHandling.Box));
    }

    [Fact]
    public void GetAvailableDeliveryOptions_ExcludesOsobak()
    {
        var result = _sut.GetAvailableDeliveryOptions();
        result.Select(x => x.Carrier).Should().NotContain(Carriers.Osobak);
    }

    [Fact]
    public void GetAvailableDeliveryOptions_ExcludesExportMethods()
    {
        var result = _sut.GetAvailableDeliveryOptions();
        var pplHandlings = result.Where(x => x.Carrier == Carriers.PPL).Select(x => x.Handling).ToList();
        pplHandlings.Should().HaveCount(2);
        pplHandlings.Should().Contain(DeliveryHandling.NaRuky);
        pplHandlings.Should().Contain(DeliveryHandling.Box);
    }

    [Fact]
    public void GetAvailableDeliveryOptions_ClassifiesDoRukyAsNaRuky()
    {
        var result = _sut.GetAvailableDeliveryOptions();
        result.Should().Contain((Carriers.Zasilkovna, DeliveryHandling.NaRuky));
    }

    [Fact]
    public void GetAvailableDeliveryOptions_ClassifiesZpointAndParcelshopAsBox()
    {
        var result = _sut.GetAvailableDeliveryOptions();
        result.Should().Contain((Carriers.Zasilkovna, DeliveryHandling.Box));
        result.Should().Contain((Carriers.PPL, DeliveryHandling.Box));
        result.Should().Contain((Carriers.GLS, DeliveryHandling.Box));
    }
}
