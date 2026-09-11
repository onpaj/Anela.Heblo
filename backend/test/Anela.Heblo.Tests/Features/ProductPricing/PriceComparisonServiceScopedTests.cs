using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

/// <summary>
/// Covers <see cref="PriceComparisonService.BuildScopedReportAsync"/> — the manual "sync"
/// the comparison screen triggers for the products currently on display.
/// </summary>
public class PriceComparisonServiceScopedTests
{
    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly Mock<IEshopPriceListClient> _eshop = new();
    private readonly Mock<IProductPriceErpClient> _erp = new();

    private PriceComparisonService CreateService() => new(_catalog.Object, _eshop.Object, _erp.Object);

    private void GivenCatalog(params string[] codes) =>
        _catalog
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes
                .Select(code => new CatalogAggregate
                {
                    ProductCode = code,
                    Type = ProductType.Product,
                    ProductName = $"Product {code}",
                })
                .ToList());

    private void GivenPerCodeShoptetPrice(string code, decimal? priceWithVat) =>
        _eshop
            .Setup(e => e.GetPriceWithVatAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priceWithVat);

    private void GivenWholeListShoptetPrices(params (string Code, decimal PriceWithVat)[] prices) =>
        _eshop
            .Setup(e => e.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices.ToDictionary(p => p.Code, p => p.PriceWithVat));

    private void GivenErpPrices(params (string Code, decimal PriceWithVat)[] prices) =>
        _erp
            .Setup(e => e.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices
                .Select(p => new ProductPriceErp
                {
                    ProductCode = p.Code,
                    PriceWithVat = p.PriceWithVat,
                    PriceWithoutVat = p.PriceWithVat,
                    ErpItemId = 1,
                    ErpPriceType = "bezDph",
                    VatRate = 21m,
                })
                .ToList());

    [Fact]
    public async Task returns_rows_only_for_the_requested_product_codes()
    {
        // Arrange
        GivenCatalog("A", "B", "C");
        GivenPerCodeShoptetPrice("A", 100m);
        GivenPerCodeShoptetPrice("B", 200m);
        GivenErpPrices(("A", 100m), ("B", 250m), ("C", 300m));

        // Act
        var result = await CreateService().BuildScopedReportAsync(new[] { "A", "B" }, CancellationToken.None);

        // Assert
        result.Rows.Select(r => r.ProductCode).Should().BeEquivalentTo(new[] { "A", "B" });
        result.Summary.TotalInScope.Should().Be(2);
        result.Summary.InAgreementCount.Should().Be(1);
        result.Summary.FlexiDiffersCount.Should().Be(1);
    }

    [Fact]
    public async Task reads_shoptet_per_code_instead_of_the_whole_price_list_for_a_small_selection()
    {
        // Arrange
        GivenCatalog("A", "B", "C");
        GivenPerCodeShoptetPrice("A", 100m);
        GivenErpPrices(("A", 100m));

        // Act
        await CreateService().BuildScopedReportAsync(new[] { "A" }, CancellationToken.None);

        // Assert
        _eshop.Verify(e => e.GetPriceWithVatAsync("A", It.IsAny<CancellationToken>()), Times.Once);
        _eshop.Verify(e => e.GetPricesWithVatAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task matches_requested_codes_case_insensitively()
    {
        // Arrange
        GivenCatalog("MAS001180");
        GivenPerCodeShoptetPrice("MAS001180", 390m);
        GivenErpPrices(("MAS001180", 390m));

        // Act
        var result = await CreateService()
            .BuildScopedReportAsync(new[] { "mas001180" }, CancellationToken.None);

        // Assert
        result.Rows.Should().ContainSingle()
            .Which.Kind.Should().Be(PriceDivergenceKind.InAgreement);
    }

    [Fact]
    public async Task classifies_a_product_shoptet_has_no_price_for_as_missing_in_shoptet()
    {
        // Arrange
        GivenCatalog("A");
        GivenPerCodeShoptetPrice("A", null);
        GivenErpPrices(("A", 100m));

        // Act
        var result = await CreateService().BuildScopedReportAsync(new[] { "A" }, CancellationToken.None);

        // Assert
        result.Rows.Should().ContainSingle()
            .Which.Kind.Should().Be(PriceDivergenceKind.MissingInShoptet);
    }

    [Fact]
    public async Task ignores_a_requested_code_that_is_not_a_priced_catalog_product()
    {
        // Arrange
        GivenCatalog("A");
        GivenPerCodeShoptetPrice("A", 100m);
        GivenErpPrices(("A", 100m));

        // Act
        var result = await CreateService()
            .BuildScopedReportAsync(new[] { "A", "GHOST" }, CancellationToken.None);

        // Assert
        result.Rows.Should().ContainSingle().Which.ProductCode.Should().Be("A");
        _eshop.Verify(e => e.GetPriceWithVatAsync("GHOST", It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The whole point of the sync button: Flexi's ceník read is cached for five minutes, so
    /// without forceReload the operator would press sync and be shown the same stale numbers.
    /// </summary>
    [Fact]
    public async Task forces_a_fresh_flexi_read_rather_than_serving_the_five_minute_cache()
    {
        // Arrange
        GivenCatalog("A");
        GivenPerCodeShoptetPrice("A", 100m);
        GivenErpPrices(("A", 100m));

        // Act
        await CreateService().BuildScopedReportAsync(new[] { "A" }, CancellationToken.None);

        // Assert
        _erp.Verify(e => e.GetAllAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Past a couple of dozen products, one request per product costs more than the paginated
    /// whole-list read — a "sync" with no filter applied must not become an HTTP request per
    /// product in the catalogue.
    /// </summary>
    [Fact]
    public async Task reads_the_whole_price_list_once_for_a_selection_too_large_to_read_per_code()
    {
        // Arrange
        var codes = Enumerable.Range(1, 60).Select(i => $"P{i:000}").ToArray();
        GivenCatalog(codes);
        GivenWholeListShoptetPrices(codes.Select(c => (c, 100m)).ToArray());
        GivenErpPrices(codes.Select(c => (c, 100m)).ToArray());

        // Act
        var result = await CreateService().BuildScopedReportAsync(codes, CancellationToken.None);

        // Assert
        _eshop.Verify(e => e.GetPricesWithVatAsync(It.IsAny<CancellationToken>()), Times.Once);
        _eshop.Verify(
            e => e.GetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Rows.Should().HaveCount(60);
    }

    /// <summary>
    /// Pins the crossover itself, which the 60-product case above is too far from to catch an
    /// off-by-one: 25 selected products are still read one at a time, 26 are not.
    /// </summary>
    [Theory]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public async Task switches_to_the_whole_price_list_one_product_past_the_per_code_threshold(
        int selectionSize, bool expectedPerCodeReads)
    {
        // Arrange
        var codes = Enumerable.Range(1, selectionSize).Select(i => $"P{i:000}").ToArray();
        GivenCatalog(codes);
        GivenWholeListShoptetPrices(codes.Select(c => (c, 100m)).ToArray());
        GivenErpPrices(codes.Select(c => (c, 100m)).ToArray());
        foreach (var code in codes)
        {
            GivenPerCodeShoptetPrice(code, 100m);
        }

        // Act
        var result = await CreateService().BuildScopedReportAsync(codes, CancellationToken.None);

        // Assert
        _eshop.Verify(
            e => e.GetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            expectedPerCodeReads ? Times.Exactly(selectionSize) : Times.Never());
        _eshop.Verify(
            e => e.GetPricesWithVatAsync(It.IsAny<CancellationToken>()),
            expectedPerCodeReads ? Times.Never() : Times.Once());

        // Whichever route ran, the rows are the same — the threshold is a cost heuristic only.
        result.Rows.Should().HaveCount(selectionSize);
        result.Summary.InAgreementCount.Should().Be(selectionSize);
    }

    /// <summary>
    /// The per-code route exists for a sync, not for a page load: the unscoped report keeps
    /// reading the whole list even when the catalogue is small enough to fit under the
    /// per-code threshold.
    /// </summary>
    [Fact]
    public async Task leaves_the_unscoped_report_reading_the_whole_price_list()
    {
        // Arrange
        GivenCatalog("A", "B");
        GivenWholeListShoptetPrices(("A", 100m), ("B", 200m));
        GivenErpPrices(("A", 100m), ("B", 200m));

        // Act
        await CreateService().BuildReportAsync(CancellationToken.None);

        // Assert
        _eshop.Verify(e => e.GetPricesWithVatAsync(It.IsAny<CancellationToken>()), Times.Once);
        _eshop.Verify(
            e => e.GetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _erp.Verify(e => e.GetAllAsync(false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task never_writes_to_shoptet_or_flexi()
    {
        // Arrange
        GivenCatalog("A");
        GivenPerCodeShoptetPrice("A", 100m);
        GivenErpPrices(("A", 250m));

        // Act
        await CreateService().BuildScopedReportAsync(new[] { "A" }, CancellationToken.None);

        // Assert
        _eshop.Verify(
            e => e.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
