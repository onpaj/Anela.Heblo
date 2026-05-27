using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

/// <summary>
/// Characterizes the behavior of AnalyticsRepository.StreamProductsWithSalesAsync
/// </summary>
public class AnalyticsRepositoryTests
{
    [Fact]
    public async Task StreamProductsWithSalesAsync_YieldsAllProductsInOrder()
    {
        // Arrange
        const int productCount = 250;

        var products = Enumerable.Range(0, productCount)
            .Select(i => new CatalogAggregate
            {
                ProductCode = $"P{i:D4}",
                ProductName = $"Product {i}",
                Type = ProductType.Product,
                SalesHistory = new List<CatalogSaleRecord>(),
                ConsumedHistory = new List<ConsumedMaterialRecord>(),
                PurchaseHistory = new List<CatalogPurchaseRecord>(),
                Stock = new(),
            })
            .ToList();

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock
            .Setup(x => x.GetProductsWithSalesInPeriod(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var repository = new AnalyticsRepository(catalogRepositoryMock.Object, null!);

        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);
        var productTypes = new[] { ProductType.Product };

        // Act
        var yielded = new List<AnalyticsProduct>();
        await foreach (var analyticsProduct in repository.StreamProductsWithSalesAsync(
            fromDate,
            toDate,
            productTypes,
            CancellationToken.None))
        {
            yielded.Add(analyticsProduct);
        }

        // Assert
        yielded.Should().HaveCount(productCount);

        yielded.Select(p => p.ProductCode)
            .Should()
            .Equal(products.Select(p => p.ProductCode));

        yielded.First().ProductCode.Should().Be("P0000");
        yielded.Last().ProductCode.Should().Be("P0249");

        // Batch boundary spot-checks (batch size is 100)
        yielded[99].ProductCode.Should().Be("P0099");
        yielded[100].ProductCode.Should().Be("P0100");
        yielded[199].ProductCode.Should().Be("P0199");
        yielded[200].ProductCode.Should().Be("P0200");
    }
}
