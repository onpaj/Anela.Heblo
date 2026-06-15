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

    [Fact]
    public void GetShippingCodesForCarrier_Ppl_ReturnsAllPplShippingIds()
    {
        var result = _sut.GetShippingCodesForCarrier(Carriers.PPL);

        // Legacy methods plus the 2025+ scheme (PPL box / výdejní místa / do ruky).
        result.Should().BeEquivalentTo(new[] { "6", "80", "86", "358", "361", "379", "490", "496", "493" });
    }

    [Fact]
    public void GetShippingCodesForCarrier_Zasilkovna_IncludesNewSchemeIds()
    {
        var result = _sut.GetShippingCodesForCarrier(Carriers.Zasilkovna);

        result.Should().Contain(new[] { "502", "505" });
    }

    [Fact]
    public void GetShippingCodesForCarrier_Gls_IncludesNewSchemeIds()
    {
        var result = _sut.GetShippingCodesForCarrier(Carriers.GLS);

        result.Should().Contain(new[] { "511", "508" });
    }

    [Theory]
    [InlineData("490", Carriers.PPL)]
    [InlineData("496", Carriers.PPL)]
    [InlineData("493", Carriers.PPL)]
    [InlineData("502", Carriers.Zasilkovna)]
    [InlineData("505", Carriers.Zasilkovna)]
    [InlineData("511", Carriers.GLS)]
    [InlineData("508", Carriers.GLS)]
    public void ResolveCarrier_NewSchemeId_ReturnsCarrier(string code, Carriers expected)
    {
        _sut.ResolveCarrier(code).Should().Be(expected);
    }

    [Fact]
    public void GetShippingCodesForCarrier_Osobak_ReturnsSingleId()
    {
        var result = _sut.GetShippingCodesForCarrier(Carriers.Osobak);

        result.Should().BeEquivalentTo(new[] { "4" });
    }

    [Theory]
    [InlineData("21", Carriers.Zasilkovna)]
    [InlineData("6", Carriers.PPL)]
    [InlineData("97", Carriers.GLS)]
    [InlineData("4", Carriers.Osobak)]
    public void ResolveCarrier_KnownId_ReturnsCarrier(string code, Carriers expected)
    {
        _sut.ResolveCarrier(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("abc")]
    [InlineData("")]
    public void ResolveCarrier_UnknownOrNonNumeric_ReturnsNull(string code)
    {
        _sut.ResolveCarrier(code).Should().BeNull();
    }
}
