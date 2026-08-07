### task: convert-merge-scheduler-tests-to-fake-time-provider

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` (whole file)

#### Goal

Satisfy FR-5 and NFR-1 from `spec.r1.md`: drive all 12 tests with `FakeTimeProvider` so debounce and max-interval behaviour is exercised without real sleeping, and so the "assert nothing else fired" cases stop depending on a fixed real-time window.

#### Context you need before touching code

- **`FakeTimeProvider` is already available.** `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 is referenced in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`. Add `using Microsoft.Extensions.Time.Testing;` to the test file. Eleven other test files already do this — see `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs:3` and `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureErpResilienceServiceTests.cs:7`.
- **How `Advance` interacts with the scheduler.** `FakeTimeProvider.Advance(delta)` moves the fake clock and invokes every due timer callback **synchronously, on the calling thread, before `Advance` returns**. The scheduler's callback is `async _ => await ExecuteMergeAsync()`, so it runs synchronously only as far as its first *incomplete* await. State that `ExecuteMergeAsync` writes **after** awaiting the merge callback — `_lastMergeCompleted` (line 102), `_mergeScheduled = false` (line 103), `_firstPendingInvalidation` reset (line 104), and the semaphore release in `finally` (line 119) — is therefore **not** guaranteed to be visible the instant `Advance` returns.
- **The two-step observation pattern.** Signal a `TaskCompletionSource` from *inside* the merge callback (which proves `ExecuteMergeAsync` has acquired the semaphore and entered the callback), then `await sut.WaitForCurrentMergeAsync()` — that method waits on `_mergeSemaphore`, which is only released in the `finally` after all bookkeeping. Together they are a fully deterministic barrier with no polling and no sleeping. This plan provides a `WaitForMergeAsync` helper that encapsulates it; use it everywhere a completed merge is observed.
- **`FakeTimeProvider`'s default epoch is `2000-01-01T00:00:00Z`.** The current `ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay` asserts `GetLastMergeTime().Should().BeAfter(testStart)` where `testStart = DateTime.UtcNow` (2026) — that assertion **fails** under a fake clock. Seed the provider with a fixed `TestStart` and assert the exact expected instant instead. Do **not** seed with `DateTimeOffset.UtcNow`; that would put wall-clock dependence straight back in.
- **Advancing past a *pending* debounce timer fires it.** Any test that needs to reach the max-interval force path must use a `DebounceDelay` **longer** than the amount it advances, otherwise the debounce timer fires first, the merge runs, and `_firstPendingInvalidation` is reset to `DateTime.MinValue` — killing the force path. This is why tasks 3 uses `DebounceDelay = 1 hour`.
- **One test still pays real time, by design.** `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation` reaches `await _mergeSemaphore.WaitAsync(100)` on the *contended* path, which is a real 100 ms wait. `_mergeSemaphore.WaitAsync(100)` is explicitly out of scope (`spec.r1.md` → Out of Scope), so do not try to fake it away.
- **Prohibited:** `await Task.Delay(n)` used as a *wait* for scheduler-driven work. `Task.Delay` may appear **only** as the losing branch of a `Task.WhenAny` failure timeout.
- **Preserve all 12 test names and the three log assertions.** `VerifyLogged(logger, LogLevel.Information, "Force executing merge")`, `VerifyLogged(logger, LogLevel.Debug, "Merge already in progress, skipping")`, and the `logger.Verify(LogLevel.Error, …, e.Message == "boom", …, Times.Once)` block are the NFR-2 behaviour guard. They must survive.

#### Implementation steps

- [ ] **Step 1: Replace the file header, fixtures and helpers**

Replace everything from the top of the file through the end of the `VerifyLogged` helper (currently lines 1-54) with:

```csharp
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
```

`VerifyLogged` is unchanged from the current file. `FakeApplicationLifetime` is unchanged. `Options_` has a trailing underscore because `Options` is already the `Microsoft.Extensions.Options.Options` static class used by `Options.Create(...)`.

- [ ] **Step 2: Rewrite test 1 — `ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay`**

Replace the current method (lines 56-89) with:

```csharp
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
```

The `GetLastMergeTime()` assertion replaces `BeAfter(testStart)` and is strictly stronger — it pins the exact instant *and* the `DateTimeKind`, which is the FR-2 invariant.

- [ ] **Step 3: Rewrite test 2 — `ScheduleMerge_BurstOfCalls_CollapseToSingleCallback`**

Replace the current method (lines 91-126) with:

```csharp
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
```

This replaces `await Task.Delay(450); // extra window to confirm no second callback` with a fake-clock advance — the negative assertion no longer depends on a real-time window at all.

- [ ] **Step 4: Rewrite test 3 — `ScheduleMerge_BeyondMaxMergeInterval_ForcesImmediateExecution`**

Replace the current method (lines 128-162) with:

```csharp
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
```

The force path dispatches via `Task.Run` (`CatalogMergeScheduler.cs:68`), which `Advance` cannot make synchronous — that is exactly why `WaitForMergeAsync` awaits a signal rather than asserting immediately.

- [ ] **Step 5: Rewrite test 4 — `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation`**

Replace the current method (lines 164-207) with:

```csharp
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
```

- [ ] **Step 6: Rewrite tests 5-7 — the `WaitForCurrentMergeAsync` trio**

Replace the current methods (lines 209-278) with:

```csharp
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
```

All three `Stopwatch`-based "< 50 ms" assertions are gone; `IsCompleted` is a stronger and instantaneous statement of the same intent.

- [ ] **Step 7: Rewrite tests 8-9 — the disposal pair**

Replace the current methods (lines 280-326) with:

```csharp
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
```

Both become **synchronous** (`void`, no `async`) — with a fake clock there is nothing to await. This removes `Task.Delay(300)` and `Task.Delay(500)` outright.

- [ ] **Step 8: Rewrite tests 10-11 — double-dispose and shutdown**

Replace the current methods (lines 328-364) with:

```csharp
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
```

- [ ] **Step 9: Delete the now-obsolete `WaitForCurrentMergeAsync_WhenApplicationStopping_CompletesImmediately`? No — rewrite it**

Replace the current method (lines 366-380) with:

```csharp
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
```

Note: the current file has **13** `[Fact]` methods, not 12 — this one plus the 12 listed in `spec.r1.md` FR-5. All 13 are preserved.

- [ ] **Step 10: Rewrite the last test — `ScheduleMerge_WhenCallbackThrows_SchedulerRemainsUsable`**

Replace the current method (lines 382-434) with:

```csharp
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
```

The final `}` closes the class.

- [ ] **Step 11: Verify no sleeps remain as synchronisation**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O
grep -n "Task.Delay" backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs
```

Expected: every hit is inside a `Task.WhenAny(...)` failure timeout (`Task.Delay(SignalTimeout)`), and there are exactly **three** — one in `WaitForMergeAsync`, one in `AwaitSignalAsync`, one in `WaitForCurrentMergeAsync_WhenMergeInProgress_BlocksUntilComplete`. Any bare `await Task.Delay(...)` is a plan violation.

```bash
grep -n "Stopwatch\|DateTime.UtcNow" backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs
```

Expected: **no output**.

- [ ] **Step 12: Build**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O/backend
dotnet build
```

Expected: `Build succeeded.`, 0 errors.

- [ ] **Step 13: Run the scheduler tests and check the wall-clock time**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogMergeSchedulerTests" -v n
```

Expected: **Failed: 0, Passed: 13**. The reported duration for the class should be a few hundred milliseconds — the only structural real-time cost left is the ~100 ms `_mergeSemaphore.WaitAsync(100)` inside `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation`. Compare against the pre-change duration recorded in task 1, Step 10; it should be materially lower.

- [ ] **Step 14: Run the tests repeatedly to prove determinism**

Flakiness is the whole point of this change, so prove it:

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O/backend
for i in 1 2 3 4 5; do
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~CatalogMergeSchedulerTests" --no-build || echo "RUN $i FAILED"
done
```

Expected: five clean runs, no `RUN n FAILED` line.

- [ ] **Step 15: Run the wider catalog suite and format**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Catalog"
dotnet format
```

Expected: `Failed: 0`, and `dotnet format` leaves files outside the two in scope untouched (`git diff --name-only` still shows only `CatalogMergeScheduler.cs` and `CatalogMergeSchedulerTests.cs`).

- [ ] **Step 16: Commit**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs
git commit -m "test(catalog): drive CatalogMergeSchedulerTests with FakeTimeProvider

Replaces real-time Task.Delay waits with FakeTimeProvider.Advance plus
TaskCompletionSource barriers. All 13 tests and the three log assertions
are preserved. The only remaining real-time cost is the contended
_mergeSemaphore.WaitAsync(100), which is out of scope.

Refs #3886"
```

---

## Self-Review

**1. Spec coverage**

| Spec item | Covered by |
|-----------|-----------|
| FR-1 (constructor takes `TimeProvider`) | Task 1, Steps 1 + 3; verified Step 7 |
| FR-2 (clock reads via `TimeProvider`) | Task 1, Steps 4 + 6; verified Step 7; `DateTimeKind` invariant asserted in Task 2, Step 2 |
| FR-3 (timer via `TimeProvider`) | Task 1, Steps 2 + 5; verified Step 7; fake-clock behaviour proven in Task 2, Steps 2/3/7 |
| FR-4 (all construction sites updated) | Task 1, Step 8; `CatalogModule.cs` explicitly left alone (Step 7 `git diff --name-only` check) |
| FR-5 (tests converted to `FakeTimeProvider`) | Task 2, Steps 1-10; all 13 test names preserved |
| NFR-1 (runtime + determinism) | Task 2, Steps 11, 13, 14 |
| NFR-2 (behaviour preservation) | Task 1, Step 10 — the unmodified real-time tests must pass against the refactored class; plus the three surviving log assertions |
| NFR-3 (residual non-determinism documented) | Task 2 context section + the inline comment in Step 5 |
| Arch amendment 1 (append parameter last) | Task 1, Step 3 |
| Arch amendment 2 (no null guard) | Task 1 context section |
| Arch amendment 3 (`.UtcDateTime`, `Kind` assertion) | Task 1 context + Task 2, Step 2 |
| Arch amendment 4 (fixed fake epoch, exact assertion) | Task 2, Steps 1 + 2 |
| Arch amendment 5 (TCS synchronisation rule) | Task 2, Step 1 (`WaitForMergeAsync`) + Step 11 grep gate |
| Arch amendment 6 (production-like option values) | Task 2, Step 1 (`Debounce = 5s`, `MaxInterval = 30min`) |
| Arch amendment 7 (honest NFR-1 target) | Task 2, Step 13 |
| Arch amendment 8 (`CatalogModule.cs` untouched) | Task 1, Step 7 |

No gaps.

**2. Placeholder scan**

Every code step contains the complete replacement text. No "TBD", no "similar to task N", no "add error handling". The full rewritten test file is spelled out across Task 2, Steps 1-10.

**3. Type consistency**

- `CreateScheduler` returns a 3-tuple `(sut, logger, time)` in Task 2 and is destructured as `(sut, _, time)` / `(sut, logger, time)` / `(sut, _, _)` consistently in all 13 tests.
- Task 1, Step 8 keeps the **2**-tuple shape — deliberately, because Task 1 must not touch the 13 test bodies. Task 2, Step 1 replaces the helper wholesale with the 3-tuple version at the same time it rewrites every caller, so the two shapes never coexist.
- `Options_(debounce, maxInterval)`, `NewSignal()`, `WaitForMergeAsync(sut, signal, because)`, `AwaitSignalAsync(signal, because)` and `VerifyLogged(logger, level, substring)` are each defined once in Step 1 and used with matching arity everywhere.
- `Debounce`, `MaxInterval`, `TestStart` and `SignalTimeout` are the only shared constants and are all declared in Step 1.
- `ITimer?` (production field) and `FakeTimeProvider` (test) never appear in each other's file.

**4. Discrepancy found and corrected**

`spec.r1.md` FR-5 lists 12 tests. The file actually contains **13** `[Fact]` methods — the spec's list omits `WaitForCurrentMergeAsync_WhenApplicationStopping_CompletesImmediately`. Task 2, Step 9 covers it explicitly so it is not silently dropped.
