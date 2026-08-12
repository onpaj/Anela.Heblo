# Specification: Inject `TimeProvider` into `CatalogMergeScheduler`

## Summary

`CatalogMergeScheduler` is the only time-dependent class in `Features/Catalog/Infrastructure/` that does not take `TimeProvider` by constructor injection. It reads `DateTime.UtcNow` directly and constructs a raw `System.Threading.Timer`, which makes its debounce and max-merge-interval logic untestable without real wall-clock sleeps. This change injects `TimeProvider`, routes all clock reads and the debounce timer through it, and rewrites `CatalogMergeSchedulerTests` to drive a `FakeTimeProvider` instead of `Task.Delay`.

This is a pure internal refactor. No public API, DTO, database schema, HTTP endpoint, or UI surface changes.

## Background

The project has an actively-enforced (though undocumented) convention: production classes that need the current time take `TimeProvider` by constructor injection rather than calling `DateTime.Now`/`DateTime.UtcNow`. Six prior `arch-review` issues — #3773, #3748, #3403, #3495, #3488, #3633 — closed exactly this gap in other classes. `TimeProvider` is already registered as a singleton in the API composition root (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`, `services.AddSingleton(TimeProvider.System)`), and the test project already references `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`), which supplies `FakeTimeProvider`.

Within `Features/Catalog/Infrastructure/` specifically, every sibling already follows the convention:

- `CatalogMergeService.cs:21,26` — `private readonly TimeProvider _timeProvider;` + ctor parameter
- `CatalogCacheStore.cs:46,55` — same
- `CatalogDataRefreshService.cs:40,62` — same

`CatalogMergeScheduler` is the outlier. Its constructor (`CatalogMergeScheduler.cs:26-34`) takes only `ILogger`, `IOptions<CatalogCacheOptions>`, and `IHostApplicationLifetime`.

### Concrete cost today

`backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` cannot fake time, so all 12 of its tests pay real wall-clock time and several assert *absence* of an event within a fixed real-time window — an inherently race-prone shape:

| Line | Real-time cost / race |
|------|----------------------|
| 80 | `await Task.WhenAny(callbackFired.Task, Task.Delay(2000))` |
| 118, 121 | 2000 ms budget + `await Task.Delay(450)` "extra window to confirm no second callback" |
| 150-156 | `Task.Delay(60)` to exceed `MaxMergeInterval`, then a 1000 ms budget, then `Task.Delay(50)` |
| 187-191, 196 | Poll loop with `DateTime.UtcNow` deadline, then `Task.Delay(200)` |
| 241-249, 254 | Poll loop, `Task.Delay(100)`, 1000 ms budget |
| 300 | `Task.Delay(300)` — `> 2 × DebounceDelay` |
| 324 | `Task.Delay(500)` — "well beyond DebounceDelay" |
| 360 | `Task.Delay(300)` |
| 404, 407, 419, 422 | two 2000 ms budgets plus two 50 ms settles |

## Functional Requirements

### FR-1: `CatalogMergeScheduler` accepts an injected `TimeProvider`

Add a `TimeProvider timeProvider` constructor parameter to `CatalogMergeScheduler` and store it in a `private readonly TimeProvider _timeProvider` field, matching the shape used by `CatalogMergeService`, `CatalogCacheStore`, and `CatalogDataRefreshService`.

**Acceptance criteria:**
- `CatalogMergeScheduler`'s constructor signature includes `TimeProvider timeProvider`.
- The class holds `private readonly TimeProvider _timeProvider;`.
- Parameter ordering follows the sibling convention in this folder (`TimeProvider` sits among the injected collaborators, not as an optional trailing parameter with a default value).
- The class contains no `DateTime.Now` or `DateTime.UtcNow` reference after the change (`grep -n "DateTime\.\(Utc\)\?Now" CatalogMergeScheduler.cs` returns nothing).

### FR-2: Clock reads go through `TimeProvider`

Replace both direct clock reads with `_timeProvider.GetUtcNow().UtcDateTime`:

- `CatalogMergeScheduler.cs:47` — `var now = DateTime.UtcNow;` in `ScheduleMerge`, used to seed `_firstPendingInvalidation` and to compute `timeSinceFirstInvalidation` against `_options.MaxMergeInterval` (lines 55-70).
- `CatalogMergeScheduler.cs:102` — `_lastMergeCompleted = DateTime.UtcNow;` in `ExecuteMergeAsync`.

`_lastMergeCompleted` and `_firstPendingInvalidation` remain `DateTime` fields initialised to `DateTime.MinValue`, and `GetLastMergeTime()` keeps returning `DateTime` — the public interface `ICatalogMergeScheduler.GetLastMergeTime()` is unchanged.

**Acceptance criteria:**
- With a `FakeTimeProvider` set to time `T`, calling `ScheduleMerge` then driving the merge to completion makes `GetLastMergeTime()` return exactly the fake provider's current time — not a wall-clock value.
- The max-merge-interval force path triggers when the *fake* clock has advanced past `MaxMergeInterval` since the first pending invalidation, with no real time elapsed.

### FR-3: The debounce timer is created through `TimeProvider`

Replace `new Timer(...)` (`CatalogMergeScheduler.cs:74-75`) with `_timeProvider.CreateTimer(...)`, and change the field type at `CatalogMergeScheduler.cs:18` from `System.Threading.Timer?` to `ITimer?`.

The callback, initial due time (`_options.DebounceDelay`), and period (`Timeout.InfiniteTimeSpan`) are unchanged. Disposal semantics are unchanged: `ScheduleMerge` disposes the previous timer before creating a new one (line 73), and `Dispose()` disposes and nulls it under `_timerLock` (lines 148-152). `ITimer` implements `IDisposable`, so both call sites compile unchanged.

**Acceptance criteria:**
- `_debounceTimer` is declared as `private ITimer? _debounceTimer;`.
- The file contains no `new Timer(` expression.
- With `FakeTimeProvider`, advancing the fake clock by `DebounceDelay` fires the merge callback; advancing by less than `DebounceDelay` does not.
- A burst of `ScheduleMerge` calls, each followed by a fake-clock advance smaller than `DebounceDelay`, still collapses to exactly one callback after a final advance of `DebounceDelay` (debounce reset behaviour is preserved).
- Disposing the scheduler before the fake clock reaches `DebounceDelay`, then advancing past it, fires no callback.

### FR-4: Every construction site is updated

`CatalogMergeScheduler` is resolved from DI at exactly one production site and constructed by hand at exactly one test site:

- Production: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:101` — `services.AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>();`. Because `TimeProvider` is already registered as a singleton by `AddCrossCuttingServices`, this line requires **no change**; the container resolves the new parameter automatically.
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs:35` — the only `new CatalogMergeScheduler(...)` in the repository.

**Acceptance criteria:**
- `grep -rn "new CatalogMergeScheduler" backend/` returns only the test helper.
- `dotnet build` succeeds with no new warnings.
- Resolving `ICatalogMergeScheduler` from the application's service provider still succeeds (covered by existing API/DI smoke tests, if any; otherwise by `dotnet build` plus the module registration being unchanged).

### FR-5: `CatalogMergeSchedulerTests` is converted to `FakeTimeProvider`

Rewrite `CatalogMergeSchedulerTests` so timing is driven by `FakeTimeProvider.Advance(...)` rather than `Task.Delay`. The `CreateScheduler` helper gains an optional `TimeProvider? timeProvider = null` parameter (defaulting to a fresh `FakeTimeProvider`) and returns the provider so tests can advance it.

All 12 existing test cases must be preserved by behaviour, not deleted:

1. `ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay`
2. `ScheduleMerge_BurstOfCalls_CollapseToSingleCallback`
3. `ScheduleMerge_BeyondMaxMergeInterval_ForcesImmediateExecution`
4. `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation`
5. `WaitForCurrentMergeAsync_WhenNoMergeInProgress_CompletesImmediately`
6. `WaitForCurrentMergeAsync_WhenMergeInProgress_BlocksUntilComplete`
7. `WaitForCurrentMergeAsync_AfterDispose_ReturnsImmediately`
8. `ScheduleMerge_AfterDispose_DoesNotFireCallback`
9. `ScheduleMerge_DisposedBeforeTimerFires_DoesNotFireCallback`
10. `Dispose_CalledTwice_DoesNotThrow`
11. `ScheduleMerge_WhenApplicationStopping_DoesNotFireCallback`
12. `ScheduleMerge_WhenCallbackThrows_SchedulerRemainsUsable`

**Acceptance criteria:**
- Every test that previously waited on `DebounceDelay` or `MaxMergeInterval` elapsing now calls `fakeTime.Advance(...)` instead of `await Task.Delay(...)` for that purpose.
- Assertions that previously depended on `DateTime.UtcNow` (e.g. `GetLastMergeTime().Should().BeAfter(testStart)` at line 87) assert an exact fake time instead.
- `dotnet test --filter FullyQualifiedName~CatalogMergeSchedulerTests` passes, and the class's total wall-clock runtime drops materially versus the current suite (see NFR-1).
- No test asserts absence of a callback by sleeping for a fixed real-time window when a fake-clock advance can express the same thing.

## Non-Functional Requirements

### NFR-1: Test runtime and determinism

- `CatalogMergeSchedulerTests` currently spends roughly 2.0-2.5 s of unavoidable real sleeping across its cases (the `Task.Delay(2000)` budgets only cost their full value on failure, but the unconditional `Task.Delay(300)`, `Task.Delay(450)`, `Task.Delay(500)`, `Task.Delay(200)`, and several `Task.Delay(50)`/`Task.Delay(100)` settles always do). Target: the whole class runs in well under 1 s of wall time.
- No test may depend on a fixed real-time window to prove that something did *not* happen. Where the production code still hands work to the thread pool (see NFR-3), a bounded await on a `TaskCompletionSource` is acceptable, but a bare `Task.Delay` used as a synchronisation primitive is not.

### NFR-2: Behaviour preservation

The change is a refactor. Under `TimeProvider.System` — the value DI supplies in production — the scheduler must behave identically to today: same debounce reset semantics, same max-interval force path, same semaphore-based single-flight guard, same disposal and application-stopping short-circuits, same log messages and levels (`"Force executing merge due to max interval {MaxInterval}ms reached"`, `"Merge scheduled for source {DataSource}, debounce delay {Delay}ms"`, `"Merge already in progress, skipping"`, `"Starting background merge operation"`, `"Background merge completed in {Duration}ms"`, `"Background merge failed after {Duration}ms"`). Three existing tests assert on these strings via `VerifyLogged`.

### NFR-3: Residual non-determinism is documented, not hidden

Two code paths remain outside `TimeProvider`'s control and will still require a real await in tests:

- The max-interval force path at `CatalogMergeScheduler.cs:68` — `_ = Task.Run(async () => await ExecuteMergeAsync(), _applicationStopping);` — dispatches to the thread pool. `FakeTimeProvider.Advance` cannot make that observable synchronously.
- `ExecuteMergeAsync` awaits `_mergeSemaphore.WaitAsync(100)` at line 88, a real 100 ms timeout on the *contended* path only.

Tests touching those paths must synchronise on a `TaskCompletionSource` signalled from inside the merge callback (with a generous failure-only timeout), not on a fixed sleep. Whether `Task.Run` and the `WaitAsync(100)` literal should also be made time-provider-driven is explicitly **out of scope** (see Out of Scope) — the arch-review issue names only `DateTime.UtcNow` and `new Timer(...)`.

### NFR-4: Security

None. No authentication, authorisation, data exposure, or external I/O is touched.

## Data Model

Unchanged. No entities, DTOs, or persistence types are added or modified.

## API / Interface Design

### Changed constructor (the only signature change)

```csharp
// backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs
public CatalogMergeScheduler(
    ILogger<CatalogMergeScheduler> logger,
    IOptions<CatalogCacheOptions> options,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider)
```

### Unchanged public surface

`ICatalogMergeScheduler` (`ICatalogMergeScheduler.cs`) is untouched — `IsMergeInProgress`, `SetMergeCallback`, `ScheduleMerge`, `GetLastMergeTime`, `HasPendingMerge`, `WaitForCurrentMergeAsync`, `Dispose`. No HTTP endpoints, no MediatR requests, no OpenAPI regeneration, no frontend impact.

### Changed private field

```csharp
private ITimer? _debounceTimer;   // was: private Timer? _debounceTimer;
```

## Dependencies

- **`TimeProvider`** — .NET 8 BCL (`System`). Already registered: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`. No new package.
- **`ITimer` / `TimeProvider.CreateTimer`** — .NET 8 BCL. This would be the codebase's **first** use of `TimeProvider.CreateTimer` (`grep -rn "CreateTimer\|ITimer" backend/src` currently returns nothing), so there is no in-repo precedent to copy for the timer half of the change; the clock-read half has many.
- **`FakeTimeProvider`** — `Microsoft.Extensions.TimeProvider.Testing` 8.1.0, already referenced in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`. Used in ~11 existing test files, e.g. `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureErpResilienceServiceTests.cs`.
- No database migration, no configuration change, no Key Vault secret.

## Out of Scope

- Changing `ICatalogMergeScheduler` or any other public contract.
- Replacing `_ = Task.Run(...)` on the max-interval force path with a timer-based or synchronous dispatch. The fire-and-forget shape is preserved.
- Replacing the literal `100` in `_mergeSemaphore.WaitAsync(100)` with an option or a `TimeProvider`-driven timeout.
- Replacing `System.Diagnostics.Stopwatch` in `ExecuteMergeAsync` (lines 94, 108, 113, 121) with `TimeProvider.GetTimestamp()`/`GetElapsedTime()`. Elapsed-duration logging is not what the arch-review issue flags, and `Stopwatch` is not a wall-clock read.
- Auditing or fixing `DateTime.UtcNow` usage in any other file, including other `Catalog/Infrastructure/` classes already found compliant.
- Adding new scheduler behaviour (retry, backoff, metrics, cancellation of an in-flight merge).
- Documenting the `TimeProvider` convention in `docs/` — desirable, but a separate change.

## Open Questions

None.

## Status: COMPLETE
