using System;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Services;

/// <summary>
/// Unit tests for StockUpOperationResult factory methods and the IsSuccess predicate.
/// Coverage-only test suite: pins current behavior, does not change production code.
/// </summary>
public class StockUpOperationResultTests
{
    private static StockUpOperation CreateOperation()
    {
        return new StockUpOperation("DOC-1", "PROD-1", 1, StockUpSourceType.TransportBox, 1);
    }

    [Fact]
    public void Success_WithOperation_ReturnsSuccessResult()
    {
        // Arrange
        var operation = CreateOperation();

        // Act
        var result = StockUpOperationResult.Success(operation);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.Success);
        result.Message.Should().Be("Stock up operation completed successfully");
        result.Operation.Should().BeSameAs(operation);
        result.Exception.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AlreadyCompleted_WithOperation_ReturnsAlreadyCompletedResult()
    {
        // Arrange
        var operation = CreateOperation();

        // Act
        var result = StockUpOperationResult.AlreadyCompleted(operation);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.AlreadyCompleted);
        result.Message.Should().Be("Operation already completed");
        result.Operation.Should().BeSameAs(operation);
        result.Exception.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void PreviouslyFailed_WithFailedOperation_ReturnsPreviouslyFailedResult()
    {
        // Arrange
        var operation = CreateOperation();
        operation.MarkAsFailed(DateTime.UtcNow, "Test error message");

        // Act
        var result = StockUpOperationResult.PreviouslyFailed(operation);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.PreviouslyFailed);
        result.Message.Should().Be("Operation previously failed: Test error message");
        result.Operation.Should().BeSameAs(operation);
        result.Exception.Should().BeNull();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void InProgress_WithOperation_ReturnsInProgressResultWithState()
    {
        // Arrange
        var operation = CreateOperation();

        // Act
        var result = StockUpOperationResult.InProgress(operation);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.InProgress);
        result.Message.Should().Be("Operation already in progress (state: Pending)");
        result.Operation.Should().BeSameAs(operation);
        result.Exception.Should().BeNull();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void InProgress_WithNullOperation_ReturnsInProgressResultWithEmptyState()
    {
        // Act
        var result = StockUpOperationResult.InProgress(null);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.InProgress);
        result.Message.Should().Be("Operation already in progress (state: )");
        result.Operation.Should().BeNull();
        result.Exception.Should().BeNull();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void AlreadyInShoptet_WithOperation_ReturnsAlreadyInShoptetResult()
    {
        // Arrange
        var operation = CreateOperation();

        // Act
        var result = StockUpOperationResult.AlreadyInShoptet(operation);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.AlreadyInShoptet);
        result.Message.Should().Be("Document already exists in Shoptet history");
        result.Operation.Should().BeSameAs(operation);
        result.Exception.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void SubmitFailed_WithOperationAndException_ReturnsFailedResult()
    {
        // Arrange
        var operation = CreateOperation();
        var ex = new InvalidOperationException("boom");

        // Act
        var result = StockUpOperationResult.SubmitFailed(operation, ex);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.Failed);
        result.Message.Should().Be("Submit failed: boom");
        result.Operation.Should().BeSameAs(operation);
        result.Exception.Should().BeSameAs(ex);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void VerificationFailed_WithOperation_ReturnsFailedResult()
    {
        // Arrange
        var operation = CreateOperation();

        // Act
        var result = StockUpOperationResult.VerificationFailed(operation);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.Failed);
        result.Message.Should().Be("Verification failed: Record not found in Shoptet history after submission");
        result.Operation.Should().BeSameAs(operation);
        result.Exception.Should().BeNull();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void VerificationError_WithOperationAndException_ReturnsFailedResult()
    {
        // Arrange
        var operation = CreateOperation();
        var ex = new InvalidOperationException("boom");

        // Act
        var result = StockUpOperationResult.VerificationError(operation, ex);

        // Assert
        result.Status.Should().Be(StockUpResultStatus.Failed);
        result.Message.Should().Be("Verification error: boom");
        result.Operation.Should().BeSameAs(operation);
        result.Exception.Should().BeSameAs(ex);
        result.IsSuccess.Should().BeFalse();
    }
}
