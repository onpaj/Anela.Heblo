using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog.Stock;

public class StockUpProcessingServiceTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock = new();
    private readonly Mock<IEshopStockDomainService> _eshopMock = new();

    private StockUpProcessingService CreateService() =>
        new(_repositoryMock.Object, _eshopMock.Object, NullLogger<StockUpProcessingService>.Instance);

    [Fact]
    public async Task ProcessPendingOperationsAsync_NoOperations_DoesNothing()
    {
        _repositoryMock
            .Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockUpOperation>());

        var service = CreateService();
        await service.ProcessPendingOperationsAsync();

        _eshopMock.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingOperationsAsync_SuccessfulSubmit_MarksCompleted()
    {
        var operation = new StockUpOperation("DOC-001", "AKL001", 10, StockUpSourceType.TransportBox, 1);
        _repositoryMock
            .Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockUpOperation> { operation });
        _eshopMock
            .Setup(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);
        _eshopMock
            .Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        await service.ProcessPendingOperationsAsync();

        Assert.Equal(StockUpOperationState.Completed, operation.State);
        _eshopMock.Verify(e => e.StockUpAsync(It.Is<StockUpRequest>(r =>
            r.Products.Any(p => p.ProductCode == "AKL001" && p.Amount == 10))), Times.Once);
        // Post-verify should NOT be called (removed) — only pre-check is allowed (at most once)
        _eshopMock.Verify(e => e.VerifyStockUpExistsAsync("DOC-001"), Times.AtMostOnce);
    }

    [Fact]
    public async Task ProcessPendingOperationsAsync_StockUpThrows_MarksAsFailed()
    {
        var operation = new StockUpOperation("DOC-001", "AKL001", 10, StockUpSourceType.TransportBox, 1);
        _repositoryMock
            .Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockUpOperation> { operation });
        _eshopMock
            .Setup(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);
        _eshopMock
            .Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
            .ThrowsAsync(new InvalidOperationException("REST error"));

        var service = CreateService();
        await service.ProcessPendingOperationsAsync();

        Assert.Equal(StockUpOperationState.Failed, operation.State);
    }
}
