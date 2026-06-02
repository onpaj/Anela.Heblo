using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase.Infrastructure;

public class PurchaseCatalogSourceAdapterTests
{
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock = new();
    private readonly PurchaseCatalogSourceAdapter _adapter;

    public PurchaseCatalogSourceAdapterTests()
    {
        _adapter = new PurchaseCatalogSourceAdapter(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetOrderedQuantitiesAsync_DelegatesToRepositoryAndReturnsResult()
    {
        // Arrange
        var expected = new Dictionary<string, decimal>
        {
            ["PROD-A"] = 12.5m,
            ["PROD-B"] = 0m,
        };
        _repositoryMock
            .Setup(r => r.GetOrderedQuantitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _adapter.GetOrderedQuantitiesAsync(CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        _repositoryMock.Verify(r => r.GetOrderedQuantitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
