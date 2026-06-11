using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Persistence.Features.Analytics;
using Anela.Heblo.Domain.Features.Analytics;
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
    public async Task StreamProductsWithSalesAsync_DelegatesToProductSource()
    {
        // Arrange
        const int productCount = 250;

        var products = Enumerable.Range(0, productCount)
            .Select(i => new AnalyticsProduct
            {
                ProductCode = $"P{i:D4}",
                ProductName = $"Product {i}",
                Type = AnalyticsProductType.Product,
                MarginAmount = 0m,
                SalesHistory = new List<SalesDataPoint>(),
            })
            .ToList();

        var productSourceMock = new Mock<IAnalyticsProductSource>();
        productSourceMock
            .Setup(x => x.StreamProductsWithSalesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<AnalyticsProductType[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(products));

        var repository = new AnalyticsRepository(productSourceMock.Object, null!, null!);

        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);
        var productTypes = new[] { AnalyticsProductType.Product };

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

        productSourceMock.Verify(
            x => x.StreamProductsWithSalesAsync(fromDate, toDate, productTypes, CancellationToken.None),
            Times.Once);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}
