using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.IssuedInvoices;

public class ShoptetInvoiceItemDtoTests
{
    [Fact]
    public void Deserializes_ItemType_And_PriceRatio()
    {
        const string json = """
        {
          "code": "ABC",
          "name": "Sample",
          "amount": "2.00",
          "itemType": "discount-coupon",
          "priceRatio": 0.78,
          "unitPrice": { "withVat": "100.00", "withoutVat": "82.64", "vat": "17.36", "vatRate": "21.0" },
          "itemPrice": { "withVat": "200.00", "withoutVat": "165.29", "vat": "34.71", "vatRate": "21.0" }
        }
        """;

        var dto = JsonSerializer.Deserialize<ShoptetInvoiceItemDto>(json)!;

        dto.ItemType.Should().Be("discount-coupon");
        dto.PriceRatio.Should().Be(0.78m);
    }

    [Fact]
    public void Deserializes_PriceRatioZero_AsFreeItem()
    {
        const string json = """
        {
          "code": "TON002030",
          "name": "Product",
          "amount": "1.00",
          "itemType": "product",
          "priceRatio": 0.0000,
          "unitPrice": { "withVat": "180.00", "withoutVat": "148.76", "vat": "31.24", "vatRate": "21.0" },
          "itemPrice": { "withVat": "0.00", "withoutVat": "0.00", "vat": "0.00", "vatRate": "21.0" }
        }
        """;

        var dto = JsonSerializer.Deserialize<ShoptetInvoiceItemDto>(json)!;

        dto.ItemType.Should().Be("product");
        dto.PriceRatio.Should().Be(0.0000m);
    }

    [Fact]
    public void Deserializes_Omitted_ItemType_And_PriceRatio_As_Null()
    {
        const string json = """{ "code": "X", "name": "Y", "amount": "1.00" }""";

        var dto = JsonSerializer.Deserialize<ShoptetInvoiceItemDto>(json)!;

        dto.ItemType.Should().BeNull();
        dto.PriceRatio.Should().BeNull();
    }
}
