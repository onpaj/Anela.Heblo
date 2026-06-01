using Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetSemiproductRecipePdfHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ISemiproductRecipeRenderer> _rendererMock;
    private readonly GetSemiproductRecipePdfHandler _handler;

    public GetSemiproductRecipePdfHandlerTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _catalogRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);
        _rendererMock = new Mock<ISemiproductRecipeRenderer>();
        _rendererMock.Setup(r => r.Render(It.IsAny<SemiproductRecipeData>()))
            .Returns(new byte[] { 1, 2, 3 });
        _handler = new GetSemiproductRecipePdfHandler(
            _manufactureClientMock.Object,
            _catalogRepositoryMock.Object,
            _rendererMock.Object);
    }

    [Fact]
    public async Task Handle_TwoIngredients_ComputesCorrectPercentagesAndHalfBatches()
    {
        // Arrange
        const string productCode = "SP001";
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Semiproduct",
            OriginalAmount = 1000.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient { ProductCode = "ING001", ProductName = "Ingredient A", Amount = 300.0 },
                new Ingredient { ProductCode = "ING002", ProductName = "Ingredient B", Amount = 700.0 },
            }
        };

        _manufactureClientMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new GetSemiproductRecipePdfRequest { ProductCode = productCode };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal($"receptura-{productCode}.pdf", result.FileName);

        _rendererMock.Verify(r => r.Render(It.Is<SemiproductRecipeData>(d =>
            d.ProductCode == productCode &&
            d.Ingredients.Count == 2 &&
            d.Ingredients[0].AmountFullBatch == 300.0 &&
            d.Ingredients[0].AmountHalfBatch == 150.0 &&
            d.Ingredients[0].Percentage == 30.0 &&
            d.Ingredients[1].AmountFullBatch == 700.0 &&
            d.Ingredients[1].AmountHalfBatch == 350.0 &&
            d.Ingredients[1].Percentage == 70.0
        )), Times.Once);
    }

    [Fact]
    public async Task Handle_WithBatchSize_ScalesIngredientAmounts()
    {
        // Arrange
        const string productCode = "SP002";
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Scaled Semiproduct",
            OriginalAmount = 1000.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient { ProductCode = "ING001", ProductName = "Ingredient A", Amount = 300.0 },
                new Ingredient { ProductCode = "ING002", ProductName = "Ingredient B", Amount = 700.0 },
            }
        };

        _manufactureClientMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new GetSemiproductRecipePdfRequest { ProductCode = productCode, BatchSize = 2000.0 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        _rendererMock.Verify(r => r.Render(It.Is<SemiproductRecipeData>(d =>
            d.BatchSize == 2000.0 &&
            d.Ingredients[0].AmountFullBatch == 600.0 &&
            d.Ingredients[0].AmountHalfBatch == 300.0 &&
            d.Ingredients[1].AmountFullBatch == 1400.0 &&
            d.Ingredients[1].AmountHalfBatch == 700.0
        )), Times.Once);
    }

    [Fact]
    public async Task Handle_NullTemplate_ReturnsManufactureTemplateNotFoundError()
    {
        // Arrange
        const string productCode = "MISSING";
        _manufactureClientMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var request = new GetSemiproductRecipePdfRequest { ProductCode = productCode };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ManufactureTemplateNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ZeroTotalAmount_SetsPercentageToZero()
    {
        // Arrange
        const string productCode = "ZERO001";
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Zero Semiproduct",
            OriginalAmount = 0.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient { ProductCode = "ING001", ProductName = "Ingredient A", Amount = 0.0 },
                new Ingredient { ProductCode = "ING002", ProductName = "Ingredient B", Amount = 0.0 },
            }
        };

        _manufactureClientMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new GetSemiproductRecipePdfRequest { ProductCode = productCode };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        _rendererMock.Verify(r => r.Render(It.Is<SemiproductRecipeData>(d =>
            d.Ingredients.All(i => i.Percentage == 0.0)
        )), Times.Once);
    }

    [Fact]
    public async Task Handle_CatalogWithExpirationMonths_PopulatesExpirationOnData()
    {
        // Arrange
        const string productCode = "EXP001";
        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Expiring Semiproduct",
            OriginalAmount = 1000.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient { ProductCode = "ING001", ProductName = "Ingredient A", Amount = 1000.0 },
            }
        };
        var catalogAggregate = new CatalogAggregate
        {
            MinimalManufactureQuantity = 500.0,
            Properties = new CatalogProperties { ExpirationMonths = 24 }
        };

        _manufactureClientMock.Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _catalogRepositoryMock.Setup(r => r.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogAggregate);

        var request = new GetSemiproductRecipePdfRequest { ProductCode = productCode };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        _rendererMock.Verify(r => r.Render(It.Is<SemiproductRecipeData>(d =>
            d.Mmq == 500.0 &&
            d.ExpirationMonths == 24
        )), Times.Once);
    }
}
