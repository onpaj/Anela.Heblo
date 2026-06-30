# Architecture Review: Unit Test Coverage for CatalogMergeScheduler

## Skip Design: true

Test-only feature. No UI components, no API surface changes, no visual design work.

## Architectural Fit Assessment

This is a pure test-side change that fits cleanly into the existing test architecture:

- **Sibling tests already establish the conventions**: `CatalogMergeServiceTests.cs` and `CatalogMergeCallbackWiringTests.cs` use xUnit + FluentAssertions + Moq, AAA structure, and live under `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/`. The new file lands next to them with zero structural disruption.
- **No new NuGet packages**: `Anela.Heblo.Tests.csproj` already references `Microsoft.Extensions.Hosting.Abstractions` (10.0.3), which is the only non-obvious dependency `CatalogMergeScheduler` needs for an `IHostApplicationLifetime` test double.
- **Public surface is sufficient**: every uncovered branch (debounce, max-interval, semaphore-skip, disposed guard, `WaitForCurrentMergeAsync` blocking path) is reachable through `ICatalogMergeScheduler` plus the `CatalogCacheOptions` knobs (`DebounceDelay`, `MaxMergeInterval`). No production refactor is required.
- **Coverage filter is project-wide (60%)**: hitting the threshold for this single file lifts the line out of CI's coverage-gap report — the closed loop the brief defines.

The integration points are narrow and well-defined: the SUT, two options values, a captured `ILogger` mock, and an in-test `IHostApplicationLifetime`.

## Proposed Architecture

### Component Overview

```
backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/
└── CatalogMergeSchedulerTests.cs            (new)

CatalogMergeSchedulerTests (public sealed)
├── Test helpers (private)
│   ├── CreateScheduler(opts, lifetime?)    → constructs SUT + captures ILogger mock
│   ├── FakeApplicationLifetime              → IHostApplicationLifetime backed by 3x CTS
│   ├── BlockingCallback                     → Func<CT,Task> gated by a TCS the test releases
│   └── CompletionSignal                     → TCS<bool> + interlocked counter
│
└── [Fact] tests, one per acceptance criterion in FR-2..FR-9
        └── using-scope ensures scheduler.Dispose() runs in every path

Dependencies (existing)
├── Microsoft.Extensions.Hosting.Abstractions   IHostApplicationLifetime
├── Moq                                         ILogger<T> capture + verification
└── FluentAssertions                            assertions
```

### Key Design Decisions

#### Decision 1: Hand-written `FakeApplicationLifetime` over Moq
**Options considered:**
- (A) `Mock<IHostApplicationLifetime>` setting up `ApplicationStopping`, `ApplicationStarted`, `ApplicationStopped` getters per test.
- (B) A small internal `FakeApplicationLifetime` class wrapping three `CancellationTokenSource` instances, with `CancelStopping()` / `CancelStopped()` helpers.

**Chosen approach:** (B) — a single private nested helper class inside the test file.

**Rationale:** The scheduler reads `ApplicationStopping` exactly once, in the constructor, into the `_applicationStopping` field. A class-based fake makes the pre-construction cancellation in FR-8 trivial (`new FakeApplicationLifetime(stoppingCancelled: true)`) and avoids per-test Moq setup boilerplate. Moq for `IHostApplicationLifetime` would either require a setup in every test or a base fixture — both worse than 10 lines of plain code.

#### Decision 2: Coordinate every "did/did-not fire" assertion through a `TaskCompletionSource`, never `Thread.Sleep`
**Options considered:**
- (A) Wait fixed multiples of `DebounceDelay` with `Task.Delay` and inspect state.
- (B) Have each test callback signal a `TaskCompletionSource<bool>` on entry and `await` it with an upper-bound timeout (`Task.WhenAny(tcs.Task, Task.Delay(2_000))`).

**Chosen approach:** (B) for all positive assertions ("callback DID fire"). Use a bounded `Task.Delay` only for negative assertions ("callback DID NOT fire within X").

**Rationale:** NFR-2 mandates determinism. Fixed sleeps couple test pass/fail to CI machine speed; a TCS releases as soon as the timer callback actually runs. For negative assertions there is no shortcut — you must wait some bounded window — but the window is sized once (≈ 2×DebounceDelay) and reused, keeping NFR-1 (≤ 5 s total) intact.

#### Decision 3: Force semaphore contention in FR-5 by holding the first callback inside a `TaskCompletionSource`, then trigger the second invocation via the max-interval force path
**Options considered:**
- (A) Two concurrent `ScheduleMerge` calls and hope the timer fires twice while one is in flight.
- (B) Schedule once with a short `DebounceDelay`, wait for `IsMergeInProgress == true`, then `ScheduleMerge` with `MaxMergeInterval` configured so small that the very next call goes straight to `_ = Task.Run(...) ExecuteMergeAsync()`.

**Chosen approach:** (B).

**Rationale:** The semaphore-skip branch is reached only when `ExecuteMergeAsync` is invoked while another instance holds the semaphore. The debounce timer disposes itself on every `ScheduleMerge`, so chaining two timers does not produce contention. The max-interval force path (`Task.Run` fire-and-forget) is the only deterministic way to invoke a second `ExecuteMergeAsync` from public API while the first is still running.

#### Decision 4: Tight `ILogger` verification — substring match on the message, not full-string equality
**Options considered:**
- (A) Verify exact message and all parameter values.
- (B) Verify `LogLevel` and a substring of the log message ("Force executing merge", "Merge already in progress, skipping").

**Chosen approach:** (B), using Moq's `It.Is<It.IsAnyType>` pattern with a predicate.

**Rationale:** Production log strings include interpolated parameters (`{MaxInterval}ms`, `{DataSource}`, `{Delay}ms`). Substring matching keeps tests resilient to harmless wording tweaks while still proving the correct branch executed. The brief's "silent failure modes" concern is about whether the branch ran, not about the exact log text.

#### Decision 5: Test class is `public sealed`, every test wraps the scheduler in `using`
**Options considered:**
- (A) `IClassFixture<>` sharing a single scheduler across tests.
- (B) Per-test scheduler created in the method body, wrapped in `using` (or `try/finally Dispose`).

**Chosen approach:** (B).

**Rationale:** The scheduler is stateful (timer, semaphore, dictionary, disposed flag) and several tests deliberately mutate it to terminal states (disposed, app stopping). A shared fixture would cause order-dependent flakes. Per-test creation + `using` matches NFR-2 ("Every test disposes the scheduler in `finally`/`using`").

## Implementation Guidance

### Directory / Module Structure

```
backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/
└── CatalogMergeSchedulerTests.cs    ← only new file
```

No new directories. No production-side files touched. The `Anela.Heblo.Tests.csproj` `<Folder Include="Infrastructure\" />` entry can stay as-is.

### Interfaces and Contracts

The test file consumes only existing public surface:

```csharp
// From production (unchanged)
ICatalogMergeScheduler {
    bool IsMergeInProgress { get; }
    void SetMergeCallback(Func<CancellationToken, Task>);
    void ScheduleMerge(string);
    DateTime GetLastMergeTime();
    bool HasPendingMerge();
    Task WaitForCurrentMergeAsync(CancellationToken = default);
    void Dispose();
}

// From production (unchanged)
class CatalogCacheOptions {
    TimeSpan DebounceDelay     { get; set; }
    TimeSpan MaxMergeInterval  { get; set; }
    // other properties are not relevant to scheduler internals
}

// Test-private helper contract
private sealed class FakeApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _started   = new();
    private readonly CancellationTokenSource _stopping  = new();
    private readonly CancellationTokenSource _stopped   = new();

    public FakeApplicationLifetime(bool stoppingCancelled = false)
    {
        if (stoppingCancelled) _stopping.Cancel();
    }

    public CancellationToken ApplicationStarted  => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped  => _stopped.Token;

    public void StopApplication() => _stopping.Cancel();
}

// Test-private factory
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

ILogger verification pattern (reuse across FR-4, FR-5, FR-9):

```csharp
logger.Verify(l => l.Log(
    expectedLevel,
    It.IsAny<EventId>(),
    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(expectedSubstring)),
    It.IsAny<Exception?>(),
    It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
    Times.AtLeastOnce);
```

### Data Flow

**FR-2/FR-3 (debounce single + burst):**
```
Test → ScheduleMerge(s) → timer arms → DebounceDelay elapses → ExecuteMergeAsync
     → semaphore.WaitAsync(100) (succeeds) → callback(CT) → callback signals TCS
     → Test awaits TCS with timeout → asserts counter == 1, HasPendingMerge() == false
     → using-scope Dispose()
```

**FR-4 (max-interval force):**
```
Test → ScheduleMerge("a") seeds _firstPendingInvalidation
     → Task.Delay(> MaxMergeInterval)
     → ScheduleMerge("b") → timeSinceFirstInvalidation >= MaxMergeInterval
     → Task.Run(ExecuteMergeAsync) → callback fires → TCS released
     → Test asserts logger logged "Force executing merge" at LogInformation
```

**FR-5 (semaphore skip):**
```
Test → ScheduleMerge("a") with short DebounceDelay → timer → ExecuteMergeAsync
     → semaphore acquired → callback awaits gating TCS (test holds it)
     → Test polls IsMergeInProgress until true
     → Test ScheduleMerge("b") with MaxMergeInterval=0
     → Task.Run(ExecuteMergeAsync) → semaphore.WaitAsync(100) returns false
     → "Merge already in progress, skipping" logged at Debug
     → Test releases gating TCS → first merge completes → semaphore released
     → Test asserts callback counter == 1
```

**FR-6b (`WaitForCurrentMergeAsync` blocking):**
```
Test → ScheduleMerge → callback enters and blocks on gating TCS (semaphore held)
     → Test polls IsMergeInProgress until true
     → var waitTask = sut.WaitForCurrentMergeAsync()
     → assert !waitTask.IsCompleted after Task.Delay(100)
     → release gating TCS → callback returns → finally releases semaphore
     → waitTask completes within 1 s → assert IsMergeInProgress == false
     → second WaitForCurrentMergeAsync() completes within 50 ms (semaphore not leaked)
```

**FR-7 (disposed guards):**
```
7a: Dispose() → ScheduleMerge("x") → _disposed check returns early → no callback, no throw
7b: ScheduleMerge with long debounce → Dispose() before timer fires
    → timer's ExecuteMergeAsync runs → _disposed check returns early
    → assert callback counter == 0 after waiting 2 × DebounceDelay
7c: Dispose(); Dispose() → second call returns early on _disposed check → no throw
```

**FR-8 (application stopping):**
```
Test → new FakeApplicationLifetime(stoppingCancelled: true)
     → CreateScheduler(opts, fakeLifetime) → _applicationStopping is pre-cancelled
     → ScheduleMerge("x") → IsCancellationRequested check returns early
     → WaitForCurrentMergeAsync() → IsCancellationRequested check returns immediately
```

**FR-9 (callback failure recovery):**
```
Test → first callback throws → catch in ExecuteMergeAsync → LogError captured
     → finally releases semaphore → assert IsMergeInProgress == false
     → SetMergeCallback(successfulDelegate) → ScheduleMerge → second callback runs once
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-5 timing flake: the 100 ms `WaitAsync` window in `ExecuteMergeAsync` can succeed if the first merge releases the semaphore unexpectedly fast | HIGH | Gate the first callback on a TCS the test controls; poll `IsMergeInProgress` until true *before* triggering the second `ScheduleMerge`. Do not rely on `await Task.Delay(small)` for ordering. |
| FR-4 timing flake: `Task.Run` fire-and-forget gives no completion handle | MEDIUM | Always coordinate via a TCS signalled from inside the callback; `await Task.WhenAny(tcs.Task, Task.Delay(1_000))` and assert `tcs.Task.IsCompleted`. |
| Negative-assertion tests (FR-7a, FR-8) are inherently slow (must wait through `DebounceDelay × 2`) — risk to NFR-1 ≤ 5 s total | MEDIUM | Use small `DebounceDelay` (100 ms) for these tests so 2× wait stays at 200 ms. Aggregate negative-wait time across the file < 1 s. |
| `IsMergeInProgress` reads `_mergeSemaphore.CurrentCount` **without** checking `_disposed` — calling it post-Dispose throws `ObjectDisposedException` | LOW (test-side only) | Never call `IsMergeInProgress` after `Dispose()` in tests. Note as a production hazard in **Specification Amendments** but do not modify production. |
| Timer callback exception swallowing: the production `Timer(async _ => await ExecuteMergeAsync(), ...)` is effectively `async void` — exceptions inside `ExecuteMergeAsync` (other than those it already catches) would be lost | LOW | Production already wraps callback execution in try/catch; tests should not rely on observing thrown exceptions, only on logger captures and post-state. |
| `_invalidationTimes` is private — FR-3 cannot directly assert "all five sources observed" without reflection | LOW | Spec already accepts "verifiable via clearing semantics" — assert that exactly one callback fired with all five `ScheduleMerge` calls completed before the wait window expired. Do not use reflection. |
| Coverage threshold drift: another file's regression could keep CI red even after this work hits ≥ 60% locally | LOW | Verify coverage on `CatalogMergeScheduler.cs` specifically with `--collect:"XPlat Code Coverage"` and inspect the Cobertura XML for that file. Do not gate this PR on aggregate coverage. |
| Concurrent tests with shared static state | LOW | xUnit runs tests in the same class sequentially by default — no extra `Collection` attribute needed. |

## Specification Amendments

1. **Add explicit constraint: `IsMergeInProgress` must not be read after `Dispose()` in tests.** The production property accesses `_mergeSemaphore.CurrentCount` without a `_disposed` guard and will throw `ObjectDisposedException`. The spec's FR-7 and FR-6c already avoid this by construction, but a code comment in the test helper should call it out so future test authors do not stumble.

2. **Clarify FR-6c — `WaitForCurrentMergeAsync` after `Dispose()`:** the early return path is `if (_disposed ... ) return;`, which works correctly. However, if a merge is *in flight* at the moment of `Dispose()`, the production code disposes `_mergeSemaphore` while the callback may still try to release it in `finally` (guarded by `if (!_disposed)`). The test should **not** exercise mid-merge disposal — FR-7 only requires pre-merge and post-merge disposal scenarios. Add a sentence to FR-7 acknowledging this is out of scope.

3. **FR-9 logger verification:** explicitly state that the captured exception must be `InvalidOperationException` with message "boom" (matches Moq verification pattern: `It.Is<Exception>(e => e.Message == "boom")`).

4. **FR-10:** clarify that the 60% threshold check is on the *line coverage of `CatalogMergeScheduler.cs` alone* (the brief's scope), not on the aggregate project coverage — the latter can shift independently and is not the contract of this card.

No structural changes to the spec are required. The 10 FRs and 4 NFRs are sufficient and consistent.

## Prerequisites

None. All required dependencies are in place:

- Production code (`CatalogMergeScheduler.cs`, `ICatalogMergeScheduler.cs`, `CatalogCacheOptions.cs`) is stable on `main` and on this branch.
- Test project (`Anela.Heblo.Tests.csproj`) already references xUnit 2.9.2, FluentAssertions 6.12, Moq 4.20.72, and `Microsoft.Extensions.Hosting.Abstractions` 10.0.3.
- Sibling test conventions are codified in `CatalogMergeServiceTests.cs` and `CatalogMergeCallbackWiringTests.cs` — read these first to match style.
- Coverlet collector (6.0.2) is wired into the test project; CI's `dotnet test` invocation already collects coverage. No CI changes required.

Implementation can start immediately.