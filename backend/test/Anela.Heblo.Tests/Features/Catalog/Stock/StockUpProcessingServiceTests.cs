using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog.Stock;

public class StockUpProcessingServiceTests
{
    private readonly Mock<IStockUpOperationRepository> _repo = new();
    private readonly Mock<IEshopStockDomainService> _eshop = new();

    private StockUpProcessingService CreateService() =>
        new(_repo.Object, _eshop.Object, NullLogger<StockUpProcessingService>.Instance);

    private static StockUpOperation PendingOperation(string docNumber = "BOX-000001-AKL001") =>
        new(docNumber, "AKL001", 5, StockUpSourceType.TransportBox, 1);

    [Fact]
    public async Task ProcessPendingOperations_SuccessfulSubmit_MarksCompleted()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert — operation should be Completed after a successful REST call
        operation.State.Should().Be(StockUpOperationState.Completed);
    }


    [Fact]
    public async Task ProcessPendingOperations_StockUpAsyncThrows_MarksAsFailed()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .ThrowsAsync(new HttpRequestException("Shoptet stock update failed for AKL001: [unknown-product] Product does not exist."));

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert
        operation.State.Should().Be(StockUpOperationState.Failed);
        operation.ErrorMessage.Should().Contain("unknown-product");
    }

    [Fact]
    public async Task ProcessPendingOperations_CallsStockUpAsyncAndCompletes()
    {
        // Arrange
        var operation = PendingOperation();
        _repo.Setup(r => r.GetByStateAsync(StockUpOperationState.Pending, default))
             .ReturnsAsync([operation]);
        _eshop.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
              .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.ProcessPendingOperationsAsync();

        // Assert
        operation.State.Should().Be(StockUpOperationState.Completed);
        _eshop.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_DocumentNumberAlreadyExists_SkipsCreateAndDoesNotSave()
    {
        // Arrange — a prior (possibly interrupted) attempt already created this operation
        var existing = PendingOperation("BOX-000001-AKL001");
        _repo.Setup(r => r.GetByDocumentNumberAsync("BOX-000001-AKL001", It.IsAny<CancellationToken>()))
             .ReturnsAsync(existing);

        var service = CreateService();

        // Act — retrying the same create must be a safe no-op, not a duplicate insert
        await service.CreateOperationAsync("BOX-000001-AKL001", "AKL001", 5, StockUpSourceType.TransportBox, 1);

        // Assert
        _repo.Verify(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOperationAsync_DocumentNumberDoesNotExist_PersistImmediatelyDefaultTrue_AddsAndSaves()
    {
        // Arrange
        _repo.Setup(r => r.GetByDocumentNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((StockUpOperation?)null);

        var service = CreateService();

        // Act — persistImmediately omitted, must default to true (today's behavior for
        // existing callers such as GiftPackageManufactureService)
        await service.CreateOperationAsync("BOX-000002-AKL002", "AKL002", 3, StockUpSourceType.TransportBox, 2);

        // Assert
        _repo.Verify(r => r.AddAsync(It.Is<StockUpOperation>(op =>
            op.DocumentNumber == "BOX-000002-AKL002" &&
            op.ProductCode == "AKL002" &&
            op.Amount == 3 &&
            op.SourceType == StockUpSourceType.TransportBox &&
            op.SourceId == 2), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_PersistImmediatelyFalse_AddsButDoesNotSave()
    {
        // Arrange
        _repo.Setup(r => r.GetByDocumentNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((StockUpOperation?)null);

        var service = CreateService();

        // Act — deferred flush: caller is responsible for a later SaveChangesAsync so this
        // commits atomically together with other pending changes on the same DbContext
        await service.CreateOperationAsync(
            "BOX-000003-AKL003", "AKL003", 2, StockUpSourceType.TransportBox, 3,
            CancellationToken.None, persistImmediately: false);

        // Assert
        _repo.Verify(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
