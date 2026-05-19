using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetInvoiceMapperTests
{
    private static ShoptetInvoiceMapper BuildMapper() =>
        new(new BillingMethodMapper(),
            new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    [Theory]
    [InlineData("21.0", "high")]
    [InlineData("12.0", "first")]
    [InlineData("15.0", "first")]
    [InlineData("10.0", "second")]
    [InlineData("0.0", "zero")]
    public void Map_ConvertsShoptetNumericVatRate_ToCanonicalNamedRate(
        string shoptetVatRate, string expectedNamedRate)
    {
        // Arrange
        var src = new ShoptetInvoiceDto
        {
            Code = "INV1",
            Items = new List<ShoptetInvoiceItemDto>
            {
                new()
                {
                    Code = "P1",
                    Name = "Product 1",
                    Amount = "1.00",
                    ItemType = "product",
                    UnitPrice = new ShoptetInvoiceUnitPriceDto
                    {
                        WithoutVat = "100.00",
                        WithVat = shoptetVatRate == "12.0" ? "112.00" : "121.00",
                        Vat = shoptetVatRate == "12.0" ? "12.00" : "21.00",
                        VatRate = shoptetVatRate,
                    },
                },
            },
        };

        // Act
        var result = BuildMapper().Map(src);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].ItemPrice.VatRate.Should().Be(expectedNamedRate);
    }

    [Theory]
    [InlineData(1, "Dobírka", BillingMethod.CoD)]
    [InlineData(2, "Převodem", BillingMethod.BankTransfer)]
    [InlineData(3, "Hotově", BillingMethod.Cash)]
    [InlineData(4, "Kartou", BillingMethod.CreditCard)]
    public void Map_ResolvesBillingMethodByShoptetId(
        int billingMethodId, string billingMethodName, BillingMethod expected)
    {
        // Arrange
        var src = new ShoptetInvoiceDto
        {
            Code = "INV1",
            BillingMethod = new ShoptetBillingMethodDto
            {
                Id = billingMethodId,
                Name = billingMethodName,
            },
            Items = new List<ShoptetInvoiceItemDto>
            {
                new()
                {
                    Code = "P1",
                    Name = "Product 1",
                    Amount = "1.00",
                    ItemType = "product",
                    UnitPrice = new ShoptetInvoiceUnitPriceDto
                    {
                        WithoutVat = "100.00",
                        WithVat = "121.00",
                        Vat = "21.00",
                        VatRate = "21.0",
                    },
                },
            },
        };

        // Act
        var result = BuildMapper().Map(src);

        // Assert
        result.BillingMethod.Should().Be(expected);
    }
}
