using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase.Infrastructure;

public class PurchaseCatalogSourceAdapterTests
{
    private readonly Mock<IPurchaseOrderRepository> _purchaseOrderRepository = new();

    private PurchaseCatalogSourceAdapter CreateAdapter() =>
        new(_purchaseOrderRepository.Object);

    [Fact]
    public async Task GetOrderedQuantitiesAsync_DelegatesToRepository()
    {
        // Arrange
        var ct = CancellationToken.None;
        var expectedQuantities = new Dictionary<string, decimal>
        {
            { "PROD-1", 10.5m },
            { "PROD-2", 20.0m }
        };
        _purchaseOrderRepository
            .Setup(r => r.GetOrderedQuantitiesAsync(ct))
            .ReturnsAsync(expectedQuantities);

        // Act
        var result = await CreateAdapter().GetOrderedQuantitiesAsync(ct);

        // Assert
        result.Should().BeEquivalentTo(expectedQuantities);
    }

    [Fact]
    public async Task GetOrderedQuantitiesAsync_PassesCancellationTokenToRepository()
    {
        // Arrange
        var ct = new CancellationToken(false);
        _purchaseOrderRepository
            .Setup(r => r.GetOrderedQuantitiesAsync(ct))
            .ReturnsAsync(new Dictionary<string, decimal>());

        // Act
        await CreateAdapter().GetOrderedQuantitiesAsync(ct);

        // Assert
        _purchaseOrderRepository.Verify(
            r => r.GetOrderedQuantitiesAsync(ct),
            Times.Once);
    }
}
