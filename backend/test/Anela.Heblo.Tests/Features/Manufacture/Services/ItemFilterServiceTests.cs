using Anela.Heblo.Application.Features.Manufacture.Model;
using FluentAssertions;
using Anela.Heblo.Application.Features.Manufacture.Services;
using FluentAssertions;
using Xunit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ItemFilterServiceTests
{
    private readonly ItemFilterService _sut;

    public ItemFilterServiceTests()
    {
        _sut = new ItemFilterService();
    }

    [Fact]
    public void FilterItems_WithProductFamilyFilter_FiltersCorrectly()
    {
        // Arrange
        var items = new List<ManufacturingStockItemDto>
        {
            CreateItem("PROD1", "Product 1", "FamilyA"),
            CreateItem("PROD2", "Product 2", "FamilyB"),
            CreateItem("PROD3", "Product 3", "FamilyA")
        };

        var request = new GetManufacturingStockAnalysisRequest
        {
            ProductFamily = "FamilyA"
        };

        // Act
        var result = _sut.FilterItems(items, request);

        // Assert
        result.Count.Should().Be(2);
        Assert.All(result, item => Assert.Equal("FamilyA", item.ProductFamily));
    }

    [Fact]
    public void FilterItems_WithSearchTerm_FiltersCorrectly()
    {
        // Arrange
        var items = new List<ManufacturingStockItemDto>
        {
            CreateItem("PROD1", "Special Product", "FamilyA"),
            CreateItem("PROD2", "Normal Product", "FamilyB"),
            CreateItem("SPECIAL", "Another Item", "FamilyC")
        };

        var request = new GetManufacturingStockAnalysisRequest
        {
            SearchTerm = "special"
        };

        // Act
        var result = _sut.FilterItems(items, request);

        // Assert
        result.Count.Should().Be(2);
        result.Should().Contain(item => item.Code == "PROD1");
        result.Should().Contain(item => item.Code == "SPECIAL");
    }

    [Fact]
    public void FilterItems_WithCriticalItemsOnly_FiltersCorrectly()
    {
        // Arrange
        var items = new List<ManufacturingStockItemDto>
        {
            CreateItem("PROD1", "Product 1", "FamilyA", ManufacturingStockSeverity.Critical),
            CreateItem("PROD2", "Product 2", "FamilyB", ManufacturingStockSeverity.Major),
            CreateItem("PROD3", "Product 3", "FamilyA", ManufacturingStockSeverity.Critical)
        };

        var request = new GetManufacturingStockAnalysisRequest
        {
            CriticalItemsOnly = true
        };

        // Act
        var result = _sut.FilterItems(items, request);

        // Assert
        result.Count.Should().Be(2);
        Assert.All(result, item => Assert.Equal(ManufacturingStockSeverity.Critical, item.Severity));
    }

    [Fact]
    public void FilterItems_WithNoSeverityFilters_HidesUnconfiguredItemsByDefault()
    {
        // Arrange
        var items = new List<ManufacturingStockItemDto>
        {
            CreateItem("PROD1", "Product 1", "FamilyA", ManufacturingStockSeverity.Critical),
            CreateItem("PROD2", "Product 2", "FamilyB", ManufacturingStockSeverity.Unconfigured),
            CreateItem("PROD3", "Product 3", "FamilyA", ManufacturingStockSeverity.Adequate)
        };

        var request = new GetManufacturingStockAnalysisRequest();

        // Act
        var result = _sut.FilterItems(items, request);

        // Assert
        result.Count.Should().Be(2);
        Assert.DoesNotContain(result, item => item.Severity == ManufacturingStockSeverity.Unconfigured);
    }

    [Fact]
    public void SortItems_ByProductCode_SortsCorrectly()
    {
        // Arrange
        var items = new List<ManufacturingStockItemDto>
        {
            CreateItem("PROD3", "Product 3"),
            CreateItem("PROD1", "Product 1"),
            CreateItem("PROD2", "Product 2")
        };

        // Act
        var result = _sut.SortItems(items, ManufacturingStockSortBy.ProductCode, descending: false);

        // Assert
        result[0].Code.Should().Be("PROD1");
        result[1].Code.Should().Be("PROD2");
        result[2].Code.Should().Be("PROD3");
    }

    [Fact]
    public void SortItems_ByProductCodeDescending_SortsCorrectly()
    {
        // Arrange
        var items = new List<ManufacturingStockItemDto>
        {
            CreateItem("PROD1", "Product 1"),
            CreateItem("PROD2", "Product 2"),
            CreateItem("PROD3", "Product 3")
        };

        // Act
        var result = _sut.SortItems(items, ManufacturingStockSortBy.ProductCode, descending: true);

        // Assert
        result[0].Code.Should().Be("PROD3");
        result[1].Code.Should().Be("PROD2");
        result[2].Code.Should().Be("PROD1");
    }

    [Fact]
    public void CalculateSummary_CalculatesCorrectCounts()
    {
        // Arrange
        var fromDate = new DateTime(2023, 1, 1);
        var toDate = new DateTime(2023, 3, 31);
        var productFamilies = new List<string> { "FamilyA", "FamilyB" };

        var items = new List<ManufacturingStockItemDto>
        {
            CreateItem("PROD1", "Product 1", "FamilyA", ManufacturingStockSeverity.Critical),
            CreateItem("PROD2", "Product 2", "FamilyB", ManufacturingStockSeverity.Critical),
            CreateItem("PROD3", "Product 3", "FamilyA", ManufacturingStockSeverity.Major),
            CreateItem("PROD4", "Product 4", "FamilyB", ManufacturingStockSeverity.Adequate),
            CreateItem("PROD5", "Product 5", "FamilyA", ManufacturingStockSeverity.Unconfigured)
        };

        // Act
        var result = _sut.CalculateSummary(items, fromDate, toDate, productFamilies);

        // Assert
        result.TotalProducts.Should().Be(5);
        result.CriticalCount.Should().Be(2);
        result.MajorCount.Should().Be(1);
        result.MinorCount.Should().Be(0);
        result.AdequateCount.Should().Be(1);
        result.UnconfiguredCount.Should().Be(1);
        result.AnalysisPeriodStart.Should().Be(fromDate);
        result.AnalysisPeriodEnd.Should().Be(toDate);
        result.ProductFamilies.Should().BeEquivalentTo(productFamilies);
    }

    private static ManufacturingStockItemDto CreateItem(
        string code,
        string name,
        string? productFamily = null,
        ManufacturingStockSeverity severity = ManufacturingStockSeverity.Adequate)
    {
        return new ManufacturingStockItemDto
        {
            Code = code,
            Name = name,
            ProductFamily = productFamily,
            Severity = severity
        };
    }
}