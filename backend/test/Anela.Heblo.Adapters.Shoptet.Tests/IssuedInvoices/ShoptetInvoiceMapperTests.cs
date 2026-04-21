using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Shoptet.Tests.IssuedInvoices;

public class ShoptetInvoiceMapperTests
{
    private static ShoptetInvoiceMapper BuildSut() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    private static ShoptetInvoiceDto BuildMinimalDto() => new()
    {
        Code = "FAK-2025-001",
        OrderCode = "OBJ-2025-100",
        VarSymbol = 2025100L,
        BillingMethod = new ShoptetBillingMethodDto { Id = 1, Name = "bankTransfer" },
        BillingAddress = new ShoptetInvoiceAddressDto
        {
            FullName = "Jan Novák",
            Company = "Testovací s.r.o.",
            Street = "Náměstí 1",
            City = "Praha",
            Zip = "11000",
            CountryCode = "CZ",
            CompanyId = "12345678",
            VatId = "CZ12345678",
        },
        Items = new List<ShoptetInvoiceItemDto>
        {
            new()
            {
                Code = "AKL001",
                Name = "Bisabolol",
                Amount = "2",
                UnitPrice = new ShoptetInvoiceUnitPriceDto
                {
                    WithoutVat = "100",
                    Vat = "21",
                    WithVat = "121",
                    VatRate = "21",
                },
            }
        },
        Price = new ShoptetInvoicePriceDto
        {
            WithVat = "242",
            WithoutVat = "200",
            CurrencyCode = "CZK",
            ExchangeRate = null,
        },
    };

    [Fact]
    public void Map_CodeUsesOrderCode_NotInvoiceCode()
    {
        var detail = BuildSut().Map(BuildMinimalDto());
        detail.Code.Should().Be("OBJ-2025-100",
            "Playwright uses NumberOrder (orderCode) as Code, not the invoice number");
    }

    [Fact]
    public void Map_VarSymbol_PassedThroughAsLong()
    {
        var detail = BuildSut().Map(BuildMinimalDto());
        detail.VarSymbol.Should().Be(2025100L);
    }

    [Fact]
    public void Map_VatPayer_TrueWhenVatIdPresent()
    {
        var detail = BuildSut().Map(BuildMinimalDto());
        detail.VatPayer.Should().BeTrue();
    }

    [Fact]
    public void Map_VatPayer_FalseWhenVatIdAbsent()
    {
        var dto = BuildMinimalDto();
        dto.BillingAddress!.VatId = null;
        BuildSut().Map(dto).VatPayer.Should().BeFalse();
    }

    [Fact]
    public void Map_DeliveryAddress_FallsBackToBillingAddressWhenNull()
    {
        var dto = BuildMinimalDto();
        dto.DeliveryAddress = null;
        var detail = BuildSut().Map(dto);
        detail.DeliveryAddress.Should().BeEquivalentTo(detail.BillingAddress);
    }

    [Fact]
    public void Map_InvoicePrice_ParsedFromStringFields()
    {
        var detail = BuildSut().Map(BuildMinimalDto());
        detail.Price.WithVat.Should().Be(242m);
        detail.Price.WithoutVat.Should().Be(200m);
        detail.Price.Vat.Should().Be(42m);
        detail.Price.CurrencyCode.Should().Be("CZK");
        detail.Price.ExchangeRate.Should().Be(1m);
    }

    [Fact]
    public void Map_EurInvoice_SetsExchangeRateAndCurrency()
    {
        var dto = BuildMinimalDto();
        dto.Price = new ShoptetInvoicePriceDto
        {
            WithVat = "10",
            WithoutVat = "8.26",
            CurrencyCode = "EUR",
            ExchangeRate = "25.5",
        };
        var detail = BuildSut().Map(dto);
        detail.Price.CurrencyCode.Should().Be("EUR");
        detail.Price.ExchangeRate.Should().Be(25.5m);
    }

    [Fact]
    public void Map_Items_TotalPricesAreAmountTimesUnitPrice()
    {
        var detail = BuildSut().Map(BuildMinimalDto());
        var item = detail.Items.Single();
        item.Amount.Should().Be(2m);
        item.ItemPrice.WithoutVat.Should().Be(100m);
        item.ItemPrice.WithVat.Should().Be(121m);
        item.ItemPrice.Vat.Should().Be(21m);
        item.ItemPrice.TotalWithoutVat.Should().Be(200m);
        item.ItemPrice.TotalWithVat.Should().Be(242m);
        item.ItemPrice.CurrencyCode.Should().Be("CZK");
    }

    [Fact]
    public void Map_InvoicePrice_WithoutVatSumsTotalsNotUnitPrices_WhenQuantityGtOne()
    {
        // Regression: WithoutVat was summing unit prices instead of line totals.
        // For amount=3 × unitWithoutVat=100 the invoice WithoutVat must be 300, not 100.
        var dto = BuildMinimalDto();
        dto.Items =
        [
            new ShoptetInvoiceItemDto
            {
                Code = "A",
                Name = "Product A",
                Amount = "3",
                UnitPrice = new ShoptetInvoiceUnitPriceDto
                {
                    WithoutVat = "100",
                    Vat = "21",
                    WithVat = "121",
                    VatRate = "21",
                },
            },
        ];
        dto.Price = new ShoptetInvoicePriceDto
        {
            WithVat = "363",
            WithoutVat = "300",
            CurrencyCode = "CZK",
        };

        var detail = BuildSut().Map(dto);

        detail.Price.WithVat.Should().Be(363m);
        detail.Price.WithoutVat.Should().Be(300m, "must sum line totals (3×100), not unit price (100)");
        detail.Price.Vat.Should().Be(63m);
    }

    [Fact]
    public void Map_Customer_PopulatedFromBillingAddress()
    {
        var detail = BuildSut().Map(BuildMinimalDto());
        detail.Customer.Company.Should().Be("Testovací s.r.o.");
        detail.Customer.Name.Should().Be("Jan Novák");
        detail.Customer.VatId.Should().Be("CZ12345678");
        detail.Customer.CompanyId.Should().Be("12345678");
    }
}
