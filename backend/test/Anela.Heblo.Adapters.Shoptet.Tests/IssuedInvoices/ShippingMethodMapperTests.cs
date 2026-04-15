using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Shoptet.Tests.IssuedInvoices;

public class ShippingMethodMapperTests
{
    private static ShippingMethodMapper BuildSut(Dictionary<string, ShippingMethod>? map = null)
    {
        var settings = new ShoptetApiSettings
        {
            InvoiceShippingGuidMap = map ?? new Dictionary<string, ShippingMethod>
            {
                ["11111111-0000-0000-0000-000000000001"] = ShippingMethod.PPL,
                ["11111111-0000-0000-0000-000000000002"] = ShippingMethod.Zasilkovna,
                ["11111111-0000-0000-0000-000000000003"] = ShippingMethod.PickUp,
            }
        };
        return new ShippingMethodMapper(Options.Create(settings));
    }

    [Fact]
    public void Map_KnownGuid_ReturnsCorrectShippingMethod()
    {
        var sut = BuildSut();
        var shipping = new ShoptetInvoiceShippingDto { Guid = "11111111-0000-0000-0000-000000000001" };
        sut.Map(shipping).Should().Be(ShippingMethod.PPL);
    }

    [Fact]
    public void Map_NullShipping_ReturnsPickUpDefault()
    {
        var sut = BuildSut();
        sut.Map(null).Should().Be(ShippingMethod.PickUp,
            "Default matches Playwright's ShippingMethodResolver fallback");
    }

    [Fact]
    public void Map_ShippingWithNullGuid_ReturnsPickUpDefault()
    {
        var sut = BuildSut();
        var shipping = new ShoptetInvoiceShippingDto { Guid = null };
        sut.Map(shipping).Should().Be(ShippingMethod.PickUp);
    }

    [Fact]
    public void Map_UnknownGuid_ReturnsPickUpDefault()
    {
        var sut = BuildSut();
        var shipping = new ShoptetInvoiceShippingDto { Guid = "99999999-9999-9999-9999-999999999999" };
        sut.Map(shipping).Should().Be(ShippingMethod.PickUp);
    }

    [Theory]
    [InlineData("11111111-0000-0000-0000-000000000001", ShippingMethod.PPL)]
    [InlineData("11111111-0000-0000-0000-000000000002", ShippingMethod.Zasilkovna)]
    [InlineData("11111111-0000-0000-0000-000000000003", ShippingMethod.PickUp)]
    public void Map_MultipleKnownGuids_ReturnCorrectMethods(string guid, ShippingMethod expected)
    {
        var sut = BuildSut();
        var shipping = new ShoptetInvoiceShippingDto { Guid = guid };
        sut.Map(shipping).Should().Be(expected);
    }
}
