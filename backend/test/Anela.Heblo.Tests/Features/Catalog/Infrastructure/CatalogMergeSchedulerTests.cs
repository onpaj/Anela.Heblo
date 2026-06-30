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
    private sealed class FakeApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public FakeApplicationLifetime(bool stoppingCancelled = false)
        {
            if (stoppingCancelled) _stopping.Cancel();
        }

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }

    private (CatalogMergeScheduler sut, Mock<ILogger<CatalogMergeScheduler>> logger)
        CreateScheduler(CatalogCacheOptions options, IHostApplicationLifetime? lifetime = null)
    {
        var logger = new Mock<ILogger<CatalogMergeScheduler>>();
        var sut = new CatalogMergeScheduler(
            logger.Object,
            Options.Create(options),
            lifetime ?? new FakeApplicationLifetime());
        return (sut, logger);
    }

    private static void VerifyLogged(
        Mock<ILogger<CatalogMergeScheduler>> logger,
        LogLevel level,
        string substring)
    {
        logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(substring)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        };
        var (sut, _) = CreateScheduler(opts);
        var callbackFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        var testStart = DateTime.UtcNow;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocations);
                callbackFired.TrySetResult(true);
                return Task.CompletedTask;
            });

            sut.ScheduleMerge("source-a");

            var completed = await Task.WhenAny(callbackFired.Task, Task.Delay(2000));
            completed.Should().Be(callbackFired.Task, "callback should fire within 2 seconds");

            await Task.Delay(50); // let ExecuteMergeAsync finish its post-callback bookkeeping

            invocations.Should().Be(1);
            sut.HasPendingMerge().Should().BeFalse();
            sut.GetLastMergeTime().Should().BeAfter(testStart);
        }
    }

    [Fact]
    public async Task ScheduleMerge_BurstOfCalls_CollapseToSingleCallback()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(150),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        };
        var (sut, _) = CreateScheduler(opts);
        var callbackFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocations);
                callbackFired.TrySetResult(true);
                return Task.CompletedTask;
            });

            for (int i = 0; i < 5; i++)
            {
                sut.ScheduleMerge($"source-{i}");
                await Task.Delay(30);
            }

            var completed = await Task.WhenAny(callbackFired.Task, Task.Delay(2000));
            completed.Should().Be(callbackFired.Task, "single callback should fire within 2 seconds");

            await Task.Delay(450); // extra window to confirm no second callback

            invocations.Should().Be(1);
            sut.HasPendingMerge().Should().BeFalse();
        }
    }

    [Fact]
    public async Task ScheduleMerge_BeyondMaxMergeInterval_ForcesImmediateExecution()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromSeconds(10), // intentionally long
            MaxMergeInterval = TimeSpan.FromMilliseconds(50)
        };
        var (sut, logger) = CreateScheduler(opts);
        var callbackFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocations);
                callbackFired.TrySetResult(true);
                return Task.CompletedTask;
            });

            sut.ScheduleMerge("source-a"); // seeds _firstPendingInvalidation + arms debounce
            await Task.Delay(60); // wait > MaxMergeInterval (50ms)
            sut.ScheduleMerge("source-b"); // triggers force path

            var completed = await Task.WhenAny(callbackFired.Task, Task.Delay(1000));
            completed.Should().Be(callbackFired.Task, "callback should fire within 1 second via force path");

            await Task.Delay(50);

            invocations.Should().Be(1);
        }

        VerifyLogged(logger, LogLevel.Information, "Force executing merge");
    }

    [Fact]
    public async Task ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(50),
            MaxMergeInterval = TimeSpan.FromMilliseconds(1)
        };
        var (sut, logger) = CreateScheduler(opts);
        var gatingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(async _ =>
            {
                Interlocked.Increment(ref invocations);
                await gatingTcs.Task;
            });

            sut.ScheduleMerge("source-a");

            // Poll until first merge acquires the semaphore
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!sut.IsMergeInProgress && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
            sut.IsMergeInProgress.Should().BeTrue("first merge should be in progress");

            // Trigger second execute via force path (MaxMergeInterval = 1ms, enough time elapsed)
            sut.ScheduleMerge("source-b");
            await Task.Delay(200); // let second ExecuteMergeAsync run and skip

            invocations.Should().Be(1, "second invocation should be skipped");

            VerifyLogged(logger, LogLevel.Debug, "Merge already in progress, skipping");

            gatingTcs.TrySetResult(true); // release first merge
            await Task.Delay(100); // let merge complete before Dispose

            sut.IsMergeInProgress.Should().BeFalse("semaphore should be released after merge");
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_WhenNoMergeInProgress_CompletesImmediately()
    {
        var opts = new CatalogCacheOptions { DebounceDelay = TimeSpan.FromSeconds(60) };
        var (sut, _) = CreateScheduler(opts);

        using (sut)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await sut.WaitForCurrentMergeAsync();
            sw.Stop();
            sw.ElapsedMilliseconds.Should().BeLessThan(50, "no merge in progress — should return immediately");
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_WhenMergeInProgress_BlocksUntilComplete()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(50),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        };
        var (sut, _) = CreateScheduler(opts);
        var gatingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (sut)
        {
            sut.SetMergeCallback(async _ => await gatingTcs.Task);
            sut.ScheduleMerge("source-a");

            // Wait until merge holds the semaphore
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!sut.IsMergeInProgress && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
            sut.IsMergeInProgress.Should().BeTrue();

            var waitTask = sut.WaitForCurrentMergeAsync();
            await Task.Delay(100);
            waitTask.IsCompleted.Should().BeFalse("wait task should block while merge is in progress");

            gatingTcs.TrySetResult(true); // release the merge callback

            var finishedTask = await Task.WhenAny(waitTask, Task.Delay(1000));
            finishedTask.Should().Be(waitTask, "wait task should complete after merge finishes");

            sut.IsMergeInProgress.Should().BeFalse();

            // Second WaitForCurrentMergeAsync should complete immediately
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await sut.WaitForCurrentMergeAsync();
            sw.Stop();
            sw.ElapsedMilliseconds.Should().BeLessThan(50, "semaphore should not be leaked");
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_AfterDispose_ReturnsImmediately()
    {
        var opts = new CatalogCacheOptions { DebounceDelay = TimeSpan.FromSeconds(60) };
        var (sut, _) = CreateScheduler(opts);
        sut.Dispose();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sut.WaitForCurrentMergeAsync();
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(50, "disposed scheduler should return immediately");
    }

    [Fact]
    public async Task ScheduleMerge_AfterDispose_DoesNotFireCallback()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        };
        var (sut, _) = CreateScheduler(opts);
        var invocations = 0;

        sut.SetMergeCallback(_ =>
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        });

        sut.Dispose();
        sut.ScheduleMerge("source-x");

        await Task.Delay(300); // > 2 × DebounceDelay
        invocations.Should().Be(0);
    }

    [Fact]
    public async Task ScheduleMerge_DisposedBeforeTimerFires_DoesNotFireCallback()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(200),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        };
        var (sut, _) = CreateScheduler(opts);
        var invocations = 0;

        sut.SetMergeCallback(_ =>
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        });

        sut.ScheduleMerge("source-x");
        sut.Dispose(); // dispose before DebounceDelay elapses

        await Task.Delay(500); // well beyond DebounceDelay
        invocations.Should().Be(0);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var opts = new CatalogCacheOptions();
        var (sut, _) = CreateScheduler(opts);

        sut.Dispose();
        var act = () => sut.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ScheduleMerge_WhenApplicationStopping_DoesNotFireCallback()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        };
        var lifetime = new FakeApplicationLifetime(stoppingCancelled: true);
        var (sut, _) = CreateScheduler(opts, lifetime);
        var invocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocations);
                return Task.CompletedTask;
            });

            sut.ScheduleMerge("source-x");
            await Task.Delay(300); // > 2 × DebounceDelay
        }

        invocations.Should().Be(0);
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_WhenApplicationStopping_CompletesImmediately()
    {
        var opts = new CatalogCacheOptions();
        var lifetime = new FakeApplicationLifetime(stoppingCancelled: true);
        var (sut, _) = CreateScheduler(opts, lifetime);

        using (sut)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await sut.WaitForCurrentMergeAsync();
            sw.Stop();
            sw.ElapsedMilliseconds.Should().BeLessThan(50);
        }
    }

    [Fact]
    public async Task ScheduleMerge_WhenCallbackThrows_SchedulerRemainsUsable()
    {
        var opts = new CatalogCacheOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaxMergeInterval = TimeSpan.FromMinutes(30)
        };
        var (sut, logger) = CreateScheduler(opts);
        var firstAttemptDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCallbackFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondInvocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                firstAttemptDone.TrySetResult(true);
                throw new InvalidOperationException("boom");
            });

            sut.ScheduleMerge("source-a");
            await Task.WhenAny(firstAttemptDone.Task, Task.Delay(2000));
            firstAttemptDone.Task.IsCompletedSuccessfully.Should().BeTrue("first callback should have fired");

            await Task.Delay(50); // let catch + finally run and release semaphore

            sut.IsMergeInProgress.Should().BeFalse("semaphore must be released after a failing callback");

            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref secondInvocations);
                secondCallbackFired.TrySetResult(true);
                return Task.CompletedTask;
            });

            sut.ScheduleMerge("source-b");
            await Task.WhenAny(secondCallbackFired.Task, Task.Delay(2000));
            secondCallbackFired.Task.IsCompletedSuccessfully.Should().BeTrue("second callback should succeed");

            await Task.Delay(50);

            secondInvocations.Should().Be(1);
        }

        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Background merge failed")),
            It.Is<Exception>(e => e.Message == "boom"),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }
}
