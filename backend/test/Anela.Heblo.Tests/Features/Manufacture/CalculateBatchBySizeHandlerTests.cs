using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CalculateBatchBySizeHandlerTests
{
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly CalculateBatchBySizeHandler _handler;

    public CalculateBatchBySizeHandlerTests()
    {
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _handler = new CalculateBatchBySizeHandler(_manufactureRepositoryMock.Object);
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
            BatchSize = 100.0,
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

        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new CalculateBatchBySizeRequest
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
    public async Task Handle_NonExistentProductCode_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "NONEXISTENT";
        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var request = new CalculateBatchBySizeRequest
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
    public async Task Handle_InvalidOriginalBatchSize_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "TEST001";
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            BatchSize = 0.0, // Invalid batch size
            Ingredients = new List<Ingredient>()
        };

        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new CalculateBatchBySizeRequest
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
}