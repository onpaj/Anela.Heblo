# Design: Inject `TimeProvider` into `CatalogMergeScheduler`

This is a backend-only refactor (`arch-review.r1.md` → `Skip Design: true`). There is no user-facing component, so the UX/UI section is omitted entirely.

## Component Design

### `CatalogMergeScheduler` (modified)

`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs`

**Responsibility (unchanged):** debounce catalog-invalidation notifications from `CatalogCacheStore` and run a single background merge callback, with a max-interval escape hatch and a single-flight guard.

**What changes:** the class stops reading the ambient clock and stops allocating a BCL timer directly. Both now go through an injected `TimeProvider`.

#### Constructor contract

```csharp
public CatalogMergeScheduler(
    ILogger<CatalogMergeScheduler> logger,
    IOptions<CatalogCacheOptions> options,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider)
```

- `timeProvider` is **required** and **appended last**. No default value, no `?? TimeProvider.System` fallback — a class that can silently fall back to the real clock defeats the point of the change.
- Assigned plainly: `_timeProvider = timeProvider;`. No `ArgumentNullException` guard, matching the three existing parameters in this same constructor (the siblings in this folder do guard, but the local file's style wins for a surgical change).

#### Field contract

| Field | Before | After | Notes |
|-------|--------|-------|-------|
| `_timeProvider` | — | `private readonly TimeProvider _timeProvider;` | New. Placed with the other `readonly` collaborator fields (`_logger`, `_options`, `_applicationStopping`). |
| `_debounceTimer` | `private Timer? _debounceTimer;` | `private ITimer? _debounceTimer;` | `ITimer` extends `IDisposable`, so both existing `Dispose()` call sites compile unchanged. |
| `_lastMergeCompleted` | `private DateTime` = `DateTime.MinValue` | unchanged | Still `DateTime`; still seeded to `MinValue`. |
| `_firstPendingInvalidation` | `private DateTime` = `DateTime.MinValue` | unchanged | Sentinel comparison against `DateTime.MinValue` is preserved. |

`TimeProvider` and `ITimer` live in `System` and resolve under the project's `ImplicitUsings` — no new `using` directive.

#### Clock-read contract

Both reads use `_timeProvider.GetUtcNow().UtcDateTime`, **not** `.DateTime`:

| Site | Before | After |
|------|--------|-------|
| `ScheduleMerge`, line 47 | `var now = DateTime.UtcNow;` | `var now = _timeProvider.GetUtcNow().UtcDateTime;` |
| `ExecuteMergeAsync`, line 102 | `_lastMergeCompleted = DateTime.UtcNow;` | `_lastMergeCompleted = _timeProvider.GetUtcNow().UtcDateTime;` |

**Invariant:** `GetLastMergeTime()` continues to return a `DateTime` whose `Kind` is `DateTimeKind.Utc`. `.DateTime` would yield `Unspecified` and is a behaviour change, however small.

#### Timer-creation contract

| Site | Before | After |
|------|--------|-------|
| `ScheduleMerge`, lines 74-75 | `_debounceTimer = new Timer(async _ => await ExecuteMergeAsync(), null, _options.DebounceDelay, Timeout.InfiniteTimeSpan);` | `_debounceTimer = _timeProvider.CreateTimer(async _ => await ExecuteMergeAsync(), null, _options.DebounceDelay, Timeout.InfiniteTimeSpan);` |

Callback body, state (`null`), due time (`_options.DebounceDelay`), and period (`Timeout.InfiniteTimeSpan`) are all identical. The dispose-then-recreate debounce reset at line 73 is **kept as-is** — switching to `ITimer.Change(...)` would be a rewrite of the debounce mechanism, not a `TimeProvider` migration.

#### Explicitly unchanged in this class

These lines are visible in the same file and superficially look like the same class of problem. They are **out of scope** and a diff touching them should be rejected:

| Line(s) | Code | Why it stays |
|---------|------|--------------|
| 68 | `_ = Task.Run(async () => await ExecuteMergeAsync(), _applicationStopping);` | Fire-and-forget dispatch, not a clock read. Preserved verbatim. |
| 88 | `await _mergeSemaphore.WaitAsync(100)` | A contention timeout, not a wall-clock read. |
| 94, 108, 113, 121 | `System.Diagnostics.Stopwatch` | Elapsed-duration measurement, not a wall clock. |
| 14-16, 21-24 | `_mergeSemaphore`, `_invalidationTimes`, `_timerLock`, `_mergeScheduled`, `_disposed`, `_mergeCallback` | Untouched. |
| 98, 107, 112, 64, 79, 90 | All six log message templates and levels | Asserted by tests; must survive verbatim. |
| 142-155 | `Dispose()` | Untouched. `ITimer?` satisfies `?.Dispose()`. |

### `ICatalogMergeScheduler` (unchanged)

`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ICatalogMergeScheduler.cs`

No member added, removed, or re-typed. `IsMergeInProgress`, `SetMergeCallback`, `ScheduleMerge`, `GetLastMergeTime`, `HasPendingMerge`, `WaitForCurrentMergeAsync`, `Dispose` all keep their exact signatures. This is what keeps the blast radius to one file.

### `CatalogModule` (unchanged)

`backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:101`

```csharp
services.AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>();
```

Stays exactly as written. The container already has `TimeProvider` as a singleton (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`, reached via `Program.cs:109`), and both lifetimes are singleton, so there is no captive dependency. **Do not** convert this to a factory lambda.

### `CatalogMergeSchedulerTests` (rewritten)

`backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs`

**Responsibility:** prove the scheduler's debounce, force-merge, single-flight, disposal, and shutdown behaviour — now against a controllable clock rather than real sleeps.

#### Fixture contract

```csharp
private static readonly DateTimeOffset TestStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

private (CatalogMergeScheduler sut, Mock<ILogger<CatalogMergeScheduler>> logger, FakeTimeProvider time)
    CreateScheduler(
        CatalogCacheOptions options,
        IHostApplicationLifetime? lifetime = null,
        FakeTimeProvider? timeProvider = null)
{
    var time = timeProvider ?? new FakeTimeProvider(TestStart);
    var logger = new Mock<ILogger<CatalogMergeScheduler>>();
    var sut = new CatalogMergeScheduler(
        logger.Object,
        Options.Create(options),
        lifetime ?? new FakeApplicationLifetime(),
        time);
    return (sut, logger, time);
}
```

- `TestStart` is a **fixed** instant, mirroring `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs:9`. It must **not** be `DateTimeOffset.UtcNow` — that would reintroduce wall-clock dependence.
- The `FakeApplicationLifetime` nested class (lines 13-29) is kept verbatim.
- Requires `using Microsoft.Extensions.Time.Testing;` (package `Microsoft.Extensions.TimeProvider.Testing` 8.1.0, already referenced).

#### The two synchronisation primitives

Every test uses exactly these two, and nothing else:

1. **`time.Advance(delta)` — the trigger.** `FakeTimeProvider.Advance` invokes every due timer callback **synchronously on the calling thread before returning**. This is what replaces `await Task.Delay(DebounceDelay)`.

2. **`TaskCompletionSource` — the observation.** The scheduler's timer callback is `async _ => await ExecuteMergeAsync()`. It runs synchronously only up to its first *incomplete* await, so state written after an await (notably `_lastMergeCompleted`, `_mergeScheduled = false`) is not guaranteed to be visible the instant `Advance` returns. Tests therefore signal a `TaskCompletionSource` from inside the merge callback and `await` it with a generous, failure-only timeout.

**Prohibited:** `await Task.Delay(n)` used as a wait for scheduler-driven work. A `Task.Delay` may appear only as the losing branch of a `Task.WhenAny` failure timeout.

**Prohibited:** asserting on `GetLastMergeTime()` / `HasPendingMerge()` immediately after `Advance` without first awaiting the TCS.

#### Options values

Tests use production-like values from `CatalogCacheOptions` — `DebounceDelay = TimeSpan.FromSeconds(5)`, `MaxMergeInterval = TimeSpan.FromMinutes(30)` — rather than today's artificially tiny 50-200 ms values. With a fake clock, large delays cost nothing and the tests exercise the configuration the app actually ships. Where a test needs the force path, it advances the fake clock past `MaxMergeInterval` instead of shrinking the option.

#### Per-test contract

| # | Test | Fake-clock driver | Key assertion change |
|---|------|-------------------|----------------------|
| 1 | `ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay` | `ScheduleMerge` → `Advance(DebounceDelay)` → await TCS | `GetLastMergeTime().Should().Be(TestStart.Add(DebounceDelay).UtcDateTime)` — replaces `BeAfter(testStart)`, which **fails** under a fake clock |
| 2 | `ScheduleMerge_BurstOfCalls_CollapseToSingleCallback` | 5 × (`ScheduleMerge` + `Advance(1s)`), then `Advance(DebounceDelay)` | `invocations == 1`; the "extra window to confirm no second callback" becomes a further `Advance(DebounceDelay)` with no sleep |
| 3 | `ScheduleMerge_BeyondMaxMergeInterval_ForcesImmediateExecution` | `ScheduleMerge` → `Advance(MaxMergeInterval + 1s)` → `ScheduleMerge` (force path) → await TCS | `VerifyLogged(Information, "Force executing merge")` unchanged |
| 4 | `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation` | `Advance(DebounceDelay)` to start a gated merge; `Advance(MaxMergeInterval + 1s)`; `ScheduleMerge` → force path | `VerifyLogged(Debug, "Merge already in progress, skipping")` unchanged. **Still pays ~100 ms real time** on `WaitAsync(100)` — inherent, out of scope |
| 5 | `WaitForCurrentMergeAsync_WhenNoMergeInProgress_CompletesImmediately` | none | Assert the returned task is already completed instead of `Stopwatch < 50 ms` |
| 6 | `WaitForCurrentMergeAsync_WhenMergeInProgress_BlocksUntilComplete` | `Advance(DebounceDelay)` to start a TCS-gated merge | `waitTask.IsCompleted == false` while gated; completes after release. Replaces the poll loop and both `Stopwatch` checks |
| 7 | `WaitForCurrentMergeAsync_AfterDispose_ReturnsImmediately` | none | Assert already-completed instead of `Stopwatch < 50 ms` |
| 8 | `ScheduleMerge_AfterDispose_DoesNotFireCallback` | `Dispose()` → `ScheduleMerge` → `Advance(2 × DebounceDelay)` | `invocations == 0`, no sleep |
| 9 | `ScheduleMerge_DisposedBeforeTimerFires_DoesNotFireCallback` | `ScheduleMerge` → `Dispose()` → `Advance(2 × DebounceDelay)` | `invocations == 0`, no sleep |
| 10 | `Dispose_CalledTwice_DoesNotThrow` | none | unchanged apart from the `CreateScheduler` tuple arity |
| 11 | `ScheduleMerge_WhenApplicationStopping_DoesNotFireCallback` | `Advance(2 × DebounceDelay)` | `invocations == 0`, no sleep |
| 12 | `ScheduleMerge_WhenCallbackThrows_SchedulerRemainsUsable` | `Advance(DebounceDelay)` per attempt, TCS per attempt | The `logger.Verify(LogLevel.Error, …, e.Message == "boom", …, Times.Once)` block survives verbatim |

All 12 names are preserved. No test is deleted or merged.

## Data Schemas

**None.** This change adds, removes, and modifies zero:

- database tables, columns, indexes, or EF migrations
- DTOs, request/response contracts, or MediatR messages
- HTTP endpoints or OpenAPI operations (so no C#/TypeScript client regeneration)
- event payloads or queue messages
- configuration keys (`CatalogCacheOptions` is read, never changed) or Key Vault secrets

### Type-shape deltas (the complete set)

| Symbol | Before | After |
|--------|--------|-------|
| `CatalogMergeScheduler` ctor | `(ILogger, IOptions<CatalogCacheOptions>, IHostApplicationLifetime)` | `(ILogger, IOptions<CatalogCacheOptions>, IHostApplicationLifetime, TimeProvider)` |
| `CatalogMergeScheduler._debounceTimer` | `System.Threading.Timer?` | `System.Threading.ITimer?` |
| `CatalogMergeScheduler._timeProvider` | — | `readonly TimeProvider` |

Everything reachable from outside the class — `ICatalogMergeScheduler` in full, `CatalogCacheOptions` in full, `GetLastMergeTime()`'s `DateTime` return type and its `DateTimeKind.Utc` kind — is byte-identical before and after.
