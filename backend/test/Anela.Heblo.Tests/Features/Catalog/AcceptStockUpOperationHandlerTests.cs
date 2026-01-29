using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class AcceptStockUpOperationHandlerTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock;
    private readonly Mock<ILogger<AcceptStockUpOperationHandler>> _loggerMock;
    private readonly AcceptStockUpOperationHandler _handler;

    public AcceptStockUpOperationHandlerTests()
    {
        _repositoryMock = new Mock<IStockUpOperationRepository>();
        _loggerMock = new Mock<ILogger<AcceptStockUpOperationHandler>>();
        _handler = new AcceptStockUpOperationHandler(
            _repositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenOperationNotFound_ReturnsFailure()
    {
        // Arrange
        var request = new AcceptStockUpOperationRequest { OperationId = 999 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.Failed, response.Status);
        Assert.Equal("Stock-up operation with ID 999 not found", response.ErrorMessage);

        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOperationNotFailed_ReturnsFailure()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        // Operation is in Pending state (not Failed)

        var request = new AcceptStockUpOperationRequest { OperationId = 1 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.Failed, response.Status);
        Assert.Contains("Can only accept Failed operations", response.ErrorMessage);

        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        // Verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOperationIsFailed_AcceptsAndReturnsSuccess()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        operation.MarkAsFailed(DateTime.UtcNow, "API timeout");

        var request = new AcceptStockUpOperationRequest { OperationId = 1 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(StockUpResultStatus.Success, response.Status);
        Assert.Null(response.ErrorMessage);

        // Verify domain logic was called (operation should now be Completed)
        Assert.Equal(StockUpOperationState.Completed, operation.State);
        Assert.NotNull(operation.ErrorMessage);
        Assert.Contains("API timeout", operation.ErrorMessage);
        Assert.Contains("Manually accepted", operation.ErrorMessage);

        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUnexpectedExceptionOccurs_ReturnsFailure()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        operation.MarkAsFailed(DateTime.UtcNow, "Original error");

        var request = new AcceptStockUpOperationRequest { OperationId = 1 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.Failed, response.Status);
        Assert.Contains("An unexpected error occurred", response.ErrorMessage);

        // Verify error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
