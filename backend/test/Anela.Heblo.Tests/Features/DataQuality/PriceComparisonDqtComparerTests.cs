using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
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
}
