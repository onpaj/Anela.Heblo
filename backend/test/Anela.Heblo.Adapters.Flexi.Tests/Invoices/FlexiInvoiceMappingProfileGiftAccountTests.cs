using Anela.Heblo.Adapters.Flexi.Invoices;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
    public void Map_LeavesDphToAbra_ForGiftProduct()
    {
        // Act
        var flexi = MapSingleItem(GiftProductCode);

        // Assert — DPH classification stays owned by the Abra product config
        flexi.Items[0].CopyCategoryVat.Should().BeTrue();
        flexi.Items[0].CategoryVat.Should().BeNull();
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
