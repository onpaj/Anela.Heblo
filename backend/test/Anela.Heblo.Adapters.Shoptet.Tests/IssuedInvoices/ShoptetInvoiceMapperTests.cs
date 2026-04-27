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
        // BuildMinimalDto has 1 item: amount=2, unitPrice.withoutVat=100.
        // Invoice WithoutVat = sum of TotalWithoutVat (line totals) = 200.
        var detail = BuildSut().Map(BuildMinimalDto());
        detail.Price.WithVat.Should().Be(242m);
        detail.Price.WithoutVat.Should().Be(200m, "sums line totals (TotalWithoutVat) for all items");
        // Vat = WithVat - WithoutVat = 242 - 200 = 42.
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
    public void Map_InvoicePrice_WithoutVatSumsLineTotals_WhenQuantityGtOne()
    {
        // Invoice WithoutVat sums line totals (TotalWithoutVat), not per-unit prices.
        // For amount=3 × unitWithoutVat=100 the invoice WithoutVat is 300 (line total).
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
        detail.Price.WithoutVat.Should().Be(300m, "must sum line totals (TotalWithoutVat) for all items");
        // Vat = WithVat - WithoutVat = 363 - 300 = 63.
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

    [Fact]
    public void MapItem_AppliesPriceRatio_WhenLessThanOne()
    {
        var src = new ShoptetInvoiceDto
        {
            Code = "INV1",
            OrderCode = "ORD1",
            Items = new List<ShoptetInvoiceItemDto>
            {
                new()
                {
                    Code = "P1",
                    Name = "Product 1",
                    Amount = "2",
                    ItemType = "product",
                    PriceRatio = 0.78m,
                    UnitPrice = new ShoptetInvoiceUnitPriceDto
                    {
                        WithoutVat = "100", WithVat = "121", Vat = "21", VatRate = "21.00"
                    }
                }
            },
            Price = new ShoptetInvoicePriceDto { CurrencyCode = "CZK", WithVat = "188.76", WithoutVat = "156.00", Vat = "32.76" }
        };
        var mapper = BuildSut();

        var result = mapper.Map(src);

        result.Items.Should().HaveCount(1);
        result.Items[0].ItemPrice.WithoutVat.Should().Be(78m);      // 100 * 0.78
        result.Items[0].ItemPrice.WithVat.Should().Be(94.38m);      // 121 * 0.78
        result.Items[0].ItemPrice.TotalWithoutVat.Should().Be(156m); // 78 * 2
        result.Items[0].ItemPrice.TotalWithVat.Should().Be(188.76m); // 94.38 * 2
        result.Items[0].ItemPrice.Vat.Should().Be(16.38m);  // 21 * 0.78
    }

    [Fact]
    public void MapItem_AppliesPriceRatioZero_ForFreeItem()
    {
        // Real-world case: invoice 126000039 has priceRatio=0.0 (100% free)
        var src = new ShoptetInvoiceDto
        {
            Code = "INV1",
            OrderCode = "ORD1",
            Items = new List<ShoptetInvoiceItemDto>
            {
                new()
                {
                    Code = "TON002030",
                    Name = "Free product",
                    Amount = "1",
                    ItemType = "product",
                    PriceRatio = 0.0m,
                    UnitPrice = new ShoptetInvoiceUnitPriceDto
                    {
                        WithoutVat = "180", WithVat = "217.80", Vat = "37.80", VatRate = "21.00"
                    }
                }
            },
            Price = new ShoptetInvoicePriceDto { CurrencyCode = "CZK", WithVat = "0.00", WithoutVat = "0.00", Vat = "0.00" }
        };
        var mapper = BuildSut();

        var result = mapper.Map(src);

        result.Items.Should().HaveCount(1);
        result.Items[0].ItemPrice.WithoutVat.Should().Be(0m);     // 180 * 0.0
        result.Items[0].ItemPrice.WithVat.Should().Be(0m);        // 217.80 * 0.0
        result.Items[0].ItemPrice.TotalWithoutVat.Should().Be(0m);
        result.Items[0].ItemPrice.TotalWithVat.Should().Be(0m);
        result.Items[0].ItemPrice.Vat.Should().Be(0m);  // 37.80 * 0.0
    }

    [Fact]
    public void MapItem_NoChange_WhenPriceRatioIsNullOrOne()
    {
        var src = new ShoptetInvoiceDto
        {
            Code = "INV1",
            OrderCode = "ORD1",
            Items = new List<ShoptetInvoiceItemDto>
            {
                new() { Code = "P1", Name = "P1", Amount = "1", ItemType = "product", PriceRatio = null,
                        UnitPrice = new ShoptetInvoiceUnitPriceDto { WithoutVat = "100", WithVat = "121", Vat = "21", VatRate = "21.00" } },
                new() { Code = "P2", Name = "P2", Amount = "1", ItemType = "product", PriceRatio = 1.0m,
                        UnitPrice = new ShoptetInvoiceUnitPriceDto { WithoutVat = "50",  WithVat = "60.5", Vat = "10.5", VatRate = "21.00" } },
            },
            Price = new ShoptetInvoicePriceDto { CurrencyCode = "CZK", WithVat = "181.5", WithoutVat = "150", Vat = "31.5" }
        };
        var mapper = BuildSut();

        var result = mapper.Map(src);

        result.Items[0].ItemPrice.WithoutVat.Should().Be(100m);
        result.Items[1].ItemPrice.WithoutVat.Should().Be(50m);
    }

    [Fact]
    public void Map_FoldsDiscountCouponInto_ProductLines_ProportionallyToBase()
    {
        var src = new ShoptetInvoiceDto
        {
            Code = "INV1",
            OrderCode = "ORD1",
            Items = new List<ShoptetInvoiceItemDto>
            {
                new() { Code = "P1", Name = "P1", Amount = "1", ItemType = "product", PriceRatio = 1.0m,
                        UnitPrice = new ShoptetInvoiceUnitPriceDto { WithoutVat = "300", WithVat = "363", Vat = "63", VatRate = "21.00" } },
                new() { Code = "P2", Name = "P2", Amount = "1", ItemType = "product", PriceRatio = 1.0m,
                        UnitPrice = new ShoptetInvoiceUnitPriceDto { WithoutVat = "200", WithVat = "242", Vat = "42", VatRate = "21.00" } },
                new() { Code = "COUPON", Name = "10% off", Amount = "1", ItemType = "discount-coupon", PriceRatio = 1.0m,
                        UnitPrice = new ShoptetInvoiceUnitPriceDto { WithoutVat = "-50", WithVat = "-60.5", Vat = "-10.5", VatRate = "21.00" } },
            },
            Price = new ShoptetInvoicePriceDto { CurrencyCode = "CZK", WithVat = "544.5", WithoutVat = "450", Vat = "94.5" }
        };
        var mapper = BuildSut();

        var result = mapper.Map(src);

        result.Items.Should().HaveCount(2); // coupon row dropped
        // P1 weight = 300/500 = 0.6 → discount share = 0.6 * (-50) = -30 → TotalWithoutVat = 300 - 30 = 270
        // P2 weight = 200/500 = 0.4 → discount share = 0.4 * (-50) = -20 → TotalWithoutVat = 200 - 20 = 180
        result.Items.Single(i => i.Code == "P1").ItemPrice.TotalWithoutVat.Should().Be(270m);
        result.Items.Single(i => i.Code == "P2").ItemPrice.TotalWithoutVat.Should().Be(180m);
        result.Items.Single(i => i.Code == "P1").ItemPrice.WithoutVat.Should().Be(270m); // per unit (amount=1)
        result.Items.Single(i => i.Code == "P2").ItemPrice.WithoutVat.Should().Be(180m);
        result.Price.WithoutVat.Should().Be(450m);
        result.Price.WithVat.Should().Be(544.5m);
    }

    [Fact]
    public void Map_DropsZeroValueAggregateRows_GiftAndVolumeDiscount()
    {
        var src = new ShoptetInvoiceDto
        {
            Code = "INV1",
            OrderCode = "ORD1",
            Items = new List<ShoptetInvoiceItemDto>
            {
                new() { Code = "P1", Name = "P1", Amount = "1", ItemType = "product", PriceRatio = 1.0m,
                        UnitPrice = new ShoptetInvoiceUnitPriceDto { WithoutVat = "100", WithVat = "121", Vat = "21", VatRate = "21.00" } },
                new() { Code = "GIFT", Name = "Free sample", Amount = "1", ItemType = "gift", PriceRatio = 1.0m,
                        UnitPrice = new ShoptetInvoiceUnitPriceDto { WithoutVat = "0", WithVat = "0", Vat = "0", VatRate = "21.00" } },
                new() { Code = "VD", Name = "Volume", Amount = "1", ItemType = "volume-discount", PriceRatio = 1.0m,
                        UnitPrice = new ShoptetInvoiceUnitPriceDto { WithoutVat = "0", WithVat = "0", Vat = "0", VatRate = "21.00" } },
            },
            Price = new ShoptetInvoicePriceDto { CurrencyCode = "CZK", WithVat = "121", WithoutVat = "100", Vat = "21" }
        };
        var mapper = BuildSut();

        var result = mapper.Map(src);

        result.Items.Should().HaveCount(1);
        result.Items[0].Code.Should().Be("P1");
        result.Items[0].ItemPrice.TotalWithoutVat.Should().Be(100m);
    }
}
