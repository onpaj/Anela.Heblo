using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class CatalogMergeSchedulerTests
{
    /// <summary>Fixed fake epoch. Never DateTimeOffset.UtcNow — that reintroduces wall-clock dependence.</summary>
    private static readonly DateTimeOffset TestStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Production-like values from CatalogCacheOptions. Free under a fake clock.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(30);

    /// <summary>Failure-only budget. Never used as a wait — only as the losing branch of WhenAny.</summary>
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

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

    private static CatalogCacheOptions Options_(TimeSpan? debounce = null, TimeSpan? maxInterval = null) => new()
    {
        DebounceDelay = debounce ?? Debounce,
        MaxMergeInterval = maxInterval ?? MaxInterval
    };

    private (CatalogMergeScheduler sut, Mock<ILogger<CatalogMergeScheduler>> logger, FakeTimeProvider time)
        CreateScheduler(CatalogCacheOptions options, IHostApplicationLifetime? lifetime = null)
    {
        var time = new FakeTimeProvider(TestStart);
        var logger = new Mock<ILogger<CatalogMergeScheduler>>();
        var sut = new CatalogMergeScheduler(
            logger.Object,
            Options.Create(options),
            lifetime ?? new FakeApplicationLifetime(),
            time);
        return (sut, logger, time);
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Deterministic barrier for "a merge has fully completed".
    /// Step 1: await the signal raised from inside the merge callback — proves ExecuteMergeAsync
    ///         acquired the semaphore and entered the callback.
    /// Step 2: await WaitForCurrentMergeAsync — waits on the same semaphore, which is only released
    ///         in the finally block, after _lastMergeCompleted / _mergeScheduled / _firstPendingInvalidation
    ///         have all been written.
    /// No polling, no sleeping. The Task.Delay is a failure timeout only.
    /// </summary>
    private static async Task WaitForMergeAsync(
        CatalogMergeScheduler sut,
        TaskCompletionSource<bool> callbackEntered,
        string because)
    {
        var winner = await Task.WhenAny(callbackEntered.Task, Task.Delay(SignalTimeout));
        winner.Should().Be(callbackEntered.Task, because);
        await sut.WaitForCurrentMergeAsync();
    }

    /// <summary>Awaits a signal with a failure-only budget, without touching the scheduler's semaphore.</summary>
    private static async Task AwaitSignalAsync(TaskCompletionSource<bool> signal, string because)
    {
        var winner = await Task.WhenAny(signal.Task, Task.Delay(SignalTimeout));
        winner.Should().Be(signal.Task, because);
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
        var opts = Options_();
        var (sut, _, time) = CreateScheduler(opts);
        var callbackEntered = NewSignal();
        var invocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocations);
                callbackEntered.TrySetResult(true);
                return Task.CompletedTask;
            });

            sut.ScheduleMerge("source-a");

            // Not yet due — one tick short of the debounce delay.
            time.Advance(Debounce - TimeSpan.FromTicks(1));
            invocations.Should().Be(0, "the debounce delay has not elapsed on the fake clock");

            time.Advance(TimeSpan.FromTicks(1));
            await WaitForMergeAsync(sut, callbackEntered, "the debounce timer should have fired the merge");

            invocations.Should().Be(1);
            sut.HasPendingMerge().Should().BeFalse();
            sut.GetLastMergeTime().Should().Be(TestStart.Add(Debounce).UtcDateTime);
            sut.GetLastMergeTime().Kind.Should().Be(DateTimeKind.Utc,
                "GetLastMergeTime must keep the Kind that DateTime.UtcNow produced");
        }
    }

    [Fact]
    public async Task ScheduleMerge_BurstOfCalls_CollapseToSingleCallback()
    {
        var opts = Options_();
        var (sut, _, time) = CreateScheduler(opts);
        var callbackEntered = NewSignal();
        var invocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocations);
                callbackEntered.TrySetResult(true);
                return Task.CompletedTask;
            });

            // Five invalidations, each one second apart — every one resets the 5s debounce window,
            // so the timer never becomes due during the burst.
            for (int i = 0; i < 5; i++)
            {
                sut.ScheduleMerge($"source-{i}");
                time.Advance(TimeSpan.FromSeconds(1));
            }

            invocations.Should().Be(0, "each ScheduleMerge resets the debounce window");

            time.Advance(Debounce);
            await WaitForMergeAsync(sut, callbackEntered, "a single merge should fire after the burst settles");

            invocations.Should().Be(1);
            sut.HasPendingMerge().Should().BeFalse();

            // The timer period is Timeout.InfiniteTimeSpan — advancing further must not fire it again.
            time.Advance(Debounce * 2);
            invocations.Should().Be(1, "the debounce timer is one-shot");
        }
    }

    [Fact]
    public async Task ScheduleMerge_BeyondMaxMergeInterval_ForcesImmediateExecution()
    {
        // DebounceDelay is deliberately LONGER than the advance below, so the debounce timer
        // never becomes due and cannot reset _firstPendingInvalidation before the force path runs.
        var opts = Options_(debounce: TimeSpan.FromHours(1), maxInterval: MaxInterval);
        var (sut, logger, time) = CreateScheduler(opts);
        var callbackEntered = NewSignal();
        var invocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocations);
                callbackEntered.TrySetResult(true);
                return Task.CompletedTask;
            });

            sut.ScheduleMerge("source-a");           // seeds _firstPendingInvalidation, arms a 1h debounce
            time.Advance(MaxInterval + TimeSpan.FromMinutes(1));
            invocations.Should().Be(0, "the 1h debounce timer is not due yet");

            sut.ScheduleMerge("source-b");           // 31 min since first invalidation -> force path

            await WaitForMergeAsync(sut, callbackEntered, "the max-interval force path should run the merge");

            invocations.Should().Be(1);
        }

        VerifyLogged(logger, LogLevel.Information, "Force executing merge");
    }

    [Fact]
    public async Task ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation()
    {
        var opts = Options_(debounce: Debounce, maxInterval: MaxInterval);
        var (sut, logger, time) = CreateScheduler(opts);
        var callbackEntered = NewSignal();
        var gate = NewSignal();
        var skipLogged = NewSignal();
        var invocations = 0;

        // Signal as soon as the scheduler logs the skip, so we never have to poll or sleep for it.
        logger.Setup(l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Merge already in progress, skipping")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!))
            .Callback(() => skipLogged.TrySetResult(true));

        using (sut)
        {
            sut.SetMergeCallback(async _ =>
            {
                Interlocked.Increment(ref invocations);
                callbackEntered.TrySetResult(true);
                await gate.Task;                     // hold the merge semaphore open
            });

            sut.ScheduleMerge("source-a");
            time.Advance(Debounce);                  // fires the debounce timer
            await AwaitSignalAsync(callbackEntered, "the first merge should have entered the callback");
            sut.IsMergeInProgress.Should().BeTrue("the first merge holds the semaphore");

            // Reach the force path while the first merge is still gated. The debounce timer already
            // fired and is one-shot, so this advance arms nothing new.
            time.Advance(MaxInterval + TimeSpan.FromMinutes(1));
            sut.ScheduleMerge("source-b");

            // The second ExecuteMergeAsync blocks on _mergeSemaphore.WaitAsync(100) — a REAL 100 ms
            // wait on the contended path. That literal is out of scope for this change (spec.r1.md).
            await AwaitSignalAsync(skipLogged, "the second merge should log that it is skipping");

            invocations.Should().Be(1, "second invocation should be skipped");
            VerifyLogged(logger, LogLevel.Debug, "Merge already in progress, skipping");

            gate.TrySetResult(true);                 // release the first merge
            await sut.WaitForCurrentMergeAsync();

            sut.IsMergeInProgress.Should().BeFalse("semaphore should be released after merge");
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_WhenNoMergeInProgress_CompletesImmediately()
    {
        var (sut, _, _) = CreateScheduler(Options_());

        using (sut)
        {
            var waitTask = sut.WaitForCurrentMergeAsync();
            waitTask.IsCompleted.Should().BeTrue("no merge in progress — should return synchronously");
            await waitTask;
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_WhenMergeInProgress_BlocksUntilComplete()
    {
        var (sut, _, time) = CreateScheduler(Options_());
        var callbackEntered = NewSignal();
        var gate = NewSignal();

        using (sut)
        {
            sut.SetMergeCallback(async _ =>
            {
                callbackEntered.TrySetResult(true);
                await gate.Task;
            });

            sut.ScheduleMerge("source-a");
            time.Advance(Debounce);
            await AwaitSignalAsync(callbackEntered, "the merge should have entered the callback");
            sut.IsMergeInProgress.Should().BeTrue();

            var waitTask = sut.WaitForCurrentMergeAsync();
            waitTask.IsCompleted.Should().BeFalse("wait task should block while merge is in progress");

            gate.TrySetResult(true);

            var winner = await Task.WhenAny(waitTask, Task.Delay(SignalTimeout));
            winner.Should().Be(waitTask, "wait task should complete after merge finishes");
            await waitTask;

            sut.IsMergeInProgress.Should().BeFalse();

            // Second call must not block — proves the semaphore was not leaked.
            var second = sut.WaitForCurrentMergeAsync();
            second.IsCompleted.Should().BeTrue("semaphore should not be leaked");
            await second;
        }
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_AfterDispose_ReturnsImmediately()
    {
        var (sut, _, _) = CreateScheduler(Options_());
        sut.Dispose();

        var waitTask = sut.WaitForCurrentMergeAsync();
        waitTask.IsCompleted.Should().BeTrue("disposed scheduler should return synchronously");
        await waitTask;
    }

    [Fact]
    public void ScheduleMerge_AfterDispose_DoesNotFireCallback()
    {
        var (sut, _, time) = CreateScheduler(Options_());
        var invocations = 0;

        sut.SetMergeCallback(_ =>
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        });

        sut.Dispose();
        sut.ScheduleMerge("source-x");

        time.Advance(Debounce * 2);
        invocations.Should().Be(0);
    }

    [Fact]
    public void ScheduleMerge_DisposedBeforeTimerFires_DoesNotFireCallback()
    {
        var (sut, _, time) = CreateScheduler(Options_());
        var invocations = 0;

        sut.SetMergeCallback(_ =>
        {
            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        });

        sut.ScheduleMerge("source-x");
        sut.Dispose();                  // disposes the ITimer before it becomes due

        time.Advance(Debounce * 2);
        invocations.Should().Be(0);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var (sut, _, _) = CreateScheduler(Options_());

        sut.Dispose();
        var act = () => sut.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void ScheduleMerge_WhenApplicationStopping_DoesNotFireCallback()
    {
        var lifetime = new FakeApplicationLifetime(stoppingCancelled: true);
        var (sut, _, time) = CreateScheduler(Options_(), lifetime);
        var invocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref invocations);
                return Task.CompletedTask;
            });

            sut.ScheduleMerge("source-x");
            time.Advance(Debounce * 2);
        }

        invocations.Should().Be(0);
    }

    [Fact]
    public async Task WaitForCurrentMergeAsync_WhenApplicationStopping_CompletesImmediately()
    {
        var lifetime = new FakeApplicationLifetime(stoppingCancelled: true);
        var (sut, _, _) = CreateScheduler(Options_(), lifetime);

        using (sut)
        {
            var waitTask = sut.WaitForCurrentMergeAsync();
            waitTask.IsCompleted.Should().BeTrue("a stopping application should short-circuit the wait");
            await waitTask;
        }
    }

    [Fact]
    public async Task ScheduleMerge_WhenCallbackThrows_SchedulerRemainsUsable()
    {
        var (sut, logger, time) = CreateScheduler(Options_());
        var firstEntered = NewSignal();
        var secondEntered = NewSignal();
        var secondInvocations = 0;

        using (sut)
        {
            sut.SetMergeCallback(_ =>
            {
                firstEntered.TrySetResult(true);
                throw new InvalidOperationException("boom");
            });

            sut.ScheduleMerge("source-a");
            time.Advance(Debounce);
            await WaitForMergeAsync(sut, firstEntered, "the first callback should have fired");

            sut.IsMergeInProgress.Should().BeFalse("semaphore must be released after a failing callback");

            sut.SetMergeCallback(_ =>
            {
                Interlocked.Increment(ref secondInvocations);
                secondEntered.TrySetResult(true);
                return Task.CompletedTask;
            });

            // The failed run left _mergeScheduled true and _firstPendingInvalidation at TestStart,
            // and only 5s of fake time has passed — well inside MaxInterval — so this takes the
            // normal debounce path.
            sut.ScheduleMerge("source-b");
            time.Advance(Debounce);
            await WaitForMergeAsync(sut, secondEntered, "the second callback should succeed");

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
