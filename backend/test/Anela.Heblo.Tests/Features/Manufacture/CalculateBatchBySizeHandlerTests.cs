using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Catalog;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CalculateBatchBySizeHandlerTests
{
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly CalculatedBatchSizeHandler _handler;

    public CalculateBatchBySizeHandlerTests()
    {
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _handler = new CalculatedBatchSizeHandler(_manufactureRepositoryMock.Object, _catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsCalculatedIngredients()
    {
        // Arrange
        const string productCode = "TEST001";
        const double desiredBatchSize = 150.0;
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
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

        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var request = new CalculatedBatchSizeRequest
        {
            ProductCode = productCode,
            DesiredBatchSize = desiredBatchSize
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(productCode, result.ProductCode);
        Assert.Equal("Test Product", result.ProductName);
        Assert.Equal(100.0, result.OriginalBatchSize);
        Assert.Equal(150.0, result.NewBatchSize);
        Assert.Equal(1.5, result.ScaleFactor);

        Assert.Equal(2, result.Ingredients.Count);

        var ingredient1 = result.Ingredients.First(i => i.ProductCode == "ING001");
        Assert.Equal("Ingredient 1", ingredient1.ProductName);
        Assert.Equal(50.0, ingredient1.OriginalAmount);
        Assert.Equal(75.0, ingredient1.CalculatedAmount);
        Assert.Equal(10.00m, ingredient1.Price);

        var ingredient2 = result.Ingredients.First(i => i.ProductCode == "ING002");
        Assert.Equal("Ingredient 2", ingredient2.ProductName);
        Assert.Equal(30.0, ingredient2.OriginalAmount);
        Assert.Equal(45.0, ingredient2.CalculatedAmount);
        Assert.Equal(5.00m, ingredient2.Price);
    }

    [Fact]
    public async Task Handle_NonExistentTemplate_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "NONEXISTENT";
        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var request = new CalculatedBatchSizeRequest
        {
            ProductCode = productCode,
            DesiredBatchSize = 150.0
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ManufactureTemplateNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_NonExistentProduct_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "NONEXISTENT";
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            OriginalAmount = 100.0,
            Ingredients = new List<Ingredient>()
        };

        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        
        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        var request = new CalculatedBatchSizeRequest
        {
            ProductCode = productCode,
            DesiredBatchSize = 150.0
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ProductNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_InvalidOriginalBatchSize_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "TEST001";
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            OriginalAmount = 0.0, // Invalid batch size
            Ingredients = new List<Ingredient>()
        };

        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            MinimalManufactureQuantity = 100.0
        };

        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var request = new CalculatedBatchSizeRequest
        {
            ProductCode = productCode,
            DesiredBatchSize = 150.0
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidBatchSize, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_NullDesiredBatchSize_UsesMinimalManufactureQuantity()
    {
        // Arrange
        const string productCode = "TEST001";
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            OriginalAmount = 100.0,
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

        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            MinimalManufactureQuantity = 200.0
        };

        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var request = new CalculatedBatchSizeRequest
        {
            ProductCode = productCode,
            DesiredBatchSize = null // Should use MinimalManufactureQuantity
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(200.0, result.NewBatchSize);
        Assert.Equal(2.0, result.ScaleFactor); // 200/100 = 2.0
        Assert.Equal(100.0, result.Ingredients[0].CalculatedAmount); // 50 * 2.0 = 100.0
    }
}