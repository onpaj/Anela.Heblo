using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class BundleSalesExpanderTests
{
    private static readonly DateTime SaleDate = new(2026, 8, 20);
    private readonly BundleSalesExpander _sut = new();

    [Fact]
    public void Expand_MultipliesComponentQuantityByBomAmountForBothChannels()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 3, amountB2C: 5) };
        var parts = new[] { Part("BAL001", "KRM001", amount: 2) };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        var component = result.Single(r => r.ProductCode == "KRM001");
        component.AmountB2B.Should().Be(6);
        component.AmountB2C.Should().Be(10);
        component.AmountTotal.Should().Be(16);
    }

    [Fact]
    public void Expand_LeavesRevenueAtZeroOnSyntheticRecords()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 3, amountB2C: 5) };
        var parts = new[] { Part("BAL001", "KRM001", amount: 2) };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        var component = result.Single(r => r.ProductCode == "KRM001");
        component.SumB2B.Should().Be(0);
        component.SumB2C.Should().Be(0);
        component.SumTotal.Should().Be(0);
    }

    [Fact]
    public void Expand_StampsSourceBundleCodeForTraceability()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 1, amountB2C: 0) };
        var parts = new[] { Part("BAL001", "KRM001", amount: 1) };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        result.Single(r => r.ProductCode == "KRM001").SourceBundleCode.Should().Be("BAL001");
    }

    [Fact]
    public void Expand_KeepsOriginalBundleRecordUntouched()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 3, amountB2C: 5) };
        var parts = new[] { Part("BAL001", "KRM001", amount: 2) };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        var bundle = result.Single(r => r.ProductCode == "BAL001");
        bundle.AmountB2B.Should().Be(3);
        bundle.SumB2C.Should().Be(500);
        bundle.SourceBundleCode.Should().BeNull();
    }

    [Fact]
    public void Expand_PassesNonBundleRecordsThroughUnchanged()
    {
        // Arrange
        var sales = new[]
        {
            new CatalogSaleRecord
            {
                Date = SaleDate,
                ProductCode = "KRM001",
                ProductName = "Krém",
                AmountB2B = 4,
                AmountB2C = 1,
                SumB2C = 250,
            },
        };

        // Act
        var result = _sut.Expand(sales, Array.Empty<CatalogSetPart>());

        // Assert
        result.Should().HaveCount(1);
        result[0].AmountB2B.Should().Be(4);
        result[0].SumB2C.Should().Be(250);
    }

    [Fact]
    public void Expand_DoesNotRecurseWhenComponentIsItselfABundle()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 1, amountB2C: 0) };
        var parts = new[]
        {
            Part("BAL001", "BAL002", amount: 1),
            Part("BAL002", "KRM001", amount: 10),
        };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainSingle(r => r.ProductCode == "BAL002");
        result.Should().NotContain(r => r.ProductCode == "KRM001");
    }

    [Fact]
    public void Expand_ReturnsInputUnchangedWhenPartsAreEmpty()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 3, amountB2C: 5) };

        // Act
        var result = _sut.Expand(sales, Array.Empty<CatalogSetPart>());

        // Assert
        result.Should().HaveCount(1);
        result[0].ProductCode.Should().Be("BAL001");
    }

    [Fact]
    public void Expand_EmitsOneRecordPerComponentOccurrence()
    {
        // Arrange
        var sales = new[] { BundleSale(amountB2B: 1, amountB2C: 0) };
        var parts = new[]
        {
            Part("BAL001", "KRM001", amount: 1),
            Part("BAL001", "MYD001", amount: 3),
        };

        // Act
        var result = _sut.Expand(sales, parts);

        // Assert
        result.Should().HaveCount(3);
        result.Single(r => r.ProductCode == "MYD001").AmountB2B.Should().Be(3);
    }

    private static CatalogSaleRecord BundleSale(double amountB2B, double amountB2C) => new()
    {
        Date = SaleDate,
        ProductCode = "BAL001",
        ProductName = "Dárkový balíček",
        AmountB2B = amountB2B,
        AmountB2C = amountB2C,
        AmountTotal = amountB2B + amountB2C,
        SumB2C = 500,
        SumTotal = 500,
    };

    private static CatalogSetPart Part(string setCode, string componentCode, double amount) => new()
    {
        SetCode = setCode,
        ComponentCode = componentCode,
        ComponentName = componentCode + " name",
        Amount = amount,
    };
}
