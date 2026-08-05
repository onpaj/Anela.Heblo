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
    public async Task StageOperationAsync_NoExistingDocument_AddsWithoutSaving()
    {
        // Arrange
        _repo.Setup(r => r.GetByDocumentNumberAsync("BOX-000001-AKL001", default))
             .ReturnsAsync((StockUpOperation?)null);

        var service = CreateService();

        // Act
        await service.StageOperationAsync("BOX-000001-AKL001", "AKL001", 5, StockUpSourceType.TransportBox, 1);

        // Assert — staged into the change tracker, but the caller owns the commit point
        _repo.Verify(r => r.AddAsync(It.Is<StockUpOperation>(op =>
                op.DocumentNumber == "BOX-000001-AKL001" &&
                op.ProductCode == "AKL001" &&
                op.Amount == 5 &&
                op.SourceType == StockUpSourceType.TransportBox &&
                op.SourceId == 1 &&
                op.State == StockUpOperationState.Pending),
            default),
            Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StageOperationAsync_ExistingDocument_SkipsWithoutAddingOrThrowing()
    {
        // Arrange — simulates a retry after rollback, or a legacy wedge predating this fix
        var existing = PendingOperation();
        _repo.Setup(r => r.GetByDocumentNumberAsync("BOX-000001-AKL001", default))
             .ReturnsAsync(existing);

        var service = CreateService();

        // Act
        var act = () => service.StageOperationAsync("BOX-000001-AKL001", "AKL001", 5, StockUpSourceType.TransportBox, 1);

        // Assert
        await act.Should().NotThrowAsync();
        _repo.Verify(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
