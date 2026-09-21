using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class PricingBaselineBuilderTests
{
    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));

    private PricingBaselineBuilder CreateSut() => new(_catalog.Object, _time);

    [Fact]
    public async Task Uses_the_latest_month_costs_not_the_thirteen_month_average()
    {
        // A product whose material cost rose sharply in the most recent month: the average
        // would understate today's cost, which is the whole reason this feature exists.
        var product = CatalogTestData.WithMonthlyMargins(
            productCode: "P1",
            priceWithoutVat: 420m,
            months: new[]
            {
                (Month: new DateTime(2026, 8, 1), Material: 150m, Manufacturing: 70m),
                (Month: new DateTime(2026, 9, 1), Material: 193m, Manufacturing: 70m)
            });
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Single().MaterialCost.Should().Be(193m);
        rows.Single().ManufacturingCost.Should().Be(70m);
    }

    [Fact]
    public async Task Sums_sales_quantity_over_the_trailing_twelve_months()
    {
        var product = CatalogTestData.WithSales("P1", priceWithoutVat: 420m, sales: new[]
        {
            (Date: new DateTime(2026, 9, 1), Quantity: 100d),
            (Date: new DateTime(2026, 3, 1), Quantity: 250d),
            (Date: new DateTime(2025, 6, 1), Quantity: 999d)   // older than 12 months — excluded
        });
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Single().Quantity.Should().Be(350d);
    }

    [Fact]
    public async Task Flags_a_product_without_a_price_as_having_no_data()
    {
        var product = CatalogTestData.WithMonthlyMargins("P1", priceWithoutVat: null,
            months: new[] { (Month: new DateTime(2026, 9, 1), Material: 100m, Manufacturing: 10m) });
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Single().HasData.Should().BeFalse();
    }

    [Fact]
    public async Task Flags_a_product_without_margin_history_as_having_no_data()
    {
        var product = CatalogTestData.WithMonthlyMargins("P1", priceWithoutVat: 420m,
            months: Array.Empty<(DateTime, decimal, decimal)>());
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Single().HasData.Should().BeFalse();
    }

    [Fact]
    public async Task Defaults_to_products_and_goods_when_no_type_filter_is_given()
    {
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate>
                {
                    CatalogTestData.OfType("P1", ProductType.Product),
                    CatalogTestData.OfType("G1", ProductType.Goods),
                    CatalogTestData.OfType("M1", ProductType.Material)
                });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Select(r => r.ProductCode).Should().BeEquivalentTo(new[] { "P1", "G1" });
    }
}

/// <summary>
/// Builds minimal CatalogAggregate instances for the baseline builder tests. PriceWithoutVat
/// is a read-only computed property (EshopPrice?.PriceWithoutVat, falling back to ErpPrice),
/// so tests set EshopPrice.PriceWithoutVat rather than assigning the aggregate's property
/// directly — leaving EshopPrice null (and priceWithoutVat null) reproduces the "no price" case.
/// </summary>
internal static class CatalogTestData
{
    public static CatalogAggregate WithMonthlyMargins(
        string productCode,
        decimal? priceWithoutVat,
        (DateTime Month, decimal Material, decimal Manufacturing)[] months)
    {
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productCode,
            Type = ProductType.Product,
            EshopPrice = priceWithoutVat is null
                ? null
                : new ProductPriceEshop { PriceWithoutVat = priceWithoutVat }
        };

        product.Margins.MonthlyData = months.ToDictionary(
            m => m.Month,
            m => new MarginData
            {
                M0 = new MarginLevel(percentage: 0m, amount: 0m, costTotal: m.Material, costLevel: m.Material),
                M1 = new MarginLevel(percentage: 0m, amount: 0m, costTotal: m.Material + m.Manufacturing, costLevel: m.Manufacturing)
            });

        return product;
    }

    public static CatalogAggregate WithSales(
        string productCode,
        decimal? priceWithoutVat,
        (DateTime Date, double Quantity)[] sales)
    {
        var product = WithMonthlyMargins(productCode, priceWithoutVat,
            new[] { (Month: new DateTime(2026, 9, 1), Material: 0m, Manufacturing: 0m) });

        product.SalesHistory = sales
            .Select(s => new CatalogSaleRecord
            {
                Date = s.Date,
                ProductCode = productCode,
                ProductName = productCode,
                AmountB2B = s.Quantity,
                AmountB2C = 0d
            })
            .ToList();

        return product;
    }

    public static CatalogAggregate OfType(string productCode, ProductType type)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productCode,
            Type = type,
            EshopPrice = new ProductPriceEshop { PriceWithoutVat = 100m }
        };
    }
}
