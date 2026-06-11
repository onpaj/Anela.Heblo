using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Catalog.UseCases.RetryStockUpOperation;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class RetryStockUpOperationHandlerTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock;
    private readonly Mock<ILogger<RetryStockUpOperationHandler>> _loggerMock;
    private readonly RetryStockUpOperationHandler _handler;
    private static readonly DateTime FixedNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public RetryStockUpOperationHandlerTests()
    {
        _repositoryMock = new Mock<IStockUpOperationRepository>();
        _loggerMock = new Mock<ILogger<RetryStockUpOperationHandler>>();
        _handler = new RetryStockUpOperationHandler(
            _repositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenOperationNotFound_ReturnsFailure()
    {
        // Arrange
        var request = new RetryStockUpOperationRequest { OperationId = 999 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.Failed, response.Status);
        Assert.Contains("999", response.ErrorMessage);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOperationIsCompleted_ReturnsAlreadyCompleted()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        operation.MarkAsCompleted(FixedNow);

        var request = new RetryStockUpOperationRequest { OperationId = 1 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.AlreadyCompleted, response.Status);
        Assert.Contains("DOC-001", response.ErrorMessage);
        Assert.Equal(StockUpOperationState.Completed, operation.State);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOperationIsFailed_CallsResetAndReturnsInProgress()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        operation.MarkAsFailed(FixedNow, "some error");

        var request = new RetryStockUpOperationRequest { OperationId = 1 };

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
        Assert.Equal(StockUpResultStatus.InProgress, response.Status);
        Assert.Null(response.ErrorMessage);

        // Observable Reset() post-state
        Assert.Equal(StockUpOperationState.Pending, operation.State);
        Assert.Null(operation.SubmittedAt);
        Assert.Null(operation.CompletedAt);
        Assert.Null(operation.ErrorMessage);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Branch selection signal: handler emits NO Warning on the Reset branch.
        // If this assertion ever fails, the handler has been switched to ForceReset
        // (or someone introduced a new Warning above this call site).
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOperationIsSubmitted_CallsForceResetAndReturnsInProgress()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        operation.MarkAsSubmitted(FixedNow);

        var request = new RetryStockUpOperationRequest { OperationId = 1 };

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
        Assert.Equal(StockUpResultStatus.InProgress, response.Status);
        Assert.Null(response.ErrorMessage);

        // Observable ForceReset() post-state
        Assert.Equal(StockUpOperationState.Pending, operation.State);
        Assert.Null(operation.SubmittedAt);
        Assert.Null(operation.CompletedAt);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Branch selection signal: the handler emits exactly one Warning
        // immediately before calling ForceReset(). If this drops to Times.Never,
        // the branch swap regression has landed.
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
    public async Task Handle_WhenOperationIsPending_CallsForceResetAndReturnsInProgress()
    {
        // Arrange — constructor leaves the operation in Pending state; no Mark* call needed.
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);

        var request = new RetryStockUpOperationRequest { OperationId = 1 };

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
        Assert.Equal(StockUpResultStatus.InProgress, response.Status);
        Assert.Null(response.ErrorMessage);

        // State remains Pending after ForceReset (Pending -> Pending is valid).
        Assert.Equal(StockUpOperationState.Pending, operation.State);
        Assert.Null(operation.SubmittedAt); // Pending operations never had SubmittedAt; harmless redundancy.
        Assert.Null(operation.CompletedAt);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Branch selection signal — same proxy as the Submitted case.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
