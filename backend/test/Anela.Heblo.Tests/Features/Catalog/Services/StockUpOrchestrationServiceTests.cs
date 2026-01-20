using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Services;

/// <summary>
/// Unit tests for StockUpOrchestrationService.
/// Tests all 4 layers of protection against duplicates without calling real Shoptet.
/// </summary>
public class StockUpOrchestrationServiceTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock;
    private readonly Mock<IEshopStockDomainService> _eshopServiceMock;
    private readonly Mock<ILogger<StockUpOrchestrationService>> _loggerMock;
    private readonly StockUpOrchestrationService _service;

    private const string TestDocumentNumber = "BOX-000123-TEST001";
    private const string TestProductCode = "TEST001";
    private const int TestAmount = 10;
    private const StockUpSourceType TestSourceType = StockUpSourceType.TransportBox;
    private const int TestSourceId = 123;

    public StockUpOrchestrationServiceTests()
    {
        _repositoryMock = new Mock<IStockUpOperationRepository>();
        _eshopServiceMock = new Mock<IEshopStockDomainService>();
        _loggerMock = new Mock<ILogger<StockUpOrchestrationService>>();

        _service = new StockUpOrchestrationService(
            _repositoryMock.Object,
            _eshopServiceMock.Object,
            _loggerMock.Object);
    }

    #region Layer 1: UNIQUE Constraint Protection

    [Fact]
    public async Task ExecuteAsync_WhenDocumentNumberIsNew_CreatesNewOperation()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation op, CancellationToken ct) => op);

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var verifyCallCount = 0;
        _eshopServiceMock.Setup(e => e.VerifyStockUpExistsAsync(TestDocumentNumber))
            .ReturnsAsync(() =>
            {
                verifyCallCount++;
                return verifyCallCount == 2; // Pre-check: false, Post-verify: true
            });

        _eshopServiceMock.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.Success);
        result.IsSuccess.Should().BeTrue();
        result.Operation.Should().NotBeNull();
        result.Operation!.State.Should().Be(StockUpOperationState.Completed);
        result.Operation.DocumentNumber.Should().Be(TestDocumentNumber);

        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(3));
        _eshopServiceMock.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDocumentNumberAlreadyExistsAndCompleted_ReturnsAlreadyCompleted()
    {
        // Arrange
        var existingOperation = CreateCompletedOperation();

        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateUniqueConstraintException());

        _repositoryMock.Setup(r => r.GetByDocumentNumberAsync(TestDocumentNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOperation);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.AlreadyCompleted);
        result.IsSuccess.Should().BeTrue();
        result.Operation.Should().Be(existingOperation);

        // Should NOT call Shoptet at all
        _eshopServiceMock.Verify(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()), Times.Never);
        _eshopServiceMock.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationPreviouslyFailed_ReturnsPreviouslyFailed()
    {
        // Arrange
        var failedOperation = CreateFailedOperation();

        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateUniqueConstraintException());

        _repositoryMock.Setup(r => r.GetByDocumentNumberAsync(TestDocumentNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedOperation);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.PreviouslyFailed);
        result.IsSuccess.Should().BeFalse();
        result.Operation.Should().Be(failedOperation);
        result.Operation!.ErrorMessage.Should().NotBeNullOrEmpty();

        // Should NOT call Shoptet at all
        _eshopServiceMock.Verify(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()), Times.Never);
        _eshopServiceMock.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationInProgress_ReturnsInProgress()
    {
        // Arrange
        var submittedOperation = CreateSubmittedOperation();

        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateUniqueConstraintException());

        _repositoryMock.Setup(r => r.GetByDocumentNumberAsync(TestDocumentNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submittedOperation);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.InProgress);
        result.IsSuccess.Should().BeFalse();
        result.Operation.Should().Be(submittedOperation);

        // Should NOT call Shoptet at all
        _eshopServiceMock.Verify(e => e.VerifyStockUpExistsAsync(It.IsAny<string>()), Times.Never);
        _eshopServiceMock.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Never);
    }

    #endregion

    #region Layer 3: Pre-Submit Check

    [Fact]
    public async Task ExecuteAsync_WhenDocumentExistsInShoptet_MarksCompletedWithoutSubmit()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation op, CancellationToken ct) => op);

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eshopServiceMock.Setup(e => e.VerifyStockUpExistsAsync(TestDocumentNumber))
            .ReturnsAsync(true); // Pre-check: already in Shoptet

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.AlreadyInShoptet);
        result.IsSuccess.Should().BeTrue();
        result.Operation.Should().NotBeNull();
        result.Operation!.State.Should().Be(StockUpOperationState.Completed);

        // Should NOT submit to Shoptet (skip submit phase)
        _eshopServiceMock.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Never);
        _eshopServiceMock.Verify(e => e.VerifyStockUpExistsAsync(TestDocumentNumber), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPreCheckFails_ContinuesWithSubmit()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation op, CancellationToken ct) => op);

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var preCheckCallCount = 0;
        _eshopServiceMock.Setup(e => e.VerifyStockUpExistsAsync(TestDocumentNumber))
            .ReturnsAsync(() =>
            {
                preCheckCallCount++;
                if (preCheckCallCount == 1)
                    throw new Exception("Pre-check network error");
                return true; // Post-verify succeeds
            });

        _eshopServiceMock.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.Success);
        result.IsSuccess.Should().BeTrue();

        // Should proceed with submit despite pre-check error
        _eshopServiceMock.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Once);
        _eshopServiceMock.Verify(e => e.VerifyStockUpExistsAsync(TestDocumentNumber), Times.Exactly(2));
    }

    #endregion

    #region Submit Phase

    [Fact]
    public async Task ExecuteAsync_WhenSubmitFails_MarksOperationAsFailed()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation op, CancellationToken ct) => op);

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eshopServiceMock.Setup(e => e.VerifyStockUpExistsAsync(TestDocumentNumber))
            .ReturnsAsync(false); // Pre-check: not in Shoptet

        var submitException = new Exception("Network timeout");
        _eshopServiceMock.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
            .ThrowsAsync(submitException);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.Failed);
        result.IsSuccess.Should().BeFalse();
        result.Operation.Should().NotBeNull();
        result.Operation!.State.Should().Be(StockUpOperationState.Failed);
        result.Operation.ErrorMessage.Should().Contain("Submit failed");
        result.Exception.Should().Be(submitException);

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    #endregion

    #region Verification Phase

    [Fact]
    public async Task ExecuteAsync_WhenVerificationFails_MarksOperationAsFailed()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation op, CancellationToken ct) => op);

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var verifyCallCount = 0;
        _eshopServiceMock.Setup(e => e.VerifyStockUpExistsAsync(TestDocumentNumber))
            .ReturnsAsync(() =>
            {
                verifyCallCount++;
                return verifyCallCount == 1 ? false : false; // Pre-check: false, Post-verify: false
            });

        _eshopServiceMock.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.Failed);
        result.IsSuccess.Should().BeFalse();
        result.Operation.Should().NotBeNull();
        result.Operation!.State.Should().Be(StockUpOperationState.Failed);
        result.Operation.ErrorMessage.Should().Contain("Verification failed");

        _eshopServiceMock.Verify(e => e.VerifyStockUpExistsAsync(TestDocumentNumber), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerificationThrowsException_MarksOperationAsFailed()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation op, CancellationToken ct) => op);

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var verifyCallCount = 0;
        var verificationException = new Exception("Playwright timeout");
        _eshopServiceMock.Setup(e => e.VerifyStockUpExistsAsync(TestDocumentNumber))
            .ReturnsAsync(() =>
            {
                verifyCallCount++;
                if (verifyCallCount == 1)
                    return false; // Pre-check succeeds
                throw verificationException; // Post-verify throws
            });

        _eshopServiceMock.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.Failed);
        result.IsSuccess.Should().BeFalse();
        result.Operation.Should().NotBeNull();
        result.Operation!.State.Should().Be(StockUpOperationState.Failed);
        result.Operation.ErrorMessage.Should().Contain("Verification error");
        result.Exception.Should().Be(verificationException);
    }

    #endregion

    #region Complete Workflow Tests

    [Fact]
    public async Task ExecuteAsync_CompleteSuccessWorkflow_TransitionsThroughAllStates()
    {
        // Arrange
        StockUpOperation? capturedOperation = null;
        var saveCallCount = 0;

        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<StockUpOperation>(), It.IsAny<CancellationToken>()))
            .Callback<StockUpOperation, CancellationToken>((op, ct) => capturedOperation = op)
            .ReturnsAsync((StockUpOperation op, CancellationToken ct) => op);

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                saveCallCount++;
                // Capture state at each save
                if (capturedOperation != null)
                {
                    switch (saveCallCount)
                    {
                        case 1:
                            capturedOperation.State.Should().Be(StockUpOperationState.Pending);
                            break;
                        case 2:
                            capturedOperation.State.Should().Be(StockUpOperationState.Submitted);
                            break;
                        case 3:
                            capturedOperation.State.Should().Be(StockUpOperationState.Completed);
                            break;
                    }
                }
            })
            .ReturnsAsync(1);

        var verifyCallCount = 0;
        _eshopServiceMock.Setup(e => e.VerifyStockUpExistsAsync(TestDocumentNumber))
            .ReturnsAsync(() =>
            {
                verifyCallCount++;
                return verifyCallCount == 2; // Pre-check: false, Post-verify: true
            });

        _eshopServiceMock.Setup(e => e.StockUpAsync(It.IsAny<StockUpRequest>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(StockUpResultStatus.Success);
        result.IsSuccess.Should().BeTrue();
        result.Operation.Should().NotBeNull();
        result.Operation!.State.Should().Be(StockUpOperationState.Completed);

        // Verify complete workflow
        saveCallCount.Should().Be(3, "Should save after: 1) Create, 2) Submit, 3) Complete");
        _eshopServiceMock.Verify(e => e.VerifyStockUpExistsAsync(TestDocumentNumber), Times.Exactly(2));
        _eshopServiceMock.Verify(e => e.StockUpAsync(It.IsAny<StockUpRequest>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static StockUpOperation CreateDefaultOperation()
    {
        return new StockUpOperation(
            TestDocumentNumber,
            TestProductCode,
            TestAmount,
            TestSourceType,
            TestSourceId);
    }

    private static StockUpOperation CreateSubmittedOperation()
    {
        var operation = CreateDefaultOperation();
        operation.MarkAsSubmitted(DateTime.UtcNow);
        return operation;
    }

    private static StockUpOperation CreateCompletedOperation()
    {
        var operation = CreateDefaultOperation();
        operation.MarkAsCompleted(DateTime.UtcNow);
        return operation;
    }

    private static StockUpOperation CreateFailedOperation()
    {
        var operation = CreateDefaultOperation();
        operation.MarkAsFailed(DateTime.UtcNow, "Test error: Network timeout");
        return operation;
    }

    private static DbUpdateException CreateUniqueConstraintException()
    {
        var innerException = new Exception("duplicate key value violates unique constraint \"IX_StockUpOperations_DocumentNumber_Unique\" (23505)");
        return new DbUpdateException("An error occurred while saving the entity changes", innerException);
    }

    #endregion
}
