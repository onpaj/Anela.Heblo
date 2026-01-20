using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Stock;

/// <summary>
/// Unit tests for StockUpOperation entity.
/// Tests domain logic and state transitions without external dependencies.
/// </summary>
public class StockUpOperationTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesOperationInPendingState()
    {
        // Arrange
        var documentNumber = "BOX-000123-TEST001";
        var productCode = "TEST001";
        var amount = 10;
        var sourceType = StockUpSourceType.TransportBox;
        var sourceId = 123;

        // Act
        var operation = new StockUpOperation(documentNumber, productCode, amount, sourceType, sourceId);

        // Assert
        operation.DocumentNumber.Should().Be(documentNumber);
        operation.ProductCode.Should().Be(productCode);
        operation.Amount.Should().Be(amount);
        operation.SourceType.Should().Be(sourceType);
        operation.SourceId.Should().Be(sourceId);
        operation.State.Should().Be(StockUpOperationState.Pending);
        operation.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        operation.SubmittedAt.Should().BeNull();
        operation.VerifiedAt.Should().BeNull();
        operation.CompletedAt.Should().BeNull();
        operation.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidDocumentNumber_ThrowsValidationException(string? invalidDocumentNumber)
    {
        // Act
        Action act = () => new StockUpOperation(
            invalidDocumentNumber!,
            "TEST001",
            10,
            StockUpSourceType.TransportBox,
            123);

        // Assert
        act.Should().Throw<ValidationException>()
            .WithMessage("DocumentNumber is required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidProductCode_ThrowsValidationException(string? invalidProductCode)
    {
        // Act
        Action act = () => new StockUpOperation(
            "BOX-000123-TEST001",
            invalidProductCode!,
            10,
            StockUpSourceType.TransportBox,
            123);

        // Assert
        act.Should().Throw<ValidationException>()
            .WithMessage("ProductCode is required");
    }

    [Fact]
    public void Constructor_WithZeroAmount_ThrowsValidationException()
    {
        // Act
        Action act = () => new StockUpOperation(
            "BOX-000123-TEST001",
            "TEST001",
            0,
            StockUpSourceType.TransportBox,
            123);

        // Assert
        act.Should().Throw<ValidationException>()
            .WithMessage("Amount cannot be zero");
    }

    #endregion

    #region MarkAsSubmitted Tests

    [Fact]
    public void MarkAsSubmitted_FromPendingState_ChangesStateToSubmitted()
    {
        // Arrange
        var operation = CreateDefaultOperation();
        var submittedAt = DateTime.UtcNow;

        // Act
        operation.MarkAsSubmitted(submittedAt);

        // Assert
        operation.State.Should().Be(StockUpOperationState.Submitted);
        operation.SubmittedAt.Should().Be(submittedAt);
    }

    [Theory]
    [InlineData(StockUpOperationState.Submitted)]
    [InlineData(StockUpOperationState.Verified)]
    [InlineData(StockUpOperationState.Completed)]
    [InlineData(StockUpOperationState.Failed)]
    public void MarkAsSubmitted_FromInvalidState_ThrowsInvalidOperationException(StockUpOperationState invalidState)
    {
        // Arrange
        var operation = CreateOperationInState(invalidState);

        // Act
        Action act = () => operation.MarkAsSubmitted(DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Cannot mark as Submitted from {invalidState} state");
    }

    #endregion

    #region MarkAsVerified Tests

    [Fact]
    public void MarkAsVerified_FromSubmittedState_ChangesStateToVerified()
    {
        // Arrange
        var operation = CreateDefaultOperation();
        operation.MarkAsSubmitted(DateTime.UtcNow);
        var verifiedAt = DateTime.UtcNow.AddSeconds(1);

        // Act
        operation.MarkAsVerified(verifiedAt);

        // Assert
        operation.State.Should().Be(StockUpOperationState.Verified);
        operation.VerifiedAt.Should().Be(verifiedAt);
    }

    [Theory]
    [InlineData(StockUpOperationState.Pending)]
    [InlineData(StockUpOperationState.Verified)]
    [InlineData(StockUpOperationState.Completed)]
    [InlineData(StockUpOperationState.Failed)]
    public void MarkAsVerified_FromInvalidState_ThrowsInvalidOperationException(StockUpOperationState invalidState)
    {
        // Arrange
        var operation = CreateOperationInState(invalidState);

        // Act
        Action act = () => operation.MarkAsVerified(DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Cannot mark as Verified from {invalidState} state");
    }

    #endregion

    #region MarkAsCompleted Tests

    [Fact]
    public void MarkAsCompleted_FromVerifiedState_ChangesStateToCompleted()
    {
        // Arrange
        var operation = CreateDefaultOperation();
        operation.MarkAsSubmitted(DateTime.UtcNow);
        operation.MarkAsVerified(DateTime.UtcNow.AddSeconds(1));
        var completedAt = DateTime.UtcNow.AddSeconds(2);

        // Act
        operation.MarkAsCompleted(completedAt);

        // Assert
        operation.State.Should().Be(StockUpOperationState.Completed);
        operation.CompletedAt.Should().Be(completedAt);
    }

    [Fact]
    public void MarkAsCompleted_FromPendingState_ChangesStateToCompleted()
    {
        // Arrange - Pre-check scenario where operation already exists in Shoptet
        var operation = CreateDefaultOperation();
        var completedAt = DateTime.UtcNow;

        // Act
        operation.MarkAsCompleted(completedAt);

        // Assert
        operation.State.Should().Be(StockUpOperationState.Completed);
        operation.CompletedAt.Should().Be(completedAt);
    }

    [Theory]
    [InlineData(StockUpOperationState.Submitted)]
    [InlineData(StockUpOperationState.Completed)]
    [InlineData(StockUpOperationState.Failed)]
    public void MarkAsCompleted_FromInvalidState_ThrowsInvalidOperationException(StockUpOperationState invalidState)
    {
        // Arrange
        var operation = CreateOperationInState(invalidState);

        // Act
        Action act = () => operation.MarkAsCompleted(DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Cannot mark as Completed from {invalidState} state");
    }

    #endregion

    #region MarkAsFailed Tests

    [Fact]
    public void MarkAsFailed_FromAnyState_ChangesStateToFailed()
    {
        // Arrange
        var operation = CreateDefaultOperation();
        var failedAt = DateTime.UtcNow;
        var errorMessage = "Submit failed: Network error";

        // Act
        operation.MarkAsFailed(failedAt, errorMessage);

        // Assert
        operation.State.Should().Be(StockUpOperationState.Failed);
        operation.CompletedAt.Should().Be(failedAt);
        operation.ErrorMessage.Should().Be(errorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MarkAsFailed_WithInvalidErrorMessage_ThrowsValidationException(string? invalidErrorMessage)
    {
        // Arrange
        var operation = CreateDefaultOperation();

        // Act
        Action act = () => operation.MarkAsFailed(DateTime.UtcNow, invalidErrorMessage!);

        // Assert
        act.Should().Throw<ValidationException>()
            .WithMessage("Error message is required when marking operation as failed");
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void Reset_FromFailedState_ChangesStateToPending()
    {
        // Arrange
        var operation = CreateDefaultOperation();
        operation.MarkAsSubmitted(DateTime.UtcNow);
        operation.MarkAsFailed(DateTime.UtcNow.AddSeconds(1), "Test error");

        // Act
        operation.Reset();

        // Assert
        operation.State.Should().Be(StockUpOperationState.Pending);
        operation.SubmittedAt.Should().BeNull();
        operation.VerifiedAt.Should().BeNull();
        operation.CompletedAt.Should().BeNull();
        operation.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(StockUpOperationState.Pending)]
    [InlineData(StockUpOperationState.Submitted)]
    [InlineData(StockUpOperationState.Verified)]
    [InlineData(StockUpOperationState.Completed)]
    public void Reset_FromNonFailedState_ThrowsInvalidOperationException(StockUpOperationState invalidState)
    {
        // Arrange
        var operation = CreateOperationInState(invalidState);

        // Act
        Action act = () => operation.Reset();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Can only reset Failed operations, current state: {invalidState}");
    }

    #endregion

    #region State Transition Integration Tests

    [Fact]
    public void CompleteWorkflow_FromPendingToCompleted_WorksCorrectly()
    {
        // Arrange
        var operation = CreateDefaultOperation();
        var submittedAt = DateTime.UtcNow;
        var verifiedAt = submittedAt.AddSeconds(1);
        var completedAt = verifiedAt.AddSeconds(1);

        // Act & Assert - Full workflow
        operation.State.Should().Be(StockUpOperationState.Pending);

        operation.MarkAsSubmitted(submittedAt);
        operation.State.Should().Be(StockUpOperationState.Submitted);

        operation.MarkAsVerified(verifiedAt);
        operation.State.Should().Be(StockUpOperationState.Verified);

        operation.MarkAsCompleted(completedAt);
        operation.State.Should().Be(StockUpOperationState.Completed);
    }

    [Fact]
    public void FailureAndRetry_Workflow_WorksCorrectly()
    {
        // Arrange
        var operation = CreateDefaultOperation();

        // Act & Assert - Failure workflow
        operation.MarkAsSubmitted(DateTime.UtcNow);
        operation.State.Should().Be(StockUpOperationState.Submitted);

        operation.MarkAsFailed(DateTime.UtcNow.AddSeconds(1), "Network timeout");
        operation.State.Should().Be(StockUpOperationState.Failed);

        // Retry after reset
        operation.Reset();
        operation.State.Should().Be(StockUpOperationState.Pending);

        // Second attempt
        operation.MarkAsSubmitted(DateTime.UtcNow.AddSeconds(2));
        operation.MarkAsVerified(DateTime.UtcNow.AddSeconds(3));
        operation.MarkAsCompleted(DateTime.UtcNow.AddSeconds(4));
        operation.State.Should().Be(StockUpOperationState.Completed);
    }

    #endregion

    #region ForceReset Tests

    [Theory]
    [InlineData(StockUpOperationState.Pending)]
    [InlineData(StockUpOperationState.Submitted)]
    [InlineData(StockUpOperationState.Verified)]
    [InlineData(StockUpOperationState.Failed)]
    public void ForceReset_FromAnyNonCompletedState_ChangesStateToPending(StockUpOperationState initialState)
    {
        // Arrange
        var operation = CreateOperationInState(initialState);

        // Act
        operation.ForceReset();

        // Assert
        operation.State.Should().Be(StockUpOperationState.Pending);
        operation.SubmittedAt.Should().BeNull();
        operation.VerifiedAt.Should().BeNull();
        operation.CompletedAt.Should().BeNull();
        operation.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ForceReset_FromCompletedState_ThrowsInvalidOperationException()
    {
        // Arrange
        var operation = CreateOperationInState(StockUpOperationState.Completed);

        // Act
        Action act = () => operation.ForceReset();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot force reset Completed operations");
    }

    [Fact]
    public void ForceReset_FromSubmittedState_ClearsTimestamps()
    {
        // Arrange - Simulate stuck operation in Submitted state
        var operation = CreateDefaultOperation();
        operation.MarkAsSubmitted(DateTime.UtcNow);

        operation.SubmittedAt.Should().NotBeNull("operation should have SubmittedAt timestamp");

        // Act
        operation.ForceReset();

        // Assert
        operation.State.Should().Be(StockUpOperationState.Pending);
        operation.SubmittedAt.Should().BeNull();
    }

    #endregion

    #region Helper Methods

    private static StockUpOperation CreateDefaultOperation()
    {
        return new StockUpOperation(
            "BOX-000123-TEST001",
            "TEST001",
            10,
            StockUpSourceType.TransportBox,
            123);
    }

    private static StockUpOperation CreateOperationInState(StockUpOperationState targetState)
    {
        var operation = CreateDefaultOperation();

        return targetState switch
        {
            StockUpOperationState.Pending => operation,
            StockUpOperationState.Submitted => CreateSubmittedOperation(operation),
            StockUpOperationState.Verified => CreateVerifiedOperation(operation),
            StockUpOperationState.Completed => CreateCompletedOperation(operation),
            StockUpOperationState.Failed => CreateFailedOperation(operation),
            _ => throw new ArgumentOutOfRangeException(nameof(targetState))
        };
    }

    private static StockUpOperation CreateSubmittedOperation(StockUpOperation operation)
    {
        operation.MarkAsSubmitted(DateTime.UtcNow);
        return operation;
    }

    private static StockUpOperation CreateVerifiedOperation(StockUpOperation operation)
    {
        operation.MarkAsSubmitted(DateTime.UtcNow);
        operation.MarkAsVerified(DateTime.UtcNow.AddSeconds(1));
        return operation;
    }

    private static StockUpOperation CreateCompletedOperation(StockUpOperation operation)
    {
        operation.MarkAsSubmitted(DateTime.UtcNow);
        operation.MarkAsVerified(DateTime.UtcNow.AddSeconds(1));
        operation.MarkAsCompleted(DateTime.UtcNow.AddSeconds(2));
        return operation;
    }

    private static StockUpOperation CreateFailedOperation(StockUpOperation operation)
    {
        operation.MarkAsFailed(DateTime.UtcNow, "Test error");
        return operation;
    }

    #endregion
}
