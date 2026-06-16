using System.Diagnostics;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class CatalogMergeSchedulerTests
{
    // NOTE: Do NOT read IsMergeInProgress after Dispose(). The production
    // property reads _mergeSemaphore.CurrentCount with no _disposed guard
    // and will throw ObjectDisposedException.
    private sealed class FakeApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public FakeApplicationLifetime(bool stoppingCancelled = false)
        {
            if (stoppingCancelled)
            {
                _stopping.Cancel();
            }
        }

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }

    private static (CatalogMergeScheduler sut, Mock<ILogger<CatalogMergeScheduler>> logger)
        CreateScheduler(CatalogCacheOptions options, IHostApplicationLifetime? lifetime = null)
    {
        var logger = new Mock<ILogger<CatalogMergeScheduler>>();
        var sut = new CatalogMergeScheduler(
            logger.Object,
            Options.Create(options),
            lifetime ?? new FakeApplicationLifetime());
        return (sut, logger);
    }

    private static void VerifyLog(
        Mock<ILogger<CatalogMergeScheduler>> logger,
        LogLevel level,
        string substring,
        Times times)
    {
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(substring)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            times);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate())
            {
                return true;
            }
            await Task.Delay(10);
        }
        return predicate();
    }

    [Fact]
    public void Construction_DoesNotThrow_AndIsMergeInProgress_StartsFalse()
    {
        // Arrange / Act
        using var sut = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        }).sut;

        // Assert
        sut.IsMergeInProgress.Should().BeFalse();
        sut.HasPendingMerge().Should().BeFalse();
        sut.GetLastMergeTime().Should().Be(DateTime.MinValue);
    }

    [Fact]
    public async Task ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay()
    {
        // Arrange
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });
        using (sut)
        {
            var testStart = DateTime.UtcNow;
            var callbackFired = new TaskCompletionSource<bool>();
            int invocationCount = 0;

            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocationCount);
                callbackFired.TrySetResult(true);
                return Task.CompletedTask;
            });

            // Act
            sut.ScheduleMerge("source-a");

            // Assert: callback fires within bounded window
            var completed = await Task.WhenAny(callbackFired.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            completed.Should().Be(callbackFired.Task, "callback must fire within 2 s");

            // Allow ExecuteMergeAsync's finally block to release the semaphore
            (await WaitUntilAsync(() => !sut.IsMergeInProgress, TimeSpan.FromMilliseconds(500)))
                .Should().BeTrue();

            invocationCount.Should().Be(1);
            sut.HasPendingMerge().Should().BeFalse();
            sut.GetLastMergeTime().Should().BeAfter(testStart);
        }
    }

    [Fact]
    public async Task ScheduleMerge_BurstOfInvalidations_CollapsesToSingleCallback()
    {
        // Arrange
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(150),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });
        using (sut)
        {
            var callbackFired = new TaskCompletionSource<bool>();
            int invocationCount = 0;

            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocationCount);
                callbackFired.TrySetResult(true);
                return Task.CompletedTask;
            });

            // Act: five schedules at 30 ms apart
            for (int i = 0; i < 5; i++)
            {
                sut.ScheduleMerge($"source-{i}");
                await Task.Delay(30);
            }

            // Assert: wait up to DebounceDelay * 3 + slack
            var completed = await Task.WhenAny(callbackFired.Task, Task.Delay(TimeSpan.FromMilliseconds(800)));
            completed.Should().Be(callbackFired.Task);

            (await WaitUntilAsync(() => !sut.IsMergeInProgress, TimeSpan.FromMilliseconds(500)))
                .Should().BeTrue();

            invocationCount.Should().Be(1, "burst of 5 invalidations within debounce window must collapse to one merge");
            sut.HasPendingMerge().Should().BeFalse();
        }
    }

    [Fact]
    public async Task ScheduleMerge_ForceExecutes_WhenMaxIntervalElapsed()
    {
        // Arrange: long debounce so any debounce-path delay would clearly exceed
        // the 1 s assertion window; tiny MaxMergeInterval so the second schedule
        // immediately satisfies the force-execute condition.
        var (sut, logger) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromSeconds(10),
            MaxMergeInterval = TimeSpan.FromMilliseconds(50)
        });
        using (sut)
        {
            var callbackFired = new TaskCompletionSource<bool>();
            int invocationCount = 0;

            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocationCount);
                callbackFired.TrySetResult(true);
                return Task.CompletedTask;
            });

            // Act: seed _firstPendingInvalidation, wait past MaxMergeInterval, schedule again
            sut.ScheduleMerge("source-a");
            await Task.Delay(80); // > MaxMergeInterval
            sut.ScheduleMerge("source-b");

            // Assert: callback fires via Task.Run force path within 1 s
            var completed = await Task.WhenAny(callbackFired.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            completed.Should().Be(callbackFired.Task, "force path must fire within 1 s, well below DebounceDelay");

            (await WaitUntilAsync(() => !sut.IsMergeInProgress, TimeSpan.FromMilliseconds(500)))
                .Should().BeTrue();

            invocationCount.Should().Be(1);
            VerifyLog(logger, LogLevel.Information, "Force executing merge", Times.Once());
        }
    }

    [Fact]
    public async Task ExecuteMergeAsync_SkipsConcurrentExecution_WhenMergeAlreadyInProgress()
    {
        // Arrange: small DebounceDelay starts the first merge quickly;
        // tiny MaxMergeInterval drives the second ScheduleMerge into the
        // force-execute path so a second ExecuteMergeAsync is dispatched
        // while the first is still holding the semaphore.
        var (sut, logger) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMilliseconds(1)
        });
        using (sut)
        {
            var firstCallbackStarted = new TaskCompletionSource<bool>();
            var releaseFirstCallback = new TaskCompletionSource<bool>();
            int invocationCount = 0;

            sut.SetMergeCallback(async _ =>
            {
                int n = Interlocked.Increment(ref invocationCount);
                if (n == 1)
                {
                    firstCallbackStarted.TrySetResult(true);
                    await releaseFirstCallback.Task;
                }
            });

            // Act 1: trigger first merge via debounce path
            sut.ScheduleMerge("source-a");
            var started = await Task.WhenAny(firstCallbackStarted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            started.Should().Be(firstCallbackStarted.Task, "first merge must start so semaphore is held");

            (await WaitUntilAsync(() => sut.IsMergeInProgress, TimeSpan.FromMilliseconds(500)))
                .Should().BeTrue();

            // Act 2: trigger second ExecuteMergeAsync via force path while first is in flight
            sut.ScheduleMerge("source-b");

            // The skip path waits up to 100 ms on the semaphore, then logs and returns.
            // Wait long enough for that path to complete before asserting.
            await Task.Delay(300);

            // Assert: callback was only invoked once (second invocation skipped)
            invocationCount.Should().Be(1);
            VerifyLog(logger, LogLevel.Debug, "Merge already in progress, skipping", Times.AtLeastOnce());

            // Cleanup: release first callback and confirm the scheduler returns to idle
            releaseFirstCallback.SetResult(true);
            (await WaitUntilAsync(() => !sut.IsMergeInProgress, TimeSpan.FromSeconds(1)))
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_ReturnsImmediately_WhenNoMergeInProgress()
    {
        // Arrange
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });
        using (sut)
        {
            // Act
            var sw = Stopwatch.StartNew();
            await sut.WaitForCurrentMergeAsync();
            sw.Stop();

            // Assert
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_BlocksUntilMergeCompletes_AndReleasesSemaphore()
    {
        // Arrange
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });
        using (sut)
        {
            var callbackStarted = new TaskCompletionSource<bool>();
            var releaseCallback = new TaskCompletionSource<bool>();

            sut.SetMergeCallback(async _ =>
            {
                callbackStarted.TrySetResult(true);
                await releaseCallback.Task;
            });

            sut.ScheduleMerge("source-a");
            var started = await Task.WhenAny(callbackStarted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            started.Should().Be(callbackStarted.Task);

            (await WaitUntilAsync(() => sut.IsMergeInProgress, TimeSpan.FromMilliseconds(500)))
                .Should().BeTrue();

            // Act: WaitForCurrentMergeAsync must not return while callback is blocked
            var waitTask = sut.WaitForCurrentMergeAsync();
            await Task.Delay(100);
            waitTask.IsCompleted.Should().BeFalse("merge is still in-flight");

            // Release the callback; the wait must complete within 1 s
            releaseCallback.SetResult(true);
            var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(1)));
            completed.Should().Be(waitTask);
            waitTask.IsCompletedSuccessfully.Should().BeTrue();

            sut.IsMergeInProgress.Should().BeFalse();

            // A follow-up wait must complete immediately - semaphore was not leaked
            var sw = Stopwatch.StartNew();
            await sut.WaitForCurrentMergeAsync();
            sw.Stop();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_ReturnsImmediately_AfterDispose()
    {
        // Arrange
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });
        sut.Dispose();

        // Act / Assert: must not throw and must return promptly
        var sw = Stopwatch.StartNew();
        await sut.WaitForCurrentMergeAsync();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task ScheduleMerge_NoOps_AfterDispose()
    {
        // Arrange
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });
        int invocationCount = 0;
        sut.SetMergeCallback(_ =>
        {
            Interlocked.Increment(ref invocationCount);
            return Task.CompletedTask;
        });

        // Act
        sut.Dispose();
        var act = () => sut.ScheduleMerge("source-a");

        // Assert: no throw, and after >= 2x debounce delay the callback was never invoked
        act.Should().NotThrow();
        await Task.Delay(250); // >= 2 x DebounceDelay
        invocationCount.Should().Be(0);
    }

    [Fact]
    public async Task ScheduledMerge_NeverFires_WhenDisposedBeforeTimerElapses()
    {
        // Arrange: long-ish debounce so the timer has not yet fired when we Dispose
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(300),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });
        int invocationCount = 0;
        sut.SetMergeCallback(_ =>
        {
            Interlocked.Increment(ref invocationCount);
            return Task.CompletedTask;
        });

        // Act
        sut.ScheduleMerge("source-a");
        sut.Dispose(); // disposes the underlying Timer before its 300 ms elapses

        // Assert: wait well past the original DebounceDelay
        await Task.Delay(700);
        invocationCount.Should().Be(0);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });

        // Act
        sut.Dispose();
        var act = () => sut.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ScheduleMerge_NoOps_AndWait_ReturnsImmediately_WhenApplicationStoppingAlreadyCancelled()
    {
        // Arrange
        var lifetime = new FakeApplicationLifetime(stoppingCancelled: true);
        var (sut, _) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        }, lifetime);
        using (sut)
        {
            int invocationCount = 0;
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocationCount);
                return Task.CompletedTask;
            });

            // Act
            sut.ScheduleMerge("source-a");

            // Assert: callback never fires
            await Task.Delay(250); // >= 2 x DebounceDelay
            invocationCount.Should().Be(0);

            // WaitForCurrentMergeAsync must return immediately
            var sw = Stopwatch.StartNew();
            await sut.WaitForCurrentMergeAsync();
            sw.Stop();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
        }
    }

    [Fact]
    public async Task ExecuteMergeAsync_CallbackThrows_LogsErrorReleasesSemaphoreAndRemainsUsable()
    {
        // Arrange
        var (sut, logger) = CreateScheduler(new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        });
        using (sut)
        {
            var firstCallbackFired = new TaskCompletionSource<bool>();
            sut.SetMergeCallback(_ =>
            {
                firstCallbackFired.TrySetResult(true);
                return Task.FromException(new InvalidOperationException("boom"));
            });

            // Act 1: trigger the failing merge
            sut.ScheduleMerge("source-a");
            (await Task.WhenAny(firstCallbackFired.Task, Task.Delay(TimeSpan.FromSeconds(2))))
                .Should().Be(firstCallbackFired.Task);

            // Semaphore must be released in the finally block
            (await WaitUntilAsync(() => !sut.IsMergeInProgress, TimeSpan.FromSeconds(1)))
                .Should().BeTrue();

            // Assert error was logged with the original exception
            logger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Background merge failed")),
                    It.Is<Exception>(e => e is InvalidOperationException && e.Message == "boom"),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
                Times.Once);

            // Act 2: swap in a successful callback and verify the scheduler still works
            var secondCallbackFired = new TaskCompletionSource<bool>();
            int secondInvocations = 0;
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref secondInvocations);
                secondCallbackFired.TrySetResult(true);
                return Task.CompletedTask;
            });

            sut.ScheduleMerge("source-b");

            (await Task.WhenAny(secondCallbackFired.Task, Task.Delay(TimeSpan.FromSeconds(2))))
                .Should().Be(secondCallbackFired.Task);
            secondInvocations.Should().Be(1);
        }
    }
}
