# Inject `TimeProvider` into `CatalogMergeScheduler` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `CatalogMergeScheduler` take `TimeProvider` by constructor injection and route both its clock reads and its debounce timer through it, then rewrite `CatalogMergeSchedulerTests` to drive a `FakeTimeProvider` instead of real `Task.Delay` sleeps.

**Architecture:** Two lines of production behaviour move onto the injected clock — `DateTime.UtcNow` becomes `_timeProvider.GetUtcNow().UtcDateTime` (twice), and `new Timer(...)` becomes `_timeProvider.CreateTimer(...)` with the field re-typed from `Timer?` to `ITimer?`. Nothing else in the class changes: the semaphore single-flight guard, the `Task.Run` max-interval force path, the `Stopwatch` duration logging, all six log templates, and `Dispose()` are all preserved verbatim. `ICatalogMergeScheduler` and `CatalogModule.cs` need no edit at all, because the container already registers `TimeProvider.System` as a singleton and the scheduler is itself a singleton.

**Tech Stack:** .NET 8, C#, `System.TimeProvider` / `System.Threading.ITimer` (BCL), xUnit 2.9.2, FluentAssertions 6.12.0, Moq 4.20.72, `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 (`FakeTimeProvider`).

---

## File Structure

| File | Responsibility | Action |
|------|----------------|--------|
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs` | Debounces catalog invalidations and runs a single background merge | **Modify** — lines 10-18, 26-34, 47, 74-75, 102 |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` | Unit-tests the scheduler's debounce / force / single-flight / disposal behaviour | **Modify** — helper + all 12 tests |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ICatalogMergeScheduler.cs` | Public contract | **No change** |
| `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` | DI registration (line 101) | **No change** |

Task 1 delivers a compiling, fully-passing increment on its own: the production class is converted while the existing real-time tests stay byte-identical apart from one constructor argument, so their continued passing *is* the behaviour-preservation proof. Task 2 then converts those tests to fake time.

---

### task: inject-timeprovider-into-catalog-merge-scheduler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs:31-40` (the `CreateScheduler` helper only)

#### Goal

Satisfy FR-1, FR-2, FR-3 and FR-4 from `spec.r1.md`: `CatalogMergeScheduler` accepts an injected `TimeProvider`, uses it for both clock reads and for creating the debounce timer, and no longer references `DateTime.UtcNow` or `new Timer(...)`. The 12 existing tests are left untouched except for passing `TimeProvider.System` — if they all still pass, production behaviour is provably unchanged (NFR-2).

#### Context you need before touching code

- **`TimeProvider` is already registered.** `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` does `services.AddSingleton(TimeProvider.System);` inside `AddCrossCuttingServices()`, which `backend/src/Anela.Heblo.API/Program.cs:109` calls. `CatalogMergeScheduler` is registered at `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:101` as `services.AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>();`. Both are singletons, so there is no captive-dependency problem and **`CatalogModule.cs` must not be edited** — constructor injection resolves the new parameter automatically. Do not convert line 101 to a factory lambda.
- **`TimeProvider` and `ITimer` are BCL types in the `System` namespace.** The project has `<ImplicitUsings>enable</ImplicitUsings>`, so **no new `using` directive is needed** and no `using` becomes orphaned (`System.Threading.Timer` was also implicitly resolved).
- **Use `.UtcDateTime`, not `.DateTime`.** `DateTime.UtcNow` yields `DateTimeKind.Utc`. `GetUtcNow().UtcDateTime` preserves that; `GetUtcNow().DateTime` silently downgrades it to `DateTimeKind.Unspecified`. `GetLastMergeTime()` returns this value. The repo uses both idioms (57 `.DateTime` vs 23 `.UtcDateTime`), so there is no house convention to follow — behaviour preservation is the tiebreak. The nearest analogues are `CatalogMergeService.cs:281` and `CatalogDataRefreshService.cs:232`, which both use `.UtcDateTime`.
- **Append the parameter last, and make it required.** The three siblings in this folder each put `TimeProvider` in a different position, so there is nothing to match. Appending keeps the diff to a one-line addition at every call site. Do **not** write `TimeProvider? timeProvider = null` with a `?? TimeProvider.System` fallback — that lets a test silently keep using the real clock.
- **Do not add a null guard.** `CatalogMergeService` and `CatalogCacheStore` guard with `?? throw new ArgumentNullException(...)`, but `CatalogMergeScheduler`'s own constructor guards none of its three existing parameters. Match the file, not the folder. Adding a guard only for the new parameter is inconsistent; adding guards to all four is scope creep.
- **Keep the dispose-then-recreate debounce reset.** Line 73 is `_debounceTimer?.Dispose();` immediately before the timer is re-created. Switching to `ITimer.Change(...)` would be a rewrite of the debounce mechanism, not a `TimeProvider` migration. Keep the two-line shape.

#### Explicitly do NOT touch these lines in `CatalogMergeScheduler.cs`

They look like the same class of problem and they are all out of scope. A diff that changes them should be rejected.

| Line(s) | Code | Why it stays |
|---------|------|--------------|
| 68 | `_ = Task.Run(async () => await ExecuteMergeAsync(), _applicationStopping);` | Thread-pool dispatch, not a clock read |
| 88 | `await _mergeSemaphore.WaitAsync(100)` | A contention timeout, not a wall-clock read |
| 94, 108, 113, 121 | `System.Diagnostics.Stopwatch` | Elapsed-duration measurement, not a wall clock |
| 64, 79, 90, 98, 107, 112 | The six log message templates and their levels | Asserted by three tests; must survive verbatim |
| 142-155 | `Dispose()` | `ITimer?` already satisfies `?.Dispose()` |

#### Implementation steps

- [ ] **Step 1: Add the `_timeProvider` field**

In `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs`, the readonly collaborator fields at lines 10-12 currently read:

```csharp
    private readonly ILogger<CatalogMergeScheduler> _logger;
    private readonly CatalogCacheOptions _options;
    private readonly CancellationToken _applicationStopping;
```

Change to:

```csharp
    private readonly ILogger<CatalogMergeScheduler> _logger;
    private readonly CatalogCacheOptions _options;
    private readonly CancellationToken _applicationStopping;
    private readonly TimeProvider _timeProvider;
```

- [ ] **Step 2: Re-type the debounce timer field**

Line 18 currently reads:

```csharp
    private Timer? _debounceTimer;
```

Change to:

```csharp
    private ITimer? _debounceTimer;
```

- [ ] **Step 3: Add the constructor parameter**

Lines 26-34 currently read:

```csharp
    public CatalogMergeScheduler(
        ILogger<CatalogMergeScheduler> logger,
        IOptions<CatalogCacheOptions> options,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _options = options.Value;
        _applicationStopping = applicationLifetime.ApplicationStopping;
    }
```

Change to:

```csharp
    public CatalogMergeScheduler(
        ILogger<CatalogMergeScheduler> logger,
        IOptions<CatalogCacheOptions> options,
        IHostApplicationLifetime applicationLifetime,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _options = options.Value;
        _applicationStopping = applicationLifetime.ApplicationStopping;
        _timeProvider = timeProvider;
    }
```

- [ ] **Step 4: Replace the clock read in `ScheduleMerge`**

Line 47 currently reads:

```csharp
        var now = DateTime.UtcNow;
```

Change to:

```csharp
        var now = _timeProvider.GetUtcNow().UtcDateTime;
```

- [ ] **Step 5: Replace the timer construction in `ScheduleMerge`**

Lines 72-75 currently read:

```csharp
            // Reset debounce timer
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(async _ => await ExecuteMergeAsync(),
                null, _options.DebounceDelay, Timeout.InfiniteTimeSpan);
```

Change to (line 73 stays exactly as it is):

```csharp
            // Reset debounce timer
            _debounceTimer?.Dispose();
            _debounceTimer = _timeProvider.CreateTimer(async _ => await ExecuteMergeAsync(),
                null, _options.DebounceDelay, Timeout.InfiniteTimeSpan);
```

- [ ] **Step 6: Replace the clock read in `ExecuteMergeAsync`**

Line 102 currently reads:

```csharp
            _lastMergeCompleted = DateTime.UtcNow;
```

Change to:

```csharp
            _lastMergeCompleted = _timeProvider.GetUtcNow().UtcDateTime;
```

- [ ] **Step 7: Verify no clock reads or raw timers remain**

Run:

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O
grep -n "DateTime\.UtcNow\|DateTime\.Now\|new Timer(" backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs
```

Expected: **no output**, exit code 1.

Then confirm the DI site and the interface were not touched:

```bash
git diff --name-only
```

Expected: exactly two paths — `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs` and (after Step 8) `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs`. `CatalogModule.cs` and `ICatalogMergeScheduler.cs` must **not** appear.

- [ ] **Step 8: Fix the one hand-construction site so the solution compiles**

`backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` lines 31-40 currently read:

```csharp
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
```

Change **only** the constructor call, passing the real system clock so the existing real-time tests keep their current meaning:

```csharp
    private (CatalogMergeScheduler sut, Mock<ILogger<CatalogMergeScheduler>> logger)
        CreateScheduler(CatalogCacheOptions options, IHostApplicationLifetime? lifetime = null)
    {
        var logger = new Mock<ILogger<CatalogMergeScheduler>>();
        var sut = new CatalogMergeScheduler(
            logger.Object,
            Options.Create(options),
            lifetime ?? new FakeApplicationLifetime(),
            TimeProvider.System);
        return (sut, logger);
    }
```

Do **not** change any of the 12 test methods in this step. Task 2 does that.

Confirm this is the only construction site:

```bash
grep -rn "new CatalogMergeScheduler" backend/
```

Expected: exactly one hit, `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs:35`.

- [ ] **Step 9: Build**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O/backend
dotnet build
```

Expected: `Build succeeded.` with **0 Error(s)** and no new warnings.

- [ ] **Step 10: Run the scheduler tests unchanged — this is the behaviour-preservation gate**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogMergeSchedulerTests"
```

Expected: **Passed! - Failed: 0, Passed: 12**.

Because these 12 tests still drive the real clock and were not otherwise edited, their passing proves `TimeProvider.System.GetUtcNow()` and `TimeProvider.System.CreateTimer()` behave identically to `DateTime.UtcNow` and `new Timer(...)` here. If any test fails at this step, the production edit is wrong — fix it before moving on; do **not** loosen the test.

- [ ] **Step 11: Run the wider catalog suite**

`CatalogCacheStore` calls `ScheduleMerge` at lines 81, 95 and 385, so its tests exercise the scheduler indirectly.

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Catalog"
```

Expected: `Failed: 0`.

- [ ] **Step 12: Format**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O/backend
dotnet format
git diff --stat
```

Expected: `dotnet format` reports no remaining issues, and it introduces no changes to files outside the two listed above.

- [ ] **Step 13: Commit**

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs
git commit -m "refactor(catalog): inject TimeProvider into CatalogMergeScheduler

Replaces DateTime.UtcNow with _timeProvider.GetUtcNow().UtcDateTime and
new Timer(...) with _timeProvider.CreateTimer(...), matching every sibling
class in Features/Catalog/Infrastructure. No public contract change.

Refs #3886"
```

---

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
