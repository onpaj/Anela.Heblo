using Anela.Heblo.Application.Features.Manufacture.UseCases.GetBatchTemplate;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetBatchTemplateHandlerTests
{
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly GetBatchTemplateHandler _handler;

    public GetBatchTemplateHandlerTests()
    {
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _handler = new GetBatchTemplateHandler(_manufactureRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidProductCode_ReturnsSuccessResponse()
    {
        // Arrange
        const string productCode = "TEST001";
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
                    Amount = 25.0,
                    Price = 5.00m
                }
            }
        };

        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new GetBatchTemplateRequest { ProductCode = productCode };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(productCode, result.ProductCode);
        Assert.Equal("Test Product", result.ProductName);
        Assert.Equal(100.0, result.BatchSize);
        Assert.Equal(2, result.Ingredients.Count);

        var ingredient1 = result.Ingredients.First(i => i.ProductCode == "ING001");
        Assert.Equal("Ingredient 1", ingredient1.ProductName);
        Assert.Equal(50.0, ingredient1.Amount);
        Assert.Equal(10.00m, ingredient1.Price);
    }

    [Fact]
    public async Task Handle_NonExistentProductCode_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "NONEXISTENT";
        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var request = new GetBatchTemplateRequest { ProductCode = productCode };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ManufactureTemplateNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_RepositoryThrowsException_ReturnsErrorResponse()
    {
        // Arrange
        const string productCode = "TEST001";
        _manufactureRepositoryMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var request = new GetBatchTemplateRequest { ProductCode = productCode };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Exception, result.ErrorCode);
    }
}