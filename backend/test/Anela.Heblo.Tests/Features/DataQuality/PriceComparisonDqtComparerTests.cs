using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Infrastructure;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class PriceComparisonDqtComparerTests
{
    private readonly Mock<IPriceComparisonSource> _source = new();

    private PriceComparisonDqtComparer CreateSut() => new(_source.Object);

    [Fact]
    public void handles_the_price_comparison_test_type()
    {
        CreateSut().TestType.Should().Be(DqtTestType.PriceComparison);
    }

    [Fact]
    public async Task counts_every_product_checked_but_reports_only_mismatches()
    {
        // Arrange
        _source.Setup(s => s.GetDivergencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceDivergence>
            {
                new() { ProductCode = "A", ShoptetPriceWithVat = 190m, FlexiPriceWithVat = 190m,
                        Kind = "InAgreement", IsMismatch = false },
                new() { ProductCode = "B", ShoptetPriceWithVat = 250m, FlexiPriceWithVat = 200m,
                        Kind = "FlexiDiffers", IsMismatch = true },
            });

        // Act
        var result = await CreateSut().CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert
        result.TotalChecked.Should().Be(2);
        result.Mismatches.Should().ContainSingle().Which.EntityKey.Should().Be("B");
    }

    [Fact]
    public async Task records_the_shoptet_and_flexi_prices_on_a_mismatch()
    {
        // Arrange
        _source.Setup(s => s.GetDivergencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceDivergence>
            {
                new() { ProductCode = "B", ShoptetPriceWithVat = 250m, FlexiPriceWithVat = 200m,
                        Kind = "FlexiDiffers", IsMismatch = true },
            });

        // Act
        var result = await CreateSut().CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert
        var mismatch = result.Mismatches.Single();
        mismatch.MismatchCode.Should().Be((int)PriceComparisonMismatch.PriceDiffers);
        mismatch.ShoptetValue.Should().Be("250.00");
        mismatch.HebloValue.Should().Be("200.00");
        mismatch.Details.Should().Be("FlexiDiffers");
    }

    /// <summary>
    /// Guards the seam between PriceComparisonDqtAdapter (which calls PriceDivergenceKind.ToString()
    /// to cross the module boundary) and PriceComparisonDqtComparer.MapMismatch (which switches on
    /// those string literals). A PriceDivergenceKind rename that this switch doesn't track would
    /// otherwise silently fall through to PriceComparisonMismatch.Unknown for every mismatch — no
    /// build error, no other test failure, just wrong codes in persisted DQT results.
    /// </summary>
    [Fact]
    public async Task every_mismatch_kind_the_adapter_flags_maps_to_a_known_mismatch_code()
    {
        // Arrange — one row per PriceDivergenceKind value, routed through the real adapter
        var comparisonService = new Mock<IPriceComparisonService>();
        var rows = Enum.GetValues<PriceDivergenceKind>()
            .Select((kind, i) => new PriceDivergenceRowDto
            {
                ProductCode = $"P{i}",
                ShoptetPriceWithVat = 100m,
                FlexiPriceWithVat = 100m,
                Kind = kind,
            })
            .ToList();
        comparisonService
            .Setup(s => s.BuildReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriceComparisonResult { Rows = rows });

        var adapter = new PriceComparisonDqtAdapter(comparisonService.Object);
        var sut = new PriceComparisonDqtComparer(adapter);

        // Act
        var result = await sut.CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert — every kind the adapter flagged as a mismatch resolved to a real mismatch code
        result.Mismatches.Should().NotBeEmpty();
        result.Mismatches.Should().OnlyContain(m => m.MismatchCode != (int)PriceComparisonMismatch.Unknown);
    }
}
