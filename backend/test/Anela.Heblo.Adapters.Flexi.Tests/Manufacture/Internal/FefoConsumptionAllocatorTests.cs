using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Adapters.Flexi.Tests.Manufacture;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class FefoConsumptionAllocatorTests
{
    private readonly FefoConsumptionAllocator _allocator = new FefoConsumptionAllocator();

    [Fact]
    public void Allocate_SingleLotCoversRequirement_ReturnsSingleConsumptionItem()
    {
        // Arrange
        var ingredient = ManufactureTestData.Materials.Bisabolol;
        var requirements = new Dictionary<string, IngredientRequirement>
        {
            [ingredient.Code] = new IngredientRequirement
            {
                ProductCode = ingredient.Code,
                ProductName = ingredient.Name,
                ProductType = ingredient.Type,
                RequiredAmount = 5.0,
                HasLots = true
            }
        };
        var lots = new Dictionary<string, List<CatalogLot>>
        {
            [ingredient.Code] = new List<CatalogLot>
            {
                ManufactureTestData.CreateLot(ingredient, 10m, "LOT-A", new DateOnly(2025, 6, 1))
            }
        };

        // Act
        var result = _allocator.Allocate(requirements, lots, "PROD001");

        // Assert
        Assert.Single(result);
        var item = result[0];
        Assert.Equal(ingredient.Code, item.ProductCode);
        Assert.Equal("LOT-A", item.LotNumber);
        Assert.Equal(5.0, item.Amount);
        Assert.Equal("PROD001", item.SourceProductCode);
    }

    [Fact]
    public void Allocate_MultipleLotsNeeded_UsesEarliestExpirationFirst()
    {
        // Arrange
        var ingredient = ManufactureTestData.Materials.Bisabolol;
        var requirements = new Dictionary<string, IngredientRequirement>
        {
            [ingredient.Code] = new IngredientRequirement
            {
                ProductCode = ingredient.Code,
                ProductName = ingredient.Name,
                ProductType = ingredient.Type,
                RequiredAmount = 15.0,
                HasLots = true
            }
        };
        var lots = new Dictionary<string, List<CatalogLot>>
        {
            [ingredient.Code] = new List<CatalogLot>
            {
                // Later expiration first in list - should be consumed second
                ManufactureTestData.CreateLot(ingredient, 10m, "LOT-B", new DateOnly(2025, 12, 1)),
                // Earlier expiration - should be consumed first (FEFO)
                ManufactureTestData.CreateLot(ingredient, 10m, "LOT-A", new DateOnly(2025, 6, 1))
            }
        };

        // Act
        var result = _allocator.Allocate(requirements, lots, "PROD001");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("LOT-A", result[0].LotNumber); // Earlier expiry consumed first
        Assert.Equal(10.0, result[0].Amount);
        Assert.Equal("LOT-B", result[1].LotNumber); // Later expiry consumed second
        Assert.Equal(5.0, result[1].Amount);
    }

    [Fact]
    public void Allocate_InsufficientLots_ThrowsFlexiManufactureException()
    {
        // Arrange
        var ingredient = ManufactureTestData.Materials.Bisabolol;
        var requirements = new Dictionary<string, IngredientRequirement>
        {
            [ingredient.Code] = new IngredientRequirement
            {
                ProductCode = ingredient.Code,
                ProductName = ingredient.Name,
                ProductType = ingredient.Type,
                RequiredAmount = 20.0,
                HasLots = true
            }
        };
        var lots = new Dictionary<string, List<CatalogLot>>
        {
            [ingredient.Code] = new List<CatalogLot>
            {
                ManufactureTestData.CreateLot(ingredient, 5m, "LOT-A", new DateOnly(2025, 6, 1))
            }
        };

        // Act & Assert
        var ex = Assert.Throws<FlexiManufactureException>(
            () => _allocator.Allocate(requirements, lots, "PROD001"));

        Assert.Equal(FlexiManufactureOperationKind.Allocation, ex.OperationKind);
        Assert.Contains(ingredient.Code, ex.Message);
    }

    [Fact]
    public void Allocate_RemainderWithinEpsilon_DoesNotThrow()
    {
        // Arrange - require 5.0, have 5.0009 (remainder = 5.0 - 5.0009... actually provide 4.9995 so remainder is within epsilon)
        var ingredient = ManufactureTestData.Materials.Bisabolol;
        var requirements = new Dictionary<string, IngredientRequirement>
        {
            [ingredient.Code] = new IngredientRequirement
            {
                ProductCode = ingredient.Code,
                ProductName = ingredient.Name,
                ProductType = ingredient.Type,
                RequiredAmount = 5.0,
                HasLots = true
            }
        };
        // Provide 5.0 + (epsilon - tiny bit) so remainder after consuming all is exactly 0
        // Actually: lot has 4.9995 (5.0 - 0.0005), remainder = 0.0005 < epsilon (0.001) → no throw
        var lots = new Dictionary<string, List<CatalogLot>>
        {
            [ingredient.Code] = new List<CatalogLot>
            {
                ManufactureTestData.CreateLot(ingredient, 4.9995m, "LOT-A", new DateOnly(2025, 6, 1))
            }
        };

        // Act - should not throw
        var result = _allocator.Allocate(requirements, lots, "PROD001");

        // Assert
        Assert.Single(result);
        Assert.Equal(4.9995, result[0].Amount, precision: 4);
    }
}
