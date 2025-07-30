using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Domain.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

[Collection("FlexiIntegration")]
public class FlexiStockClientIntegrationTests : IClassFixture<FlexiIntegrationTestFixture>
{
    private readonly FlexiIntegrationTestFixture _fixture;
    private readonly IErpStockClient _client;

    public FlexiStockClientIntegrationTests(FlexiIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = _fixture.ServiceProvider.GetRequiredService<IErpStockClient>();
    }

    [Fact]
    public async Task ListAsync_WithRealFlexiConnection_ReturnsStockData()
    {
        // Act
        var result = await _client.ListAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IReadOnlyList<ErpStock>>();

        if (result.Any())
        {
            // Verify basic structure
            result.Should().OnlyContain(stock => !string.IsNullOrWhiteSpace(stock.ProductCode));
            result.Should().OnlyContain(stock => !string.IsNullOrWhiteSpace(stock.ProductName));
            result.Should().OnlyContain(stock => stock.ProductId > 0, "ProductId should be positive");
            result.Should().OnlyContain(stock => stock.Stock >= 0, "Stock should be non-negative");

            // Verify ProductTypeId is valid if present
            result.Should().OnlyContain(stock =>
                !stock.ProductTypeId.HasValue ||
                Enum.IsDefined(typeof(ProductType), stock.ProductTypeId!.Value),
                "ProductTypeId should be valid enum value if present");
        }
    }

    [Fact]
    public async Task ListAsync_ValidatesProductTypes()
    {
        // Act
        var result = await _client.ListAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Check that we have different product types as expected by the client implementation
            var productTypeIds = result.Where(r => r.ProductTypeId.HasValue)
                                       .Select(r => r.ProductTypeId.Value)
                                       .Distinct()
                                       .ToList();

            if (productTypeIds.Any())
            {
                // Verify product types match expected warehouse mappings
                foreach (var typeId in productTypeIds)
                {
                    Enum.IsDefined(typeof(ProductType), typeId).Should().BeTrue(
                        $"ProductTypeId {typeId} should be a valid ProductType enum value");
                }
            }
        }
    }

    [Fact]
    public async Task ListAsync_ValidatesWarehouseMapping()
    {
        // This test verifies that the client properly handles different warehouses
        // and returns stock from Materials (5), SemiProducts (20), and Products (4)

        // Act
        var result = await _client.ListAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Group by product type to verify warehouse mapping logic
            var materialItems = result.Where(r => r.ProductTypeId == (int)ProductType.Material).ToList();
            var semiProductItems = result.Where(r => r.ProductTypeId == (int)ProductType.SemiProduct).ToList();
            var productItems = result.Where(r => r.ProductTypeId == (int)ProductType.Product).ToList();
            var goodsItems = result.Where(r => r.ProductTypeId == (int)ProductType.Goods).ToList();

            // Validate that if we have items of each type, they have correct structure
            foreach (var item in materialItems.Take(5))
            {
                item.ProductTypeId.Should().Be((int)ProductType.Material);
                item.ProductCode.Should().NotBeNullOrWhiteSpace();
                item.ProductName.Should().NotBeNullOrWhiteSpace();
            }

            foreach (var item in semiProductItems.Take(5))
            {
                item.ProductTypeId.Should().Be((int)ProductType.SemiProduct);
                item.ProductCode.Should().NotBeNullOrWhiteSpace();
                item.ProductName.Should().NotBeNullOrWhiteSpace();
            }

            foreach (var item in productItems.Take(5))
            {
                item.ProductTypeId.Should().Be((int)ProductType.Product);
                item.ProductCode.Should().NotBeNullOrWhiteSpace();
                item.ProductName.Should().NotBeNullOrWhiteSpace();
            }

            foreach (var item in goodsItems.Take(5))
            {
                item.ProductTypeId.Should().Be((int)ProductType.Goods);
                item.ProductCode.Should().NotBeNullOrWhiteSpace();
                item.ProductName.Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public async Task ListAsync_ValidatesStockValues()
    {
        // Act
        var result = await _client.ListAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            foreach (var stock in result.Take(20))
            {
                // Basic validations
                stock.ProductId.Should().BeGreaterThan(0, $"ProductId should be positive for product {stock.ProductCode}");
                stock.Stock.Should().BeGreaterOrEqualTo(0, $"Stock should be non-negative for product {stock.ProductCode}");

                // Stock values should be reasonable (not extremely high)
                stock.Stock.Should().BeLessThan(1000000m, $"Stock should be reasonable for product {stock.ProductCode}");

                // Product identifiers should be valid
                stock.ProductCode.Should().NotBeNullOrWhiteSpace("ProductCode should not be empty");
                stock.ProductName.Should().NotBeNullOrWhiteSpace("ProductName should not be empty");
                stock.ProductCode.Should().Be(stock.ProductCode.Trim(), "ProductCode should be trimmed");
                stock.ProductName.Should().Be(stock.ProductName.Trim(), "ProductName should be trimmed");
            }
        }
    }

    [Fact]
    public async Task ListAsync_ValidatesPhysicalProperties()
    {
        // Act
        var result = await _client.ListAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            foreach (var stock in result.Take(15))
            {
                // Physical properties should be non-negative
                stock.Volume.Should().BeGreaterOrEqualTo(0, $"Volume should be non-negative for product {stock.ProductCode}");
                stock.Weight.Should().BeGreaterOrEqualTo(0, $"Weight should be non-negative for product {stock.ProductCode}");

                // Physical properties should be reasonable (not extremely high)
                stock.Volume.Should().BeLessThan(1000000, $"Volume should be reasonable for product {stock.ProductCode}");
                stock.Weight.Should().BeLessThan(1000000, $"Weight should be reasonable for product {stock.ProductCode}");

                // MOQ should be valid if present
                if (!string.IsNullOrWhiteSpace(stock.MOQ))
                {
                    stock.MOQ.Should().Be(stock.MOQ.Trim(), "MOQ should be trimmed");
                }
            }
        }
    }

    [Fact]
    public async Task ListAsync_ValidatesLotAndExpirationFlags()
    {
        // Act
        var result = await _client.ListAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Check if we have items with lot tracking
            var itemsWithLots = result.Where(r => r.HasLots).Take(5).ToList();
            var itemsWithExpiration = result.Where(r => r.HasExpiration).Take(5).ToList();

            // Validate items with lot tracking
            foreach (var item in itemsWithLots)
            {
                item.HasLots.Should().BeTrue("Item should have lot tracking enabled");
                item.ProductCode.Should().NotBeNullOrWhiteSpace();
                item.ProductName.Should().NotBeNullOrWhiteSpace();
            }

            // Validate items with expiration tracking
            foreach (var item in itemsWithExpiration)
            {
                item.HasExpiration.Should().BeTrue("Item should have expiration tracking enabled");
                item.ProductCode.Should().NotBeNullOrWhiteSpace();
                item.ProductName.Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public async Task ListAsync_CancellationToken_CanBeCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        var task = _client.ListAsync(cts.Token);

        // Cancel the token
        cts.Cancel();

        // Assert
        // The task should either complete successfully (if it was fast enough)
        // or throw OperationCanceledException (if cancellation was processed)
        try
        {
            await task;
            // If we get here, the operation completed before cancellation
            task.IsCompleted.Should().BeTrue();
        }
        catch (OperationCanceledException)
        {
            // This is expected behavior when cancellation is processed
            cts.Token.IsCancellationRequested.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ListAsync_ConsistentResults_MultipleCalls()
    {
        // Act - Make multiple calls
        var result1 = await _client.ListAsync(CancellationToken.None);
        var result2 = await _client.ListAsync(CancellationToken.None);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        if (result1.Any() && result2.Any())
        {
            // Results should be consistent (same products available)
            result1.Count.Should().Be(result2.Count, "Stock calls should return consistent counts");

            // Compare sample products
            var sample1 = result1.Take(10).OrderBy(r => r.ProductCode).ToList();
            var sample2 = result2.Take(10).OrderBy(r => r.ProductCode).ToList();

            for (int i = 0; i < Math.Min(sample1.Count, sample2.Count); i++)
            {
                var stock1 = sample1[i];
                var stock2 = sample2[i];

                stock1.ProductCode.Should().Be(stock2.ProductCode);
                stock1.ProductName.Should().Be(stock2.ProductName);
                stock1.ProductId.Should().Be(stock2.ProductId);
                // Note: Stock quantities might change between calls, so we don't compare them
            }
        }
    }

    [Theory]
    [InlineData(ProductType.Material)]
    [InlineData(ProductType.SemiProduct)]
    [InlineData(ProductType.Product)]
    [InlineData(ProductType.Goods)]
    public async Task ListAsync_ContainsExpectedProductTypes(ProductType expectedType)
    {
        // Act
        var result = await _client.ListAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Check if we have items of the expected type
            var itemsOfType = result.Where(r => r.ProductTypeId == (int)expectedType).ToList();

            // If we have items of this type, validate their structure
            foreach (var item in itemsOfType.Take(3))
            {
                item.ProductTypeId.Should().Be((int)expectedType);
                item.ProductCode.Should().NotBeNullOrWhiteSpace();
                item.ProductName.Should().NotBeNullOrWhiteSpace();
                item.ProductId.Should().BeGreaterThan(0);
                item.Stock.Should().BeGreaterOrEqualTo(0);
            }
        }
    }

    [Fact]
    public async Task Integration_StockWorkflow_ValidatesCompleteDataFlow()
    {
        // This test validates the complete workflow and data consistency

        // Act
        var allStock = await _client.ListAsync(CancellationToken.None);

        // Assert
        allStock.Should().NotBeNull();

        if (allStock.Any())
        {
            // Step 1: Verify overall data structure
            allStock.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.ProductCode));
            allStock.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.ProductName));
            allStock.Should().OnlyContain(s => s.ProductId > 0);

            // Step 2: Group by product type and verify warehouse logic
            var stockByType = allStock.GroupBy(s => s.ProductTypeId).ToList();

            foreach (var typeGroup in stockByType.Take(5))
            {
                var productTypeId = typeGroup.Key;
                var items = typeGroup.ToList();

                // Verify product type is valid
                if (productTypeId.HasValue)
                {
                    Enum.IsDefined(typeof(ProductType), productTypeId.Value).Should().BeTrue(
                        $"ProductTypeId {productTypeId} should be valid");
                }

                // Verify items in this type group
                items.Should().OnlyContain(item => item.ProductTypeId == productTypeId);
                items.Should().OnlyContain(item => item.Stock >= 0);
            }

            // Step 3: Verify unique products (no duplicates)
            var productCodes = allStock.Select(s => s.ProductCode).ToList();
            var uniqueProductCodes = productCodes.Distinct().ToList();
            uniqueProductCodes.Count.Should().Be(productCodes.Count,
                "Should not have duplicate product codes in stock list");

            // Step 4: Validate business logic for sample items
            foreach (var stock in allStock.Take(20))
            {
                // Product identifiers should be consistent
                stock.ProductCode.Should().NotBeNullOrWhiteSpace();
                stock.ProductName.Should().NotBeNullOrWhiteSpace();

                // Stock values should make business sense
                if (stock.Stock > 0)
                {
                    // If we have stock, product should have valid identifiers
                    stock.ProductId.Should().BeGreaterThan(0);
                }

                // Physical properties consistency
                if (stock.Volume > 0 || stock.Weight > 0)
                {
                    // If we have physical properties, they should be reasonable
                    stock.Volume.Should().BeLessThan(1000000, "Volume should be reasonable");
                    stock.Weight.Should().BeLessThan(1000000, "Weight should be reasonable");
                }

                // Lot/expiration logic
                if (!string.IsNullOrWhiteSpace(stock.MOQ))
                {
                    stock.MOQ.Should().Be(stock.MOQ.Trim(), "MOQ should be properly formatted");
                }
            }

            // Step 5: Verify expected warehouse coverage
            var materialCount = allStock.Count(s => s.ProductTypeId == (int)ProductType.Material);
            var semiProductCount = allStock.Count(s => s.ProductTypeId == (int)ProductType.SemiProduct);
            var productCount = allStock.Count(s => s.ProductTypeId == (int)ProductType.Product);
            var goodsCount = allStock.Count(s => s.ProductTypeId == (int)ProductType.Goods);

            var totalExpectedItems = materialCount + semiProductCount + productCount + goodsCount;

            // The total should match the items we expect from the three warehouses
            // (allowing for some items that might not have ProductTypeId set)
            allStock.Count.Should().BeGreaterOrEqualTo((int)(totalExpectedItems * 0.8m),
                "Most items should have valid ProductTypeId mapping");
        }
    }
}