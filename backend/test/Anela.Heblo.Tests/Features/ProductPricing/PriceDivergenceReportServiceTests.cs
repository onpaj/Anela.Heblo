using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class PriceDivergenceReportServiceTests
{
    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly Mock<IEshopPriceListClient> _eshop = new();
    private readonly Mock<IProductPriceErpClient> _erp = new();
    private readonly Mock<IProductPriceRepository> _priceRepository = new();

    private PriceDivergenceReportService CreateService() => new(
        _catalog.Object, _eshop.Object, _erp.Object, _priceRepository.Object);

    private void GivenCatalog(params (string Code, ProductType Type, string Name)[] products) =>
        _catalog
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products
                .Select(p => new CatalogAggregate { ProductCode = p.Code, Type = p.Type, ProductName = p.Name })
                .ToList());

    private void GivenShoptetPrices(params (string Code, decimal PriceWithVat)[] prices) =>
        _eshop
            .Setup(e => e.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices.ToDictionary(p => p.Code, p => p.PriceWithVat));

    private void GivenErpPrices(params ProductPriceErp[] prices) =>
        _erp
            .Setup(e => e.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices);

    private void GivenMasterPrices(params ProductPrice[] prices) =>
        _priceRepository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices);

    private PriceDivergenceReportServiceTests WithDefaults()
    {
        _eshop.Setup(e => e.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());
        _erp.Setup(e => e.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>());
        _priceRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>());
        return this;
    }

    [Fact]
    public async Task classifies_matching_prices_as_in_agreement()
    {
        // Arrange
        WithDefaults();
        GivenCatalog(("MAS001180", ProductType.Product, "Maska"));
        GivenShoptetPrices(("MAS001180", 390.00m));
        GivenErpPrices(new ProductPriceErp
        {
            ProductCode = "MAS001180", PriceWithVat = 390.00m, PriceWithoutVat = 322.31m, ErpPriceType = "bezDph",
        });

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Kind.Should().Be(PriceDivergenceKind.InAgreement);
        row.DifferenceWithVat.Should().Be(0.00m);
        row.DifferencePercent.Should().Be(0.00m);
        result.Summary.TotalInScope.Should().Be(1);
        result.Summary.InAgreementCount.Should().Be(1);
    }

    [Fact]
    public async Task classifies_disagreeing_known_prices_as_flexi_differs_and_computes_percentage()
    {
        // Arrange — the real MAS001180 example from the Shoptet/Flexi VAT-semantics docs.
        WithDefaults();
        GivenCatalog(("MAS001180", ProductType.Product, "Maska"));
        GivenShoptetPrices(("MAS001180", 390.00m));
        GivenErpPrices(new ProductPriceErp
        {
            ProductCode = "MAS001180", PriceWithVat = 447.70m, PriceWithoutVat = 370.00m, ErpPriceType = "bezDph",
        });

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Kind.Should().Be(PriceDivergenceKind.FlexiDiffers);
        row.DifferenceWithVat.Should().Be(57.70m);
        // (447.70 - 390.00) / 390.00 * 100 = 14.79...% rounded away-from-zero to 2 decimals.
        row.DifferencePercent.Should().Be(14.79m);
        result.Summary.FlexiDiffersCount.Should().Be(1);
    }

    [Fact]
    public async Task classifies_product_absent_from_shoptet_as_missing_in_shoptet()
    {
        // Arrange
        WithDefaults();
        GivenCatalog(("ABC001", ProductType.Goods, "Widget"));
        GivenErpPrices(new ProductPriceErp
        {
            ProductCode = "ABC001", PriceWithVat = 100.00m, PriceWithoutVat = 82.64m, ErpPriceType = "bezDph",
        });

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Kind.Should().Be(PriceDivergenceKind.MissingInShoptet);
        row.ShoptetPriceWithVat.Should().BeNull();
        row.DifferenceWithVat.Should().BeNull();
        row.DifferencePercent.Should().BeNull();
        result.Summary.MissingInShoptetCount.Should().Be(1);
    }

    [Fact]
    public async Task classifies_product_absent_from_flexi_as_missing_in_flexi()
    {
        // Arrange
        WithDefaults();
        GivenCatalog(("ABC002", ProductType.Set, "Sada"));
        GivenShoptetPrices(("ABC002", 250.00m));

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Kind.Should().Be(PriceDivergenceKind.MissingInFlexi);
        row.FlexiPriceWithVat.Should().BeNull();
        row.DifferenceWithVat.Should().BeNull();
        result.Summary.MissingInFlexiCount.Should().Be(1);
    }

    [Fact]
    public async Task classifies_unknown_flexi_price_type_even_when_prices_happen_to_agree()
    {
        // Arrange — precedence check: values match to 2 decimals, but the price type is
        // unknown, so the agreement cannot be trusted and must be reported as such rather
        // than as InAgreement.
        WithDefaults();
        GivenCatalog(("XYZ001", ProductType.Product, "Krem"));
        GivenShoptetPrices(("XYZ001", 199.00m));
        GivenErpPrices(new ProductPriceErp
        {
            ProductCode = "XYZ001", PriceWithVat = 199.00m, PriceWithoutVat = 164.46m, ErpPriceType = null,
        });

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Kind.Should().Be(PriceDivergenceKind.FlexiPriceTypeUnknown);
        row.FlexiPriceType.Should().BeNull();
        result.Summary.FlexiPriceTypeUnknownCount.Should().Be(1);
        result.Summary.InAgreementCount.Should().Be(0);
    }

    [Fact]
    public async Task classifies_unknown_flexi_price_type_over_flexi_differs_when_both_apply()
    {
        // Arrange — precedence check: values disagree AND the price type is unknown.
        // FlexiPriceTypeUnknown takes precedence because an untrustworthy price type makes
        // "differs" just as unreliable a conclusion as "agrees".
        WithDefaults();
        GivenCatalog(("XYZ002", ProductType.Product, "Krem"));
        GivenShoptetPrices(("XYZ002", 199.00m));
        GivenErpPrices(new ProductPriceErp
        {
            ProductCode = "XYZ002", PriceWithVat = 250.00m, PriceWithoutVat = 206.61m, ErpPriceType = null,
        });

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Kind.Should().Be(PriceDivergenceKind.FlexiPriceTypeUnknown);
        result.Summary.FlexiPriceTypeUnknownCount.Should().Be(1);
        result.Summary.FlexiDiffersCount.Should().Be(0);
    }

    [Fact]
    public async Task missing_in_shoptet_takes_precedence_over_missing_in_flexi_when_both_are_absent()
    {
        // Arrange — neither Shoptet nor Flexi has the product. Shoptet is the retail source
        // of truth, so its absence is reported first.
        WithDefaults();
        GivenCatalog(("GONE001", ProductType.Product, "Ztraceny"));

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Kind.Should().Be(PriceDivergenceKind.MissingInShoptet);
    }

    [Fact]
    public async Task reports_heblo_master_price_alongside_the_comparison()
    {
        // Arrange
        WithDefaults();
        GivenCatalog(("MST001", ProductType.Product, "Master"));
        GivenShoptetPrices(("MST001", 100.00m));
        GivenMasterPrices(new ProductPrice { ProductCode = "MST001", PriceWithVat = 105.00m, VatRate = 21m });

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        result.Rows.Should().ContainSingle().Which.HebloMasterPriceWithVat.Should().Be(105.00m);
    }

    [Fact]
    public async Task excludes_out_of_scope_product_types_from_the_report()
    {
        // Arrange — materials and semi-products carry no retail price (assumption A3,
        // matching ProductPriceSyncService) and must not appear in the report at all.
        WithDefaults();
        GivenCatalog(
            ("MAT001", ProductType.Material, "Surovina"),
            ("SEMI001", ProductType.SemiProduct, "Polotovar"));

        // Act
        var result = await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        result.Rows.Should().BeEmpty();
        result.Summary.TotalInScope.Should().Be(0);
    }

    [Fact]
    public async Task never_calls_any_write_method_on_any_dependency()
    {
        // Arrange — the constraint this whole feature exists to guarantee: building the
        // report must never write to Shoptet, Flexi, or the ProductPrices/sync-state tables.
        WithDefaults();
        GivenCatalog(("MAS001180", ProductType.Product, "Maska"));
        GivenShoptetPrices(("MAS001180", 390.00m));
        GivenErpPrices(new ProductPriceErp
        {
            ProductCode = "MAS001180", PriceWithVat = 447.70m, PriceWithoutVat = 370.00m, ErpPriceType = "bezDph",
        });

        // Act
        await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        _eshop.Verify(
            e => e.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _priceRepository.Verify(
            r => r.UpsertAsync(It.IsAny<ProductPrice>(), It.IsAny<CancellationToken>()), Times.Never);
        _priceRepository.Verify(
            r => r.UpsertSyncStateAsync(It.IsAny<ProductPriceSyncState>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _priceRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
