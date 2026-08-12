# Architecture Review: Inject `TimeProvider` into `CatalogMergeScheduler`

## Skip Design: true

Backend-only refactor. No new or changed UI components, screens, layouts, DTOs, endpoints, or visual decisions. The OpenAPI surface is untouched, so no TypeScript client regeneration and no frontend work.

## Architectural Fit Assessment

### What I verified in the codebase

| Claim | Verified against |
|-------|------------------|
| `TimeProvider` injection is the house convention | 26 constructor injections across `Features/Catalog/` alone; `CatalogMergeService.cs:21,26`, `CatalogCacheStore.cs:46,55`, `CatalogDataRefreshService.cs:40,62` are the three direct siblings in the same folder |
| `TimeProvider` is registered as a singleton in the running container | `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` (`services.AddSingleton(TimeProvider.System)`), invoked from `Program.cs:109` (`builder.Services.AddCrossCuttingServices()`) |
| `CatalogMergeScheduler` is registered as a singleton | `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:101` — `services.AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>()` |
| Only one hand-construction site exists | `grep -rn "new CatalogMergeScheduler" backend/` → `CatalogMergeSchedulerTests.cs:35` only |
| `FakeTimeProvider` is already a first-class test tool here | `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`; used in 11 test files, e.g. `TimePeriodResolverTests.cs:3,10`, `ManufactureErpResilienceServiceTests.cs:7,26,30` |
| `TimeProvider.CreateTimer` has **no** in-repo precedent | `grep -rn "CreateTimer\|ITimer" backend/src --include=*.cs` → zero hits. This change introduces the first one. |
| `GetLastMergeTime()` / `HasPendingMerge()` have **no** production consumers | `grep -rn "GetLastMergeTime\|HasPendingMerge" backend/src` → only the interface and the implementation. Production only calls `ScheduleMerge` (`CatalogCacheStore.cs:81,95,385`) and `WaitForCurrentMergeAsync`. |

### Fit

Excellent. The lifetimes line up without any DI change: `TimeProvider.System` is a singleton and `CatalogMergeScheduler` is a singleton, so there is no captive-dependency problem. The change is additive on the constructor and invisible to `ICatalogMergeScheduler`. Integration points are two: the constructor (DI resolves the new parameter automatically) and the test file.

The one genuinely new thing is `TimeProvider.CreateTimer` / `ITimer`. Both are .NET 8 BCL types in `System`, available with the project's `ImplicitUsings` — no package reference, no `using` directive needed in `CatalogMergeScheduler.cs`.

### Project-rule compliance

- **CLAUDE.md "Surgical changes"** — this review deliberately keeps the blast radius to one production file plus its test. It explicitly rejects the tempting adjacent cleanups (`Stopwatch` → `GetTimestamp`, `Task.Run` → dispatch abstraction, `WaitAsync(100)` → option). Those are named in the spec's Out of Scope and stay there.
- **CLAUDE.md "DTOs are classes, never records"** — not applicable; no DTOs.
- **`docs/architecture/testing-strategy.md`** — unit-test layer, xUnit + Moq + FluentAssertions. `FakeTimeProvider` fits the existing stack; no new test technology.

## Proposed Architecture

### Component Overview

```
                         ┌──────────────────────────────┐
                         │  ServiceCollectionExtensions │
                         │  AddCrossCuttingServices()   │
                         │  AddSingleton(TimeProvider.  │
                         │              System)         │
                         └──────────────┬───────────────┘
                                        │ TimeProvider (singleton)
                                        │
        ┌───────────────────────────────┼───────────────────────────────┐
        │                               │                               │
        ▼                               ▼                               ▼
┌────────────────┐            ┌──────────────────┐            ┌─────────────────────┐
│CatalogCacheStore│           │CatalogMergeService│           │CatalogMergeScheduler│
│  (already OK)   │           │   (already OK)    │           │   ◄── THIS CHANGE   │
└────────┬───────┘            └──────────────────┘            └──────────┬──────────┘
         │ ScheduleMerge(source)                                          │
         └────────────────────────────────────────────────────────────────┘
                                                                          │
                                       ┌──────────────────────────────────┴─────────┐
                                       │ uses TimeProvider for BOTH:                │
                                       │  (a) GetUtcNow()  → _firstPendingInvalidation,
                                       │                     _lastMergeCompleted    │
                                       │  (b) CreateTimer() → ITimer? _debounceTimer│
                                       └────────────────────────────────────────────┘

Test wiring (CatalogMergeSchedulerTests):
   FakeTimeProvider ──► CatalogMergeScheduler
        │
        └─ Advance(DebounceDelay) fires the ITimer callback synchronously,
           on the advancing thread, before Advance() returns.
```

### Key Design Decisions

#### Decision 1: `GetUtcNow().UtcDateTime`, not `GetUtcNow().DateTime`

**Options considered:**
- (a) `_timeProvider.GetUtcNow().UtcDateTime` — `DateTimeKind.Utc`
- (b) `_timeProvider.GetUtcNow().DateTime` — `DateTimeKind.Unspecified`
- (c) Change the fields to `DateTimeOffset` and widen `ICatalogMergeScheduler.GetLastMergeTime()`

**Chosen approach:** (a) `_timeProvider.GetUtcNow().UtcDateTime`.

**Rationale:** The code being replaced is `DateTime.UtcNow`, which yields `Kind == DateTimeKind.Utc`. Only `.UtcDateTime` preserves that; `.DateTime` silently flips the `Kind` to `Unspecified`. The repo uses both idioms (57 `.DateTime`, 23 `.UtcDateTime`), so neither is "the" convention — the tiebreak is behaviour preservation, which is the whole point of a refactor. `.UtcDateTime` is also what the nearest analogue does when converting a former `DateTime.UtcNow`: `CatalogMergeService.cs:281` and `CatalogDataRefreshService.cs:232`.

(c) is rejected as a public-contract change that the arch-review issue did not ask for and that would ripple into `ICatalogMergeScheduler`.

The practical risk of the `Kind` difference is low here — `GetLastMergeTime()` has no production consumers — but "low risk" is not a reason to introduce an unnecessary difference.

#### Decision 2: Keep re-creating the timer; do not switch to `ITimer.Change`

**Options considered:**
- (a) Keep the existing `Dispose()`-then-create pattern, swapping only the factory: `_debounceTimer?.Dispose(); _debounceTimer = _timeProvider.CreateTimer(...)`
- (b) Create the `ITimer` once and call `Change(DebounceDelay, Timeout.InfiniteTimeSpan)` to reset the debounce

**Chosen approach:** (a).

**Rationale:** (b) is arguably better engineering — fewer allocations, no dispose race — but it is a behavioural rewrite of the debounce mechanism, not a `TimeProvider` migration. CLAUDE.md is explicit: *"Touch only what the task requires... Every changed line should trace directly to the request."* The arch-review finding is "doesn't use the injected `TimeProvider`", not "allocates a timer per invalidation." Keeping (a) means the diff for FR-3 is two lines (field type + factory call) and reviewers can see at a glance that scheduling semantics are unchanged.

Note it as a follow-up candidate; do not do it here.

#### Decision 3: `ITimer?` for the field, not `var`/`IDisposable`

`TimeProvider.CreateTimer` returns `ITimer` (which extends `IDisposable`). Declaring the field as `ITimer?` keeps the option of a later `Change()`-based refactor open and documents intent. `IDisposable?` would compile but throws away type information for no gain.

Both existing call sites — `_debounceTimer?.Dispose()` at line 73 and the `Dispose()` block at lines 148-152 — compile unchanged against `ITimer?`.

#### Decision 4: Constructor parameter goes last

**Chosen approach:**

```csharp
public CatalogMergeScheduler(
    ILogger<CatalogMergeScheduler> logger,
    IOptions<CatalogCacheOptions> options,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider)
```

**Rationale:** The siblings do not agree on a position (`CatalogMergeService` puts it second, `CatalogCacheStore` second, `CatalogDataRefreshService` mid-list), so there is no positional convention to honour. Appending keeps the existing three arguments in their current order, which makes the test-file diff a one-line addition rather than a re-shuffle. It is a required, non-defaulted parameter — **do not** give it `= null` with a `?? TimeProvider.System` fallback; that is a well-known way for a class to silently keep using the real clock in a test that forgot to pass a fake, and none of the siblings do it.

#### Decision 5: Null-guard style follows the siblings

`CatalogMergeService` and `CatalogCacheStore` guard with `?? throw new ArgumentNullException(nameof(x))`; `CatalogMergeScheduler`'s existing constructor does **not** guard any of its three parameters. Assign `_timeProvider = timeProvider;` plainly, matching the file it lives in rather than the folder. Adding a guard for the new parameter while `logger` and `options` stay unguarded would be inconsistent within the file, and adding guards to all four is scope creep.

#### Decision 6: `FakeTimeProvider.Advance` triggers, a `TaskCompletionSource` confirms

**The mechanism, precisely:** `FakeTimeProvider.Advance(delta)` walks its registered waiters and invokes every callback whose due time has passed, **synchronously, on the calling thread, before `Advance` returns**. The scheduler's callback is `async _ => await ExecuteMergeAsync()` — an async lambda. It runs synchronously only as far as its first *incomplete* await. In the uncontended case `_mergeSemaphore.WaitAsync(100)` returns an already-completed task and a `Task.CompletedTask` merge callback also completes synchronously, so the whole merge typically finishes inside `Advance`. **This is a happy accident, not a guarantee** — a merge callback that yields (any real `async` work) will return control to `Advance` mid-flight.

**Therefore the test rule is:** use `Advance` to *trigger* the timer, then `await` a `TaskCompletionSource` signalled from inside the merge callback (with a generous, failure-only timeout) to *observe* completion. Never assert immediately after `Advance` on state that `ExecuteMergeAsync` writes after an await. Never use `Task.Delay` as the synchronisation primitive.

For the negative assertions ("no second callback fired"), the shape becomes: advance well past the delay, then assert the counter — no sleep needed at all, because `Advance` is synchronous and there is nothing left pending.

#### Decision 7: Two paths stay real-time, and the tests must acknowledge it

`TimeProvider` does not make this class fully deterministic. Two paths remain:

1. **Max-interval force path**, `CatalogMergeScheduler.cs:68`: `_ = Task.Run(async () => await ExecuteMergeAsync(), _applicationStopping)`. Thread-pool dispatch; `Advance` cannot make it observable. The test must await a TCS.
2. **Contended semaphore**, `CatalogMergeScheduler.cs:88`: `await _mergeSemaphore.WaitAsync(100)`. When a merge is genuinely in flight, this is a real 100 ms wait. `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation` will still pay it.

Both are named in the spec (NFR-3) as out of scope. The residual real-time cost after this change is roughly 100 ms in one test, versus the ~1.5-2.5 s of unconditional sleeping today.

#### Decision 8: `FakeTimeProvider`'s epoch breaks one existing assertion — fix it, don't work around it

`FakeTimeProvider`'s default start is `2000-01-01T00:00:00Z`. `ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay` currently asserts `sut.GetLastMergeTime().Should().BeAfter(testStart)` where `testStart = DateTime.UtcNow` (2026). Under a default `FakeTimeProvider` that assertion **fails**.

The fix is to assert the exact expected fake instant, which is strictly stronger than `BeAfter`:

```csharp
var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
var fakeTime = new FakeTimeProvider(start);
...
sut.GetLastMergeTime().Should().Be(start.Add(opts.DebounceDelay).UtcDateTime);
```

Do **not** paper over it by seeding `FakeTimeProvider` with `DateTimeOffset.UtcNow` — that reintroduces wall-clock dependence into the assertion.

#### Decision 9: Raise the test delays to production-like values

Today's tests use `DebounceDelay = 100 ms` / `MaxMergeInterval = 50 ms` purely to keep real sleeps short. Once time is faked, those values cost nothing, so use values near the production defaults from `CatalogCacheOptions` (`DebounceDelay = 5 s`, `MaxMergeInterval = 30 min`). This makes the tests exercise the configuration the app actually ships and removes the "is 100 ms enough on a loaded CI box?" class of flake entirely.

The one exception is `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation`, which needs `MaxMergeInterval` small *relative to the fake clock advance* to reach the force path — express that by advancing the fake clock past it, not by shrinking the option to 1 ms.

## Implementation Guidance

### Directory / Module Structure

No new files, no new directories, no moved files.

| File | Action |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs` | Modify — lines 18, 26-34, 47, 74-75, 102 |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` | Modify — `CreateScheduler` helper + all 12 tests |
| `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` | **No change** — DI resolves the new parameter |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ICatalogMergeScheduler.cs` | **No change** |

### Interfaces and Contracts

The exact production diff, in full:

```csharp
// line 18
-    private Timer? _debounceTimer;
+    private ITimer? _debounceTimer;

// lines 26-34
     public CatalogMergeScheduler(
         ILogger<CatalogMergeScheduler> logger,
         IOptions<CatalogCacheOptions> options,
-        IHostApplicationLifetime applicationLifetime)
+        IHostApplicationLifetime applicationLifetime,
+        TimeProvider timeProvider)
     {
         _logger = logger;
         _options = options.Value;
         _applicationStopping = applicationLifetime.ApplicationStopping;
+        _timeProvider = timeProvider;
     }

// new field, alongside the other readonly fields (lines 10-12)
+    private readonly TimeProvider _timeProvider;

// line 47, in ScheduleMerge
-        var now = DateTime.UtcNow;
+        var now = _timeProvider.GetUtcNow().UtcDateTime;

// lines 74-75, in ScheduleMerge
-            _debounceTimer = new Timer(async _ => await ExecuteMergeAsync(),
-                null, _options.DebounceDelay, Timeout.InfiniteTimeSpan);
+            _debounceTimer = _timeProvider.CreateTimer(async _ => await ExecuteMergeAsync(),
+                null, _options.DebounceDelay, Timeout.InfiniteTimeSpan);

// line 102, in ExecuteMergeAsync
-            _lastMergeCompleted = DateTime.UtcNow;
+            _lastMergeCompleted = _timeProvider.GetUtcNow().UtcDateTime;
```

Everything else in the file — the `SemaphoreSlim`, `ConcurrentDictionary`, `_timerLock`, `Stopwatch`, `Task.Run` force path, `Dispose()`, all log statements — is untouched.

`ITimer` and `TimeProvider` resolve from `System` under `ImplicitUsings`; no new `using` directive is required. `System.Threading.Timer` was likewise resolved implicitly, so removing the last `new Timer(...)` leaves no orphan `using`.

### Data Flow

```
CatalogCacheStore.InvalidateSourceData / GetCatalogData
        │  ScheduleMerge("Erp")
        ▼
CatalogMergeScheduler.ScheduleMerge
        │
        ├─ now = _timeProvider.GetUtcNow().UtcDateTime          ← FR-2
        ├─ _invalidationTimes.TryAdd(source, now)
        │
        └─ lock (_timerLock)
             ├─ seed _firstPendingInvalidation (if MinValue)
             ├─ if (now - _firstPendingInvalidation >= MaxMergeInterval)
             │     └─ Task.Run(ExecuteMergeAsync)               ← still thread-pool (out of scope)
             └─ else
                   ├─ _debounceTimer?.Dispose()
                   └─ _debounceTimer = _timeProvider.CreateTimer(...) ← FR-3
                                            │
                     (real clock in prod / FakeTimeProvider.Advance in tests)
                                            ▼
                              ExecuteMergeAsync
                                   ├─ await _mergeSemaphore.WaitAsync(100)  ← still real (out of scope)
                                   ├─ await _mergeCallback(_applicationStopping)
                                   │       └─ CatalogMergeService.ExecuteBackgroundMergeAsync
                                   ├─ _lastMergeCompleted = _timeProvider.GetUtcNow().UtcDateTime  ← FR-2
                                   ├─ _mergeScheduled = false
                                   └─ _firstPendingInvalidation = DateTime.MinValue
```

Production behaviour under `TimeProvider.System` is byte-for-byte equivalent to today: `TimeProvider.System.GetUtcNow()` reads the same underlying clock as `DateTime.UtcNow`, and `TimeProvider.System.CreateTimer` wraps `System.Threading.Timer`.

### Test structure guidance

`CreateScheduler` becomes:

```csharp
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

with a class-level `private static readonly DateTimeOffset TestStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);`, mirroring `TimePeriodResolverTests.cs:9`. `FakeApplicationLifetime` is kept exactly as-is.

The 12 test cases map to fake-time drivers as follows:

| Test | Real-time cost today | Fake-time driver |
|------|---------------------|------------------|
| `ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay` | up to 2000 + 50 ms | `Advance(DebounceDelay)`; assert `GetLastMergeTime() == TestStart + DebounceDelay` (Decision 8) |
| `ScheduleMerge_BurstOfCalls_CollapseToSingleCallback` | 5×30 + 2000 + 450 ms | 5 × (`ScheduleMerge` + `Advance(DebounceDelay/5)`), then `Advance(DebounceDelay)`; assert exactly 1 |
| `ScheduleMerge_BeyondMaxMergeInterval_ForcesImmediateExecution` | 60 + 1000 + 50 ms | `ScheduleMerge`, `Advance(MaxMergeInterval + 1s)`, `ScheduleMerge` → force path; await TCS (Decision 7) |
| `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation` | poll + 200 + 100 ms | `Advance` to fire first merge, gate it on a TCS, `Advance` past `MaxMergeInterval`, `ScheduleMerge` → force path; **still pays ~100 ms** on `WaitAsync(100)` |
| `WaitForCurrentMergeAsync_WhenNoMergeInProgress_CompletesImmediately` | ~0 | unchanged; drop the `Stopwatch < 50 ms` assertion in favour of `Task.IsCompleted` |
| `WaitForCurrentMergeAsync_WhenMergeInProgress_BlocksUntilComplete` | poll + 100 + 1000 ms | `Advance` to start the merge, gate on TCS, assert `waitTask.IsCompleted == false`, release, await |
| `WaitForCurrentMergeAsync_AfterDispose_ReturnsImmediately` | ~0 | unchanged |
| `ScheduleMerge_AfterDispose_DoesNotFireCallback` | 300 ms | `Dispose()`, `ScheduleMerge`, `Advance(2 × DebounceDelay)`, assert 0 |
| `ScheduleMerge_DisposedBeforeTimerFires_DoesNotFireCallback` | 500 ms | `ScheduleMerge`, `Dispose()`, `Advance(2 × DebounceDelay)`, assert 0 |
| `Dispose_CalledTwice_DoesNotThrow` | 0 | unchanged |
| `ScheduleMerge_WhenApplicationStopping_DoesNotFireCallback` | 300 ms | `Advance(2 × DebounceDelay)`, assert 0 |
| `ScheduleMerge_WhenCallbackThrows_SchedulerRemainsUsable` | 2×2000 + 2×50 ms | `Advance(DebounceDelay)` twice with a TCS per attempt; `VerifyLogged` assertions unchanged |

The three `VerifyLogged` / `logger.Verify` assertions (lines 161, 200, 427-433) must survive verbatim — they are the NFR-2 behaviour-preservation guard.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `FakeTimeProvider.Advance` fires the async timer callback but a test asserts before the continuation runs, producing a flaky pass/fail | **High** — this is the single most likely way to get this wrong | Decision 6: `Advance` triggers, a `TaskCompletionSource` signalled from inside the merge callback confirms. Never assert post-await state straight after `Advance`. |
| `GetLastMergeTime()` assertion at line 87 silently breaks on `FakeTimeProvider`'s year-2000 epoch | Medium — a hard, obvious test failure, not a silent one | Decision 8: assert the exact fake instant. Caught immediately by `dotnet test`. |
| `.DateTime` used instead of `.UtcDateTime`, flipping `DateTimeKind` from `Utc` to `Unspecified` | Low — no production consumer of `GetLastMergeTime()` | Decision 1 mandates `.UtcDateTime`. Verifiable by grep in review. |
| First use of `TimeProvider.CreateTimer` in the repo — no precedent to copy, so a subtle disposal or lifetime mistake has no local reference implementation | Medium | The change is a like-for-like factory swap; `ITimer : IDisposable` means both existing `Dispose()` call sites (lines 73, 150) compile and behave unchanged. The `ScheduleMerge_DisposedBeforeTimerFires_DoesNotFireCallback` and `Dispose_CalledTwice_DoesNotThrow` tests cover disposal directly. |
| Missing `TimeProvider` DI registration on some other host that builds this module (e.g. a test host or a worker) | Low | Verified `Program.cs:109` calls `AddCrossCuttingServices()`. `CatalogModule.cs:101` needs no change. If any `WebApplicationFactory`-based test builds a partial container, `dotnet test` surfaces it as a resolution failure at startup. |
| Scope creep into `Task.Run`, `WaitAsync(100)`, or `Stopwatch` | Medium — all three are visible in the same file and look "wrong" for the same reason | Spec Out of Scope + Decision 7. Reviewers should reject a diff that touches lines 68, 88, or 94/108/113/121. |
| Behaviour change smuggled in via a "better" timer implementation (`Change()` instead of re-create) | Medium | Decision 2. The FR-3 diff must be exactly two lines. |
| `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation` still real-time and still the flakiest test | Low | Accepted and documented (NFR-3, Decision 7). Its 100 ms cost is inherent to `_mergeSemaphore.WaitAsync(100)`, which is out of scope. |

## Specification Amendments

1. **FR-1 acceptance criterion, parameter ordering.** The spec says ordering "follows the sibling convention in this folder." There is no such convention — the three siblings place `TimeProvider` in three different positions. Amend to: **append `TimeProvider timeProvider` as the last constructor parameter**, required and non-defaulted (Decision 4).

2. **FR-1, add a null-guard clarification.** The spec is silent. Amend: assign plainly (`_timeProvider = timeProvider;`) without an `ArgumentNullException` guard, matching the existing constructor in the same file, which guards nothing (Decision 5).

3. **FR-2, pin the conversion.** The spec says `_timeProvider.GetUtcNow().UtcDateTime`, which is correct — this review confirms it and adds the rationale (Decision 1). Add an acceptance criterion: **`GetLastMergeTime()` must still return a `DateTime` with `Kind == DateTimeKind.Utc`.**

4. **FR-5, add the epoch fix explicitly.** Add an acceptance criterion: the `FakeTimeProvider` is seeded with an explicit fixed `DateTimeOffset` (not `DateTimeOffset.UtcNow`), and `ScheduleMerge_FiresCallbackOnce_AfterDebounceDelay` asserts the exact expected instant rather than `BeAfter` (Decision 8).

5. **FR-5, add the synchronisation rule as a criterion.** Add: **every test that observes a merge completing must await a `TaskCompletionSource` signalled from inside the merge callback; `Task.Delay` may not be used to wait for scheduler-driven work.** (Decision 6.)

6. **FR-5, option values.** Add: tests use production-like `CatalogCacheOptions` values (`DebounceDelay ≈ 5 s`, `MaxMergeInterval ≈ 30 min`) and drive them with `Advance`, rather than the artificially tiny values used today to keep real sleeps short (Decision 9).

7. **NFR-1, make the target measurable and honest.** "Well under 1 s" is right in spirit, but ~100 ms of it is structurally unavoidable (`_mergeSemaphore.WaitAsync(100)` in one test). Amend the target to: **the class contains no unconditional `Task.Delay` used as a wait; residual real-time cost is confined to the single contended-semaphore test.**

8. **Scope confirmation for FR-4.** The spec says `CatalogModule.cs:101` requires no change. Confirmed by inspection — `AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>()` uses constructor injection, and `TimeProvider` is registered as a singleton. No amendment; recorded so a developer does not "helpfully" add a factory lambda.

## Prerequisites

None. Specifically:

- No NuGet package additions — `TimeProvider`/`ITimer` are BCL (.NET 8), `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 is already referenced.
- No DI registration changes — `TimeProvider.System` is already a singleton (`ServiceCollectionExtensions.cs:130`).
- No database migration, no configuration key, no Key Vault secret, no environment variable.
- No OpenAPI/TypeScript client regeneration — the HTTP surface is unchanged.
- No E2E test changes.

Validation gate before completion (per CLAUDE.md): `dotnet build`, `dotnet format`, and `dotnet test --filter FullyQualifiedName~CatalogMergeSchedulerTests` all green. The wider catalog test suite should also be run, since `CatalogCacheStore` calls `ScheduleMerge` — `dotnet test --filter FullyQualifiedName~Features.Catalog`.
