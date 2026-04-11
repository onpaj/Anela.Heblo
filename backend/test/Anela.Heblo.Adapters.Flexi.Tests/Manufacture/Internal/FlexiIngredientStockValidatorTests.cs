using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Adapters.Flexi.Tests.Manufacture;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class FlexiIngredientStockValidatorTests
{
    private readonly Mock<IErpStockClient> _mockStockClient;
    private readonly FlexiIngredientStockValidator _validator;

    public FlexiIngredientStockValidatorTests()
    {
        _mockStockClient = new Mock<IErpStockClient>();
        _validator = new FlexiIngredientStockValidator(_mockStockClient.Object, TimeProvider.System);
    }

    [Fact]
    public async Task Validate_WhenMultipleIngredientsInsufficient_AggregatesAllIntoOneException()
    {
        // Arrange: 3 ingredients, 2 have insufficient stock
        var ingredient1 = ManufactureTestData.Materials.Bisabolol;  // Sufficient
        var ingredient2 = ManufactureTestData.Materials.Glycerol;   // Insufficient
        var ingredient3 = ManufactureTestData.Materials.DermosoftEco; // Insufficient

        var requirements = new Dictionary<string, IngredientRequirement>
        {
            [ingredient1.Code] = new IngredientRequirement
            {
                ProductCode = ingredient1.Code,
                ProductName = ingredient1.Name,
                ProductType = ProductType.SemiProduct,
                RequiredAmount = 5.0,
                HasLots = false
            },
            [ingredient2.Code] = new IngredientRequirement
            {
                ProductCode = ingredient2.Code,
                ProductName = ingredient2.Name,
                ProductType = ProductType.SemiProduct,
                RequiredAmount = 10.0,
                HasLots = false
            },
            [ingredient3.Code] = new IngredientRequirement
            {
                ProductCode = ingredient3.Code,
                ProductName = ingredient3.Name,
                ProductType = ProductType.SemiProduct,
                RequiredAmount = 8.0,
                HasLots = false
            }
        };

        // Stock for SemiProducts warehouse: ingredient1 sufficient, 2 and 3 insufficient
        var stockItems = new List<ErpStock>
        {
            new() { ProductCode = ingredient1.Code, Stock = 100m, Price = 10m }, // Sufficient (100 >= 5)
            new() { ProductCode = ingredient2.Code, Stock = 2m, Price = 10m },   // Insufficient (2 < 10)
            new() { ProductCode = ingredient3.Code, Stock = 1m, Price = 10m }    // Insufficient (1 < 8)
        };

        _mockStockClient
            .Setup(x => x.StockToDateAsync(
                It.IsAny<DateTime>(),
                FlexiStockClient.SemiProductsWarehouseId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stockItems);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FlexiManufactureException>(
            () => _validator.ValidateAsync(requirements, CancellationToken.None));

        Assert.Equal(FlexiManufactureOperationKind.StockValidation, ex.OperationKind);

        // Both failing ingredient codes should be in the message
        Assert.Contains(ingredient2.Code, ex.Message);
        Assert.Contains(ingredient3.Code, ex.Message);

        // Sufficient ingredient should NOT be mentioned
        Assert.DoesNotContain(ingredient1.Code, ex.Message);
    }
}
