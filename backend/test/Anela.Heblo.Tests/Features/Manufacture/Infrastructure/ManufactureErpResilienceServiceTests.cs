using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.Infrastructure;
using Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Infrastructure;

public class ManufactureErpResilienceServiceTests
{
    private static readonly ManufactureErpOptions DefaultOptions = new()
    {
        ErpCircuitBreakerMinimumThroughput = 2,
        ErpCircuitBreakerFailureRatio = 0.5,
        ErpCircuitBreakerSamplingDurationSeconds = 900,
        ErpCircuitBreakerBreakDurationSeconds = 30,
    };

    private static ManufactureErpResilienceService CreateService(
        ManufactureErpOptions? options = null,
        TimeProvider? timeProvider = null,
        Mock<ILogger<ManufactureErpResilienceService>>? loggerMock = null)
    {
        return new ManufactureErpResilienceService(
            Options.Create(options ?? DefaultOptions),
            timeProvider ?? TimeProvider.System,
            (loggerMock ?? new Mock<ILogger<ManufactureErpResilienceService>>()).Object);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_ReturnsResult()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(_ => Task.FromResult("ok"), "SubmitManufacture");

        result.Should().Be("ok");
    }

    // ── Finding 1: CancelAfter-triggered timeouts must be counted as failures ──

    /// <summary>
    /// Reproduces the exact shape SubmitManufactureHandler.CreateLinkedCts produces: a
    /// linked CTS built off the original caller token, armed with CancelAfter, whose
    /// timeout fires and throws a TaskCanceledException carrying the LINKED token (not
    /// the original one). ExecuteAsync is invoked with the ORIGINAL, un-linked token —
    /// exactly like SubmitManufactureHandler now calls it. If the breaker naively checked
    /// ex.CancellationToken (the linked token, which IS cancelled) instead of the original
    /// caller token passed into ExecuteAsync, it would never count this as a failure.
    /// </summary>
    private static async Task<string> TimeoutOperation(CancellationToken _)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(1));
        await Task.Delay(TimeSpan.FromSeconds(5), linkedCts.Token);
        return "unreachable";
    }

    [Fact]
    public async Task ExecuteAsync_CancelAfterTimeout_IsCountedAsFailure_OpensCircuitAfterThreshold()
    {
        var service = CreateService();

        // Two CancelAfter-triggered timeouts (MinimumThroughput=2) — both invoked with
        // the caller's own, never-cancelled token, matching production call shape.
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.ExecuteAsync(TimeoutOperation, "SubmitManufacture", CancellationToken.None));
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.ExecuteAsync(TimeoutOperation, "SubmitManufacture", CancellationToken.None));

        // Circuit is now open: the next call fails fast with the typed exception and the
        // underlying operation is never invoked.
        var invoked = false;
        Task<string> GuardOperation(CancellationToken _)
        {
            invoked = true;
            return Task.FromResult("should-not-run");
        }

        var act = () => service.ExecuteAsync(GuardOperation, "SubmitManufacture", CancellationToken.None);

        await act.Should().ThrowAsync<ManufactureErpUnavailableException>();
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CancelAfterTimeout_LogsCircuitOpenedWarning()
    {
        var loggerMock = new Mock<ILogger<ManufactureErpResilienceService>>();
        var service = CreateService(loggerMock: loggerMock);

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.ExecuteAsync(TimeoutOperation, "SubmitManufacture", CancellationToken.None));
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.ExecuteAsync(TimeoutOperation, "SubmitManufacture", CancellationToken.None));

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Circuit breaker opened")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── Genuine caller cancellation must NOT be treated as a dependency failure ──

    [Fact]
    public async Task ExecuteAsync_CallerCancelled_PropagatesUnchanged_AndIsNotCountedAsFailure()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Task<string> Operation(CancellationToken _) => throw new OperationCanceledException(cts.Token);

        // Two caller-cancellations in a row — since neither counts toward the breaker,
        // a subsequent successful call must still go through normally (not fail fast).
        for (var i = 0; i < 2; i++)
        {
            var ex = await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.ExecuteAsync<string>(Operation, "SubmitManufacture", cts.Token));
            ex.CancellationToken.Should().Be(cts.Token);
        }

        var result = await service.ExecuteAsync(_ => Task.FromResult("still-closed"), "SubmitManufacture");
        result.Should().Be("still-closed");
    }

    // ── Half-open recovery ──

    [Fact]
    public async Task ExecuteAsync_AfterBreakDuration_SuccessfulProbe_ClosesCircuitAgain()
    {
        var fakeTime = new FakeTimeProvider();
        var service = CreateService(timeProvider: fakeTime);

        Task<string> FailingOperation(CancellationToken _) => throw new HttpRequestException("boom");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.ExecuteAsync(FailingOperation, "SubmitManufacture", CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.ExecuteAsync(FailingOperation, "SubmitManufacture", CancellationToken.None));

        // Circuit open — fails fast without invoking the operation.
        await Assert.ThrowsAsync<ManufactureErpUnavailableException>(
            () => service.ExecuteAsync(FailingOperation, "SubmitManufacture", CancellationToken.None));

        // Advance past BreakDurationSeconds so a half-open probe is attempted.
        fakeTime.Advance(TimeSpan.FromSeconds(DefaultOptions.ErpCircuitBreakerBreakDurationSeconds + 1));

        var result = await service.ExecuteAsync(_ => Task.FromResult("recovered"), "SubmitManufacture");
        result.Should().Be("recovered");

        // Circuit closed again — a fresh failure is passed through untouched (not
        // fast-failed), proving the operation is actually invoked again.
        var invoked = false;
        Task<string> GuardOperation(CancellationToken _)
        {
            invoked = true;
            throw new HttpRequestException("boom-again");
        }

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.ExecuteAsync(GuardOperation, "SubmitManufacture", CancellationToken.None));
        invoked.Should().BeTrue();
    }

    // ── Non-timeout, non-transport exceptions are not the breaker's concern ──

    [Fact]
    public async Task ExecuteAsync_NonHandledException_PropagatesImmediately_DoesNotCountTowardBreaker()
    {
        var service = CreateService();
        var exception = new InvalidOperationException("business rule violation");

        Task<string> Operation(CancellationToken _) => throw exception;

        for (var i = 0; i < 5; i++)
        {
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ExecuteAsync<string>(Operation, "SubmitManufacture"));
            thrown.Should().BeSameAs(exception);
        }

        var result = await service.ExecuteAsync(_ => Task.FromResult("still-closed"), "SubmitManufacture");
        result.Should().Be("still-closed");
    }
}
