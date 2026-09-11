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

    [Fact]
    public async Task fails_the_run_when_not_one_product_had_a_shoptet_price()
    {
        // Arrange: a valid-but-empty or truncated Shoptet read classifies EVERY row as
        // MissingInShoptet, which is excluded from the mismatch count — so the run would
        // complete with zero mismatches and the dashboard tile would render green "vše OK"
        // having compared precisely nothing. Reachable with no exception at all: a wrong
        // Shoptet:DefaultPriceListId, a paginator that truncates to the first page, or every
        // price being unreadable (logged, not thrown).
        _source.Setup(s => s.GetDivergencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceDivergence>
            {
                new() { ProductCode = "A", ShoptetPriceWithVat = null, FlexiPriceWithVat = 190m,
                        Kind = "MissingInShoptet", IsMismatch = false },
                new() { ProductCode = "B", ShoptetPriceWithVat = null, FlexiPriceWithVat = 250m,
                        Kind = "MissingInShoptet", IsMismatch = false },
            });

        // Act
        var act = () => CreateSut().CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert: throwing is what makes DriftDqtJobRunner record the run Failed and the tile
        // go red, instead of reporting a healthy zero.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .And.Message.Should().Contain("Shoptet");
    }

    [Fact]
    public async Task fails_the_run_when_there_is_nothing_in_scope_at_all()
    {
        // Arrange: zero rows is the same defect — a green tile over zero comparisons.
        _source.Setup(s => s.GetDivergencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceDivergence>());

        // Act
        var act = () => CreateSut().CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task does_not_fail_a_normal_mixed_result()
    {
        // Arrange: some products legitimately have no Shoptet price. As long as at least one
        // was actually compared, the run is a real comparison.
        _source.Setup(s => s.GetDivergencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceDivergence>
            {
                new() { ProductCode = "A", ShoptetPriceWithVat = 190m, FlexiPriceWithVat = 190m,
                        Kind = "InAgreement", IsMismatch = false },
                new() { ProductCode = "B", ShoptetPriceWithVat = 250m, FlexiPriceWithVat = 200m,
                        Kind = "FlexiDiffers", IsMismatch = true },
                new() { ProductCode = "C", ShoptetPriceWithVat = null, FlexiPriceWithVat = 100m,
                        Kind = "MissingInShoptet", IsMismatch = false },
            });

        // Act
        var result = await CreateSut().CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert
        result.TotalChecked.Should().Be(3);
        result.Mismatches.Should().ContainSingle().Which.EntityKey.Should().Be("B");
    }

    [Fact]
    public async Task records_missing_in_shoptet_rows_as_observable_but_not_as_mismatches()
    {
        // Arrange
        _source.Setup(s => s.GetDivergencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PriceDivergence>
            {
                new() { ProductCode = "A", ShoptetPriceWithVat = 190m, FlexiPriceWithVat = 190m,
                        Kind = "InAgreement", IsMismatch = false },
                new() { ProductCode = "C", ShoptetPriceWithVat = null, FlexiPriceWithVat = 100m,
                        Kind = "MissingInShoptet", IsMismatch = false },
            });

        // Act
        var result = await CreateSut().CompareAsync(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10), CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        var informational = result.Informational.Should().ContainSingle().Subject;
        informational.EntityKey.Should().Be("C");
        informational.MismatchCode.Should().Be((int)PriceComparisonMismatch.MissingInShoptet);
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
