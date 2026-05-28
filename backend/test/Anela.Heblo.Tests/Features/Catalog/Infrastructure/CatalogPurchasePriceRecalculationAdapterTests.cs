using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogPurchasePriceRecalculationAdapterTests
{
    private readonly Mock<IProductPriceErpClient> _erpClientMock = new();

    private CatalogPurchasePriceRecalculationAdapter CreateAdapter() =>
        new(_erpClientMock.Object);

    [Fact]
    public async Task RecalculatePurchasePriceAsync_WithValidBomId_DelegatesToProductPriceErpClient()
    {
        // Arrange
        var bomId = 123;
        var ct = CancellationToken.None;
        _erpClientMock
            .Setup(x => x.RecalculatePurchasePrice(bomId, ct))
            .Returns(Task.CompletedTask);

        var adapter = CreateAdapter();

        // Act
        await adapter.RecalculatePurchasePriceAsync(bomId, ct);

        // Assert
        _erpClientMock.Verify(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculatePurchasePriceAsync_WithCancellationToken_ForwardsTokenToErpClient()
    {
        // Arrange
        var bomId = 456;
        var cancellationToken = new CancellationToken(canceled: false);
        _erpClientMock
            .Setup(x => x.RecalculatePurchasePrice(bomId, cancellationToken))
            .Returns(Task.CompletedTask);

        var adapter = CreateAdapter();

        // Act
        await adapter.RecalculatePurchasePriceAsync(bomId, cancellationToken);

        // Assert
        _erpClientMock.Verify(
            x => x.RecalculatePurchasePrice(It.IsAny<int>(), cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task RecalculatePurchasePriceAsync_WhenErpClientThrows_PropagatesException()
    {
        // Arrange
        var bomId = 789;
        var ct = CancellationToken.None;
        _erpClientMock
            .Setup(x => x.RecalculatePurchasePrice(bomId, ct))
            .Throws(new InvalidOperationException("ERP error"));

        var adapter = CreateAdapter();

        // Act & Assert
        await adapter
            .Invoking(a => a.RecalculatePurchasePriceAsync(bomId, ct))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("ERP error");
    }
}
