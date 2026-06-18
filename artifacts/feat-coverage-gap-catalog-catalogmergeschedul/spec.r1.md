# Specification: Unit Test Coverage for CatalogMergeScheduler

## Summary
Raise unit-test line coverage of `CatalogMergeScheduler` from 54.3% to at least 60% (project threshold) by adding tests for its currently-uncovered concurrency, lifecycle, and scheduling branches. The result is a regression net for the debounce, max-interval force, semaphore-skip, disposed-guard, and wait-for-merge invariants — all of which silently degrade if broken.

## Background
`CatalogMergeScheduler` (backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs) coordinates debounced background catalog refreshes. Its current 54.3% line coverage falls below the 60% filter threshold. The weekly coverage-gap routine (CI run #27416879267, commit 3a6b7f99) flagged five uncovered branches whose silent failure modes are serious:

- A broken debounce lets redundant merges pile up under invalidation bursts.
- A broken max-interval guard means invalidations can accumulate indefinitely without ever triggering a merge.
- A broken disposed double-check lets timer callbacks fire after disposal, writing to a `ConcurrentDictionary` and invoking the merge callback against torn-down resources.
- A broken semaphore-skip lets concurrent merges run on top of each other.
- A broken `WaitForCurrentMergeAsync` returns immediately when a merge is in progress, defeating its callers (e.g. graceful shutdown, read-after-merge guarantees).

This work is purely test-side; production code in `CatalogMergeScheduler.cs` must not change.

## Functional Requirements

### FR-1: Test project location and structure
Tests live at `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs`, mirroring the source tree convention used by sibling tests (`CatalogMergeServiceTests.cs`, `CatalogMergeCallbackWiringTests.cs`).

**Acceptance criteria:**
- File exists at the above path.
- Class is named `CatalogMergeSchedulerTests`, public sealed.
- Uses xUnit + FluentAssertions + Moq/NSubstitute consistent with project conventions (see `CatalogMergeServiceTests.cs`).
- Each test method is named by behavior, e.g. `ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay`.
- Tests follow the Arrange/Act/Assert structure.

### FR-2: Debounce — single schedule fires callback exactly once after delay
After a single `ScheduleMerge(...)` call, the registered merge callback runs exactly once after the configured `DebounceDelay` has elapsed.

**Acceptance criteria:**
- Construct the scheduler with `DebounceDelay = 100 ms` (a short interval suitable for a test).
- Call `SetMergeCallback(...)` with a delegate that increments a counter and signals a `TaskCompletionSource`.
- Call `ScheduleMerge("source-a")`.
- Wait for the callback's TCS with a timeout of ≤ 2 seconds.
- Assert callback count == 1.
- Assert `HasPendingMerge()` returns `false` after callback completion.
- Assert `GetLastMergeTime()` is greater than the test start time.

### FR-3: Debounce — burst of rapid invalidations collapses to a single callback
Multiple `ScheduleMerge(...)` calls within the debounce window must result in exactly one callback firing, after the last call + debounce.

**Acceptance criteria:**
- Configure `DebounceDelay = 150 ms`, `MaxMergeInterval = 30 min` (default).
- Issue five `ScheduleMerge(...)` calls in rapid succession (e.g. one every 30 ms) using distinct data-source names.
- Wait `DebounceDelay × 3` (≈ 450 ms) after the last call.
- Assert callback fired exactly once.
- Assert all five sources are observed as invalidated before the merge runs (verifiable by inspecting `_invalidationTimes` indirectly via clearing semantics, or by snapshotting from inside the callback).
- Assert `HasPendingMerge()` returns `false` after the merge.

### FR-4: Max-interval force — callback fires immediately when MaxMergeInterval elapsed
When `timeSinceFirstInvalidation >= MaxMergeInterval`, the scheduler must bypass the debounce timer and execute the merge immediately on a `Task.Run` (no waiting for `DebounceDelay`).

**Acceptance criteria:**
- Configure `MaxMergeInterval = 50 ms`, `DebounceDelay = 10 s` (debounce intentionally long so any wait would clearly exceed the assertion window).
- Call `ScheduleMerge("source-a")` to seed `_firstPendingInvalidation`.
- Wait > 50 ms.
- Call `ScheduleMerge("source-b")`.
- Assert callback fires within ≤ 1 s (well below `DebounceDelay`), confirming the force path executed.
- Assert exactly one callback invocation.
- A log line at `LogInformation` level containing "Force executing merge" is emitted (verified via captured `ILogger` mock).

### FR-5: Semaphore skip — concurrent execute is a no-op
When `ExecuteMergeAsync` runs while a merge is already in progress, the second invocation returns within the 100 ms wait window without invoking the callback again.

**Acceptance criteria:**
- Callback delegate blocks on a `ManualResetEventSlim`/`TaskCompletionSource` controlled by the test, so the first merge holds the semaphore for the duration of the test step.
- Trigger a first `ScheduleMerge(...)` that drives the timer into the callback (using a short `DebounceDelay`).
- Wait until `IsMergeInProgress` returns `true`.
- Trigger a second merge by calling `ScheduleMerge(...)` such that the timer fires while the first is still in flight (e.g. by setting `MaxMergeInterval` to fire immediately).
- Assert callback invocation count stays at 1.
- Release the first callback; assert the merge completes cleanly, `IsMergeInProgress` returns `false`, and a debug log "Merge already in progress, skipping" was emitted.

### FR-6: `WaitForCurrentMergeAsync` — blocks while merge is in progress
`WaitForCurrentMergeAsync` must not return until the in-flight merge finishes; once it finishes, the method must release the semaphore so subsequent calls behave correctly.

**Acceptance criteria:**
- Test 6a (fast path): with no merge in progress, `WaitForCurrentMergeAsync()` completes within ≤ 50 ms.
- Test 6b (blocking path):
  - Start a merge whose callback blocks on a controllable signal.
  - Call `WaitForCurrentMergeAsync()`; the returned task must not be completed while the callback is still blocked (assert via `task.IsCompleted == false` after a short wait, e.g. 100 ms).
  - Release the callback signal; the wait task must complete within ≤ 1 s.
  - After completion, `IsMergeInProgress` returns `false` and a follow-up `WaitForCurrentMergeAsync()` completes immediately (semaphore was released, not leaked).
- Test 6c: after `Dispose`, `WaitForCurrentMergeAsync()` returns immediately without throwing.

### FR-7: Disposed guards — `ScheduleMerge` and `ExecuteMergeAsync` no-op after `Dispose`
Once `Dispose()` has been called, neither `ScheduleMerge` nor a fired timer callback may invoke the merge callback or mutate `_invalidationTimes`.

**Acceptance criteria:**
- Test 7a: call `Dispose()`, then `ScheduleMerge("x")`; assert callback never fires (wait ≥ `DebounceDelay × 2`), and the call returns without throwing.
- Test 7b: schedule a merge with a long debounce, immediately call `Dispose()` before the timer fires; assert callback is never invoked.
- Test 7c: a second `Dispose()` call must be a no-op (no `ObjectDisposedException`).

### FR-8: Application-stopping guard — cancellation token short-circuits scheduling
When the `IHostApplicationLifetime.ApplicationStopping` token has already requested cancellation, `ScheduleMerge` must no-op and `WaitForCurrentMergeAsync` must return immediately.

**Acceptance criteria:**
- Construct with a fake `IHostApplicationLifetime` whose `ApplicationStopping` is already-cancelled.
- Call `ScheduleMerge("x")`; assert callback never fires within ≥ 2 × `DebounceDelay`.
- Call `await WaitForCurrentMergeAsync()`; assert it completes within ≤ 50 ms.

### FR-9: Callback failure does not break the scheduler
If the merge callback throws, the scheduler must log the error, release the semaphore, and remain usable for subsequent schedules.

**Acceptance criteria:**
- First callback delegate throws `InvalidOperationException("boom")`.
- After the failed merge: `IsMergeInProgress` returns `false`, `LogError` was called with the exception, and a subsequent `ScheduleMerge(...)` (with a now-successful callback) produces exactly one successful invocation.

### FR-10: Coverage threshold met
The aggregate of these tests raises `CatalogMergeScheduler` line coverage to ≥ 60% (project threshold). Branch coverage of the previously uncovered branches identified in the brief is at 100%.

**Acceptance criteria:**
- Running `dotnet test` with coverage collection over `CatalogMergeScheduler.cs` reports ≥ 60% line coverage.
- Every line in the disposed-guard, max-interval, semaphore-skip, and `WaitForCurrentMergeAsync` blocking branches is exercised at least once.

## Non-Functional Requirements

### NFR-1: Performance
- Whole test class must complete in ≤ 5 seconds wall-clock on developer laptops.
- No test relies on absolute timing of more than 1 second except the timeouts that bound expected failure (e.g. "callback should NOT fire within 200 ms").
- No `Thread.Sleep` longer than 100 ms; use `Task.Delay` with `CancellationToken`, `TaskCompletionSource`, or `SemaphoreSlim` signals to coordinate.

### NFR-2: Stability and determinism
- No flaky timing assumptions: every "did fire" assertion uses a `TaskCompletionSource` or `ManualResetEventSlim` with an upper-bound timeout — not a fixed-delay sleep.
- Every test disposes the scheduler in `finally`/`using` to avoid leaking timers across runs.
- Tests pass under repeated runs (`dotnet test --filter CatalogMergeSchedulerTests --blame --logger trx`) — target zero failures over 10 consecutive runs.

### NFR-3: Conventions
- xUnit `[Fact]` (or `[Theory]` where parameterization helps), FluentAssertions for assertions, Moq for the `ILogger<CatalogMergeScheduler>` and `IHostApplicationLifetime` fakes, consistent with `CatalogMergeServiceTests.cs`.
- Nullable reference types enabled; no `dynamic`.
- `dotnet format` clean.

### NFR-4: Security
No security-sensitive paths are touched. Tests must not log or persist secrets; the merge callback is a test-only delegate.

## Data Model
No persistent data. Test-internal state:
- Counter: `int callbackInvocations` (interlocked-incremented from the callback).
- Signals: `TaskCompletionSource<bool> callbackFired`, `ManualResetEventSlim releaseCallback`.
- Captured log messages: via Moq `Mock<ILogger<CatalogMergeScheduler>>` with verification of `LogLevel`/message substring.

## API / Interface Design
No production API surface changes. Tests interact only with the public surface of `CatalogMergeScheduler` and its `ICatalogMergeScheduler` interface:
- `SetMergeCallback(Func<CancellationToken, Task>)`
- `ScheduleMerge(string)`
- `IsMergeInProgress`
- `HasPendingMerge()`
- `GetLastMergeTime()`
- `WaitForCurrentMergeAsync(CancellationToken)`
- `Dispose()`

A small test helper `CreateScheduler(CatalogCacheOptions options, IHostApplicationLifetime? lifetime = null)` returns a scheduler with mock `ILogger<CatalogMergeScheduler>` and a default `FakeApplicationLifetime` (an in-test class wrapping `CancellationTokenSource`s).

## Dependencies
- xUnit, FluentAssertions, Moq — already used by sibling tests under `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/`.
- No new NuGet packages.
- No production dependency changes.
- Coverage collection: existing CI run config (`dotnet test` with coverlet) is already in place.

## Out of Scope
- Any change to `CatalogMergeScheduler.cs` production code, including refactoring for testability. If a branch is genuinely unreachable without mutation (it should not be, given a fake `IHostApplicationLifetime`), surface it under **Open Questions**.
- Integration tests via `WebApplicationFactory` or DI container — these are unit tests only.
- Behaviour around `EnableBackgroundMerge`, `AllowStaleDataDuringMerge`, `StaleDataRetentionPeriod`, or `CacheValidityPeriod` — those options affect callers, not the scheduler internals.
- Property-based or stress/concurrency-fuzz tests — value/cost ratio outside this card.
- Coverage uplift for other files even if touched by the same test fixture.

## Open Questions
None.

## Status: COMPLETE