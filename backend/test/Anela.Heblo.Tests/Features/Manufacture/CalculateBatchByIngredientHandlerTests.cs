using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CalculateBatchByIngredientHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly CalculateBatchByIngredientHandler _handler;

    public CalculateBatchByIngredientHandlerTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _handler = new CalculateBatchByIngredientHandler(_manufactureClientMock.Object, _catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsCalculatedBatchAndIngredients()
    {
        // Arrange
        const string productCode = "TEST001";
        const string ingredientCode = "ING001";
        const double desiredIngredientAmount = 75.0;

        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            Amount = 100.0,
            OriginalAmount = 100.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient
                {
                    ProductCode = "ING001",
                    ProductName = "Ingredient 1",
                    Amount = 50.0,
                    Price = 10.00m
                },
                new Ingredient
                {
                    ProductCode = "ING002",
                    ProductName = "Ingredient 2",
                    Amount = 30.0,
                    Price = 5.00m
                }
            }
        };

        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            MinimalManufactureQuantity = 100.0
        };

        _manufactureClientMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Setup catalog items with stock information
        _catalogRepositoryMock.Setup(x => x.GetByIdAsync("ING001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate { Stock = new StockData { Erp = 100m } });
        _catalogRepositoryMock.Setup(x => x.GetByIdAsync("ING002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate { Stock = new StockData { Erp = 200m } });

        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = productCode,
            IngredientCode = ingredientCode,
            DesiredIngredientAmount = desiredIngredientAmount
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(productCode, result.ProductCode);
        Assert.Equal("Test Product", result.ProductName);
        Assert.Equal(100.0, result.OriginalBatchSize);
        Assert.Equal(150.0, result.NewBatchSize); // 100 * (75/50) = 150
        Assert.Equal(1.5, result.ScaleFactor); // 75/50 = 1.5

        Assert.Equal(ingredientCode, result.ScaledIngredientCode);
        Assert.Equal("Ingredient 1", result.ScaledIngredientName);
        Assert.Equal(50.0, result.ScaledIngredientOriginalAmount);
        Assert.Equal(desiredIngredientAmount, result.ScaledIngredientNewAmount);

        Assert.Equal(2, result.Ingredients.Count);

        var ingredient1 = result.Ingredients.First(i => i.ProductCode == "ING001");
        Assert.Equal("Ingredient 1", ingredient1.ProductName);
        Assert.Equal(50.0, ingredient1.OriginalAmount);
        Assert.Equal(75.0, ingredient1.CalculatedAmount);

        var ingredient2 = result.Ingredients.First(i => i.ProductCode == "ING002");
        Assert.Equal("Ingredient 2", ingredient2.ProductName);
        Assert.Equal(30.0, ingredient2.OriginalAmount);
        Assert.Equal(45.0, ingredient2.CalculatedAmount); // 30 * 1.5 = 45
    }

    [Fact]
    public async Task Handle_NonExistentProductCode_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "NONEXISTENT";
        _manufactureClientMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = productCode,
            IngredientCode = "ING001",
            DesiredIngredientAmount = 75.0
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ManufactureTemplateNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_NonExistentIngredientCode_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "TEST001";
        const string ingredientCode = "NONEXISTENT";

        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            BatchSize = 100.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient
                {
                    ProductCode = "ING001",
                    ProductName = "Ingredient 1",
                    Amount = 50.0,
                    Price = 10.00m
                }
            }
        };

        _manufactureClientMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = productCode,
            IngredientCode = ingredientCode,
            DesiredIngredientAmount = 75.0
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.IngredientNotFoundInTemplate, result.ErrorCode);
    }
}