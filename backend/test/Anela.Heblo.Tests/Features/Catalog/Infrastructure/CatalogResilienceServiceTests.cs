using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Polly.CircuitBreaker;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogResilienceServiceTests
{
    private readonly Mock<ILogger<CatalogResilienceService>> _loggerMock;
    private readonly CatalogResilienceService _service;

    public CatalogResilienceServiceTests()
    {
        _loggerMock = new Mock<ILogger<CatalogResilienceService>>();
        _service = new CatalogResilienceService(_loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_SuccessfulOperation_ReturnsResult()
    {
        // Arrange
        var expectedResult = "test-result";
        var operationName = "TestOperation";

        Func<CancellationToken, Task<string>> operation = ct => Task.FromResult(expectedResult);

        // Act
        var result = await _service.ExecuteWithResilienceAsync(operation, operationName);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_OperationThrowsHttpRequestException_RetriesAndSucceeds()
    {
        // Arrange
        var expectedResult = "success-after-retry";
        var operationName = "RetryOperation";
        var attemptCount = 0;

        Func<CancellationToken, Task<string>> operation = ct =>
        {
            attemptCount++;
            if (attemptCount == 1)
                throw new HttpRequestException("Network error");
            return Task.FromResult(expectedResult);
        };

        // Act
        var result = await _service.ExecuteWithResilienceAsync(operation, operationName);

        // Assert
        result.Should().Be(expectedResult);
        attemptCount.Should().Be(2); // First attempt failed, second succeeded

        // Verify retry warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrying operation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_OperationThrowsTaskCanceledException_RetriesAndSucceeds()
    {
        // Arrange
        var expectedResult = "success-after-timeout";
        var operationName = "TimeoutOperation";
        var attemptCount = 0;

        Func<CancellationToken, Task<string>> operation = ct =>
        {
            attemptCount++;
            if (attemptCount == 1)
                throw new TaskCanceledException("Operation timed out");
            return Task.FromResult(expectedResult);
        };

        // Act
        var result = await _service.ExecuteWithResilienceAsync(operation, operationName);

        // Assert
        result.Should().Be(expectedResult);
        attemptCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_OperationConsistentlyFails_ThrowsAfterAllRetries()
    {
        // Arrange
        var operationName = "AlwaysFailsOperation";
        var exception = new HttpRequestException("Persistent network error");

        Func<CancellationToken, Task<string>> operation = ct => throw exception;

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ExecuteWithResilienceAsync(operation, operationName));

        thrownException.Message.Should().Contain("temporarily unavailable");

        // Verify multiple retry warnings were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrying operation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(3)); // Should retry 3 times

        // Verify circuit breaker warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Circuit breaker is open")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_NonRetryableException_ThrowsImmediately()
    {
        // Arrange
        var operationName = "NonRetryableOperation";
        var exception = new InvalidOperationException("Business logic error");

        Func<CancellationToken, Task<string>> operation = ct => throw exception;

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ExecuteWithResilienceAsync(operation, operationName));

        thrownException.Should().Be(exception);

        // Verify no retry warnings were logged (should fail immediately)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrying operation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_OperationCancelled_DoesNotRetry()
    {
        // Arrange
        var operationName = "CancelledOperation";
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        var exception = new OperationCanceledException(cts.Token);

        Func<CancellationToken, Task<string>> operation = ct => throw exception;

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.ExecuteWithResilienceAsync(operation, operationName, cts.Token));

        thrownException.CancellationToken.Should().Be(cts.Token);

        // Verify no retries for properly cancelled operations
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrying operation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_LogsDebugMessageOnExecution()
    {
        // Arrange
        var operationName = "DebugTestOperation";

        Func<CancellationToken, Task<string>> operation = ct => Task.FromResult("result");

        // Act
        await _service.ExecuteWithResilienceAsync(operation, operationName);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Executing") && v.ToString()!.Contains(operationName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_MultipleOperations_DoesNotInterfere()
    {
        // Arrange
        var operation1 = new Func<CancellationToken, Task<string>>(ct => Task.FromResult("result1"));
        var operation2 = new Func<CancellationToken, Task<int>>(ct => Task.FromResult(42));

        // Act
        var result1Task = _service.ExecuteWithResilienceAsync(operation1, "Operation1");
        var result2Task = _service.ExecuteWithResilienceAsync(operation2, "Operation2");

        await Task.WhenAll(result1Task, result2Task);
        var result1 = await result1Task;
        var result2 = await result2Task;

        // Assert
        result1.Should().Be("result1");
        result2.Should().Be(42);
    }
}