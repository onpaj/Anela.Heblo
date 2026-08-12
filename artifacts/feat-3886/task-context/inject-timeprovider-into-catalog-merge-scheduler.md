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
