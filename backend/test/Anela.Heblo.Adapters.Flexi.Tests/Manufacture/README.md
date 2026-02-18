# FlexiManufactureClient Test Data Migration

## Overview

This document tracks the migration of `FlexiManufactureClientTests` to use real test data defined in `docs/testing/test-data-fixtures.md` as per the testing strategy.

## Changes Made

### 1. Created ManufactureTestData.cs

A new test data factory class that provides:
- **Real Product Codes**: Using actual product codes from staging environment (e.g., `AKL001` - Bisabolol, `DEO001005` - Důvěrný pan Jasmín)
- **Product Categories**: Materials, SemiProducts, and Products organized by type
- **Factory Methods**: Helper methods to create BoM items, requests, stock data, and lots using real product data

### 2. Updated Sample Tests

Migrated 3 representative tests to demonstrate the pattern:
- `GetManufactureTemplateAsync_WhenBoMExists_ReturnsCorrectTemplate`
- `SubmitManufactureAsync_SingleProductSingleIngredient_LoadsTemplateAndScalesCorrectly`
- `SubmitManufactureAsync_SharedIngredientAcrossProducts_AggregatesRequiredAmounts`

### 3. Added Legacy Helper Comments

Marked old helper methods (`CreateBoMItem`, `CreateBoMItemWithProductType`, `CreateDefaultRequest`) as legacy to guide future updates.

## Test Data Used

From `docs/testing/test-data-fixtures.md`:

### Materials (AKL Series)
- **AKL001** - Bisabolol
- **AKL003** - Dermosoft Eco 1388
- **AKL007** - Glycerol 99% Ph.Eur
- **AKL011** - Pentylen Glykol Green+
- **AKL020** - Arrowroot škrob BIO
- **AKL021** - Oxid zinečnatý

### Semi-Products (MAS Series)
- **MAS001001M** - Hedvábný pan Jasmín

### Products
- **DAR001** - Dárkové balení
- **DEO001005** - Důvěrný pan Jasmín 5ml

## Current Status

⚠️ **The test file has pre-existing compilation errors** that need to be resolved:

### Compilation Errors

The tests use `FlexiBeeApiResult<StockItemMovementResultFlexiDto>` type which doesn't exist in the codebase. This appears to be related to recent changes in the `FlexiManufactureClient` implementation.

**Example of problematic code:**
```csharp
mockStockMovementClient.Setup(x => x.SaveAsync(...))
    .ReturnsAsync(new FlexiBeeApiResult<StockItemMovementResultFlexiDto> { IsSuccess = true });
```

**Correct pattern (used in migrated tests):**
```csharp
var successResult = new { IsSuccess = true };
mockStockMovementClient.Setup(x => x.SaveAsync(...))
    .Returns(Task.FromResult((dynamic)successResult));
```

## Next Steps

1. **Fix Compilation Errors**: Update all remaining tests to use the correct mock return type pattern
2. **Complete Migration**: Apply the ManufactureTestData pattern to all 60+ tests in the file
3. **Verify Tests Pass**: Run `dotnet test` to ensure all tests pass after migration

## Migration Pattern

For new or updated tests, use this pattern:

```csharp
[Fact]
public async Task YourTest()
{
    // Arrange - Use real product data from ManufactureTestData
    var product = ManufactureTestData.Products.ConfidentBar;
    var ingredient = ManufactureTestData.Materials.Bisabolol;

    var request = ManufactureTestData.CreateManufactureRequest(product, 50);
    var header = ManufactureTestData.CreateBoMItem(1, 1, 100.0, product);
    var ingredientBoM = ManufactureTestData.CreateBoMItem(2, 2, 10.0, ingredient);

    var stock = ManufactureTestData.CreateStock(ingredient, 100, 10.0m);
    var lot = ManufactureTestData.CreateLot(ingredient, 100, "LOT-BISABOLOL", new DateOnly(2025, 6, 1));

    // Setup mocks...

    // Act
    var result = await client.SubmitManufactureAsync(request);

    // Assert
    result.Should().Be("MO-001");
}
```

## Benefits

- **Realistic Data**: Tests use actual product codes from staging environment
- **Better Documentation**: Test data fixtures serve as documentation of available test data
- **Consistency**: All tests use the same product references
- **Maintainability**: Changes to product codes only need to be updated in one place
- **Alignment with Strategy**: Follows the testing strategy guidelines from `docs/architecture/testing-strategy.md`

## References

- **Test Data Fixtures**: `docs/testing/test-data-fixtures.md`
- **Testing Strategy**: `docs/architecture/testing-strategy.md`
- **Test Data Factory**: `ManufactureTestData.cs`
