using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ItemFilterServiceDiacriticsTests
{
    private readonly ItemFilterService _filterService;

    public ItemFilterServiceDiacriticsTests()
    {
        _filterService = new ItemFilterService();
    }

    [Theory]
    [InlineData("krém", "Krém na ruce", true)] // krém should find Krém
    [InlineData("krem", "Krém na ruce", true)] // krem should find Krém (without diacritic)
    [InlineData("KREM", "Krém na ruce", true)] // KREM should find Krém
    [InlineData("cokolada", "Čokoláda", true)] // cokolada should find Čokoláda
    [InlineData("čokoláda", "Čokoláda", true)] // exact match should work
    [InlineData("ČOKOLÁDA", "Čokoláda", true)] // case insensitive exact match
    [InlineData("mydlo", "Přírodní mýdlo", true)] // mydlo should find mýdlo
    [InlineData("prirodni", "Přírodní mýdlo", true)] // prirodni should find Přírodní
    [InlineData("xyz", "Krém na ruce", false)] // no match
    public void FilterItems_Should_Find_Products_Using_Diacritic_Insensitive_Search(
        string searchTerm, 
        string productName, 
        bool shouldBeFound)
    {
        // Arrange
        var items = new List<ManufacturingStockItemDto>
        {
            new()
            {
                Code = "TEST001",
                Name = productName,
                NameNormalized = productName.NormalizeForSearch(), // Manually normalize for test
                ProductFamily = "Test Family",
                Severity = ManufacturingStockSeverity.Adequate
            }
        };

        var request = new GetManufacturingStockAnalysisRequest
        {
            SearchTerm = searchTerm,
            CriticalItemsOnly = false,
            MajorItemsOnly = false,
            AdequateItemsOnly = true, // Include adequate items
            UnconfiguredOnly = false
        };

        // Act
        var result = _filterService.FilterItems(items, request);

        // Assert
        if (shouldBeFound)
        {
            result.Should().HaveCount(1);
            result[0].Name.Should().Be(productName);
            result[0].Code.Should().Be("TEST001");
        }
        else
        {
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public void FilterItems_Should_Handle_Mixed_Czech_And_English_Characters()
    {
        // Arrange
        var items = new List<ManufacturingStockItemDto>
        {
            new()
            {
                Code = "TEST001",
                Name = "Český produkt with English",
                NameNormalized = "cesky produkt with english",
                ProductFamily = "Mixed",
                Severity = ManufacturingStockSeverity.Adequate
            }
        };

        var request = new GetManufacturingStockAnalysisRequest
        {
            SearchTerm = "cesky",
            CriticalItemsOnly = false,
            MajorItemsOnly = false,
            AdequateItemsOnly = true,
            UnconfiguredOnly = false
        };

        // Act
        var result = _filterService.FilterItems(items, request);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Český produkt with English");
    }
}