using Anela.Heblo.Adapters.Flexi.Invoices;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rem.FlexiBeeSDK.Model.Invoices;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Invoices;

public class FlexiInvoiceMappingProfileGiftAccountTests
{
    private const string GiftProductCode = "GOODYDO0001";

    private static IssuedInvoiceDetailFlexiDto MapSingleItem(string itemCode)
    {
        var config = new MapperConfiguration(
            c => c.AddProfile<FlexiInvoiceMappingProfile>(),
            NullLoggerFactory.Instance);
        var mapper = config.CreateMapper();

        var domain = new IssuedInvoiceDetail
        {
            Code = "INV1",
            OrderCode = "ORD1",
            Customer = new InvoiceCustomer { Name = "Test" },
            BillingAddress = new InvoiceAddress { Street = "S", City = "C", CountryCode = "CZ" },
            DeliveryAddress = new InvoiceAddress { Street = "S", City = "C", CountryCode = "CZ" },
            Price = new InvoicePrice { CurrencyCode = "CZK", WithoutVat = 100m, WithVat = 112m, Vat = 12m },
            Items = new List<IssuedInvoiceDetailItem>
            {
                new()
                {
                    Code = itemCode,
                    Name = "Item",
                    Amount = 1m,
                    ItemPrice = new InvoicePrice
                    {
                        CurrencyCode = "CZK",
                        WithoutVat = 100m,
                        WithVat = 112m,
                        Vat = 12m,
                        TotalWithoutVat = 100m,
                        TotalWithVat = 112m,
                        VatRate = "high",
                    },
                }
            },
        };

        return mapper.Map<IssuedInvoiceDetailFlexiDto>(domain);
    }

    [Fact]
    public void Map_SetsBaseCreditAccount_ForGiftProduct()
    {
        // Act
        var flexi = MapSingleItem(GiftProductCode);

        // Assert
        flexi.Items.Should().HaveCount(1);
        flexi.Items[0].AccountBaseDal.Should().Be("code:325002");
    }

    [Fact]
    public void Map_SetsVatCategoryExplicitly_ForGiftProduct()
    {
        // Act
        var flexi = MapSingleItem(GiftProductCode);

        // Assert — an explicit clenDph must not be overwritten by the price-list copy
        flexi.Items[0].CategoryVat.Should().Be("code:000U");
        flexi.Items[0].CopyCategoryVat.Should().BeFalse();
    }

    [Fact]
    public void Map_CopiesVatCategoryFromPriceList_ForNonGiftProduct()
    {
        // Act
        var flexi = MapSingleItem("P1");

        // Assert
        flexi.Items[0].CategoryVat.Should().BeNull();
        flexi.Items[0].CopyCategoryVat.Should().BeTrue();
    }

    [Fact]
    public void Serialize_EmitsClenDphAndKopClenDph_ForGiftProduct()
    {
        // Arrange
        var flexi = MapSingleItem(GiftProductCode);

        // Act
        var item = JObject.Parse(JsonConvert.SerializeObject(flexi.Items[0]));

        // Assert
        item["clenDph"]!.Value<string>().Should().Be("code:000U");
        item["kopClenDph"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public void Serialize_CopiesVatCategoryWithoutClenDph_ForNonGiftProduct()
    {
        // Arrange
        var flexi = MapSingleItem("P1");

        // Act
        var item = JObject.Parse(JsonConvert.SerializeObject(flexi.Items[0]));

        // Assert
        item["kopClenDph"]!.Value<bool>().Should().BeTrue();
        (item["clenDph"]?.Type ?? JTokenType.Null).Should().Be(JTokenType.Null);
    }

    [Fact]
    public void Map_DoesNotSetBaseCreditAccount_ForNonGiftProduct()
    {
        // Act
        var flexi = MapSingleItem("P1");

        // Assert
        flexi.Items[0].AccountBaseDal.Should().BeNull();
    }
}
