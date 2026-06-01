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
}
