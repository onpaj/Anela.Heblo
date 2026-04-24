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
    private readonly Mock<IManufactureClient> _mockManufactureClient;
    private readonly Mock<ICatalogRepository> _mockCatalogRepository;
    private readonly CalculatedBatchSizeHandler _handler;

    public CalculatedBatchSizeHandlerLastStockTakingTests()
    {
        _mockManufactureClient = new Mock<IManufactureClient>();
        _mockCatalogRepository = new Mock<ICatalogRepository>();
        _handler = new CalculatedBatchSizeHandler(_mockManufactureClient.Object, _mockCatalogRepository.Object);
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

        _mockManufactureClient
            .Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockCatalogRepository
            .Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                new List<CatalogAggregate> { ingredient1, ingredient2 }
                    .Where(i => ids.Contains(i.ProductCode)).ToList());

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

        _mockManufactureClient
            .Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockCatalogRepository
            .Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                new List<CatalogAggregate> { ingredient }
                    .Where(i => ids.Contains(i.ProductCode)).ToList());

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

        _mockManufactureClient
            .Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockCatalogRepository
            .Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>()); // No ingredients found

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

    [Fact]
    public async Task Handle_ShouldFetchAllIngredients_WithSingleBulkCall()
    {
        // Regression test: verifies N+1 is eliminated — GetByIdsAsync is called once for all ingredients,
        // not GetByIdAsync per ingredient.
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
                new Ingredient { ProductCode = "ING-A", ProductName = "Ingredient A", Amount = 50.0, Price = 1.0m },
                new Ingredient { ProductCode = "ING-B", ProductName = "Ingredient B", Amount = 75.0, Price = 2.0m },
                new Ingredient { ProductCode = "ING-C", ProductName = "Ingredient C", Amount = 25.0, Price = 3.0m },
            }
        };

        var product = new CatalogAggregate { ProductCode = productCode, MinimalManufactureQuantity = 500.0 };

        _mockManufactureClient
            .Setup(x => x.GetManufactureTemplateAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockCatalogRepository
            .Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockCatalogRepository
            .Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert: bulk method called exactly once with all 3 ingredient codes
        _mockCatalogRepository.Verify(
            x => x.GetByIdsAsync(
                It.Is<IEnumerable<string>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { "ING-A", "ING-B", "ING-C" }.OrderBy(i => i))),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: per-ingredient GetByIdAsync was NOT called for ingredient codes
        _mockCatalogRepository.Verify(
            x => x.GetByIdAsync(It.Is<string>(id => id != productCode), It.IsAny<CancellationToken>()),
            Times.Never);

        Assert.True(result.Success);
        Assert.Equal(3, result.Ingredients.Count);
    }
}
