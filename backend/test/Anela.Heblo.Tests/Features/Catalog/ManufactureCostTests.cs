using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class ManufactureCostTests
{
    private static ManufactureCost Create(DateTime date) => new ManufactureCost
    {
        Date = date,
        MaterialCostFromReceiptDocument = 100m,
        MaterialCostFromPurchasePrice = 60m,
        HandlingCost = 7m
    };

    [Theory]
    [InlineData(2024, 1, 1)]
    [InlineData(2025, 8, 24)]
    public void MaterialCost_BeforeCutoff_UsesPurchasePrice(int year, int month, int day)
    {
        Create(new DateTime(year, month, day)).MaterialCost.Should().Be(60m);
    }

    [Theory]
    [InlineData(2025, 8, 26)]
    [InlineData(2026, 1, 1)]
    public void MaterialCost_AfterCutoff_UsesReceiptDocument(int year, int month, int day)
    {
        Create(new DateTime(year, month, day)).MaterialCost.Should().Be(100m);
    }

    [Fact]
    public void MaterialCost_ExactlyAtCutoffMidnight_UsesPurchasePrice()
    {
        Create(new DateTime(2025, 8, 25, 0, 0, 0)).MaterialCost.Should().Be(60m);
    }

    [Fact]
    public void MaterialCost_SameDayAfterMidnight_UsesReceiptDocument()
    {
        Create(new DateTime(2025, 8, 25, 12, 0, 0)).MaterialCost.Should().Be(100m);
    }

    [Fact]
    public void MaterialCost_LastSecondBeforeCutoffDay_UsesPurchasePrice()
    {
        Create(new DateTime(2025, 8, 24, 23, 59, 59)).MaterialCost.Should().Be(60m);
    }

    [Fact]
    public void Total_BeforeCutoff_SumsPurchasePriceAndHandling()
    {
        Create(new DateTime(2025, 1, 1)).Total.Should().Be(67m);
    }

    [Fact]
    public void Total_AfterCutoff_SumsReceiptDocumentAndHandling()
    {
        Create(new DateTime(2025, 12, 1)).Total.Should().Be(107m);
    }

    [Fact]
    public void Total_DefaultInstance_IsZero()
    {
        new ManufactureCost().Total.Should().Be(0m);
    }
}
