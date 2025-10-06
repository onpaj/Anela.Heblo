using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CalculatedBatchSizeHandlerLastStockTakingTests
{
    private readonly Mock<IManufactureRepository> _mockManufactureRepository;
    private readonly Mock<ICatalogRepository> _mockCatalogRepository;
    private readonly CalculatedBatchSizeHandler _handler;

    public CalculatedBatchSizeHandlerLastStockTakingTests()
    {
        _mockManufactureRepository = new Mock<IManufactureRepository>();
        _mockCatalogRepository = new Mock<ICatalogRepository>();
        _handler = new CalculatedBatchSizeHandler(_mockManufactureRepository.Object, _mockCatalogRepository.Object);
    }

    [Fact]
    public async Task Handle_ShouldIncludeLastStockTakingDate_WhenIngredientHasStockTakingHistory()
    {
        // Arrange
        var productCode = "TEST-PRODUCT";
        var request = new CalculatedBatchSizeRequest 
        { 
            ProductCode = productCode,
            DesiredBatchSize = 1000.0
        };

        var stockTakingDate1 = DateTime.Now.AddDays(-30);
        var stockTakingDate2 = DateTime.Now.AddDays(-10); // Most recent

        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            OriginalAmount = 500.0,
            BatchSize = 500.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient
                {
                    ProductCode = "INGREDIENT-1",
                    ProductName = "Test Ingredient 1",
                    Amount = 100.0,
                    Price = 5.0m
                },
                new Ingredient
                {
                    ProductCode = "INGREDIENT-2",
                    ProductName = "Test Ingredient 2",
                    Amount = 200.0,
                    Price = 10.0m
                }
            }
        };

        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            MinimalManufactureQuantity = 500.0
        };

        var ingredient1 = new CatalogAggregate
        {
            ProductCode = "INGREDIENT-1",
            ProductName = "Test Ingredient 1",
            Stock = new StockData { Erp = 1000.0m },
            StockTakingHistory = new List<StockTakingRecord>
            {
                new StockTakingRecord { Date = stockTakingDate1, Code = "INGREDIENT-1" },
                new StockTakingRecord { Date = stockTakingDate2, Code = "INGREDIENT-1" } // Most recent
            }
        };

        var ingredient2 = new CatalogAggregate
        {
            ProductCode = "INGREDIENT-2",
            ProductName = "Test Ingredient 2",
            Stock = new StockData { Erp = 2000.0m },
            StockTakingHistory = new List<StockTakingRecord>() // No stock taking history
        };

        _mockManufactureRepository
            .Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync("INGREDIENT-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ingredient1);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync("INGREDIENT-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ingredient2);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Ingredients);
        Assert.Equal(2, result.Ingredients.Count);

        // Check ingredient 1 (has stock taking history)
        var resultIngredient1 = result.Ingredients.First(i => i.ProductCode == "INGREDIENT-1");
        Assert.NotNull(resultIngredient1.LastStockTaking);
        Assert.Equal(stockTakingDate2, resultIngredient1.LastStockTaking.Value);

        // Check ingredient 2 (no stock taking history)
        var resultIngredient2 = result.Ingredients.First(i => i.ProductCode == "INGREDIENT-2");
        Assert.Null(resultIngredient2.LastStockTaking);
    }

    [Fact]
    public async Task Handle_ShouldReturnNullLastStockTaking_WhenIngredientHasNoStockTakingHistory()
    {
        // Arrange
        var productCode = "TEST-PRODUCT";
        var request = new CalculatedBatchSizeRequest 
        { 
            ProductCode = productCode,
            DesiredBatchSize = 1000.0
        };

        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            OriginalAmount = 500.0,
            BatchSize = 500.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient
                {
                    ProductCode = "INGREDIENT-1",
                    ProductName = "Test Ingredient 1",
                    Amount = 100.0,
                    Price = 5.0m
                }
            }
        };

        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            MinimalManufactureQuantity = 500.0
        };

        var ingredient = new CatalogAggregate
        {
            ProductCode = "INGREDIENT-1",
            ProductName = "Test Ingredient 1",
            Stock = new StockData { Erp = 1000.0m },
            StockTakingHistory = new List<StockTakingRecord>() // No stock taking history
        };

        _mockManufactureRepository
            .Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync("INGREDIENT-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ingredient);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Ingredients);
        Assert.Single(result.Ingredients);

        var resultIngredient = result.Ingredients.First();
        Assert.Null(resultIngredient.LastStockTaking);
    }

    [Fact]
    public async Task Handle_ShouldReturnNullLastStockTaking_WhenIngredientNotFound()
    {
        // Arrange
        var productCode = "TEST-PRODUCT";
        var request = new CalculatedBatchSizeRequest 
        { 
            ProductCode = productCode,
            DesiredBatchSize = 1000.0
        };

        var template = new ManufactureTemplate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            OriginalAmount = 500.0,
            BatchSize = 500.0,
            Ingredients = new List<Ingredient>
            {
                new Ingredient
                {
                    ProductCode = "INGREDIENT-1",
                    ProductName = "Test Ingredient 1",
                    Amount = 100.0,
                    Price = 5.0m
                }
            }
        };

        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            MinimalManufactureQuantity = 500.0
        };

        _mockManufactureRepository
            .Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync("INGREDIENT-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null); // Ingredient not found

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Ingredients);
        Assert.Single(result.Ingredients);

        var resultIngredient = result.Ingredients.First();
        Assert.Null(resultIngredient.LastStockTaking);
        Assert.Equal(0m, resultIngredient.StockTotal);
    }
}