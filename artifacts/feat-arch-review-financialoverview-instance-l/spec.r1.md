# Specification: Remove misleading instance-level lock in FinancialAnalysisService

## Summary
Eliminate the misleading `_refreshLock` instance lock inside `FinancialAnalysisService.RefreshFinancialDataAsync` and rely solely on the shared `IMemoryCache` timestamp throttle. The lock currently provides zero cross-request protection because the service is registered Scoped (a new instance per HTTP request / per DI scope), so the only readers it ever guards are the single thread holding the request. Removing it eliminates the false sense of concurrency safety without changing observable behavior.

## Background
`FinancialAnalysisService` is registered as **Scoped** in `FinancialOverviewModule.cs:28`:

```csharp
services.AddScoped<IFinancialAnalysisService, FinancialAnalysisService>();
```

It also declares an instance-level lock object at `FinancialAnalysisService.cs:22`:

```csharp
private readonly object _refreshLock = new();
```

The lock guards a 10-minute throttle check in `RefreshFinancialDataAsync` (`FinancialAnalysisService.cs:109-117`):

```csharp
lock (_refreshLock)
{
    var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;
    if (DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(10))
    {
        _logger.LogDebug("Skipping refresh, last refresh was too recent");
        return;
    }
}
```

Because each HTTP request and each `IServiceProvider.CreateScope()` (e.g. `BackgroundRefreshSchedulerService.RunTaskLoop`, `BackgroundRefreshSchedulerService.cs:86-88`) gets a brand-new `FinancialAnalysisService` instance, the lock is created fresh and never contended. Two concurrent callers — for example the periodic background refresh scheduler and a user-triggered cache-miss path — each see their own `_refreshLock` and both pass through. The only actual cross-request guard is the `IMemoryCache` read of `LAST_REFRESH_CACHE_KEY`, which is thread-safe on its own.

The fix is to remove the lock so the code accurately reflects its actual concurrency guarantees. Promoting the service to Singleton (Option B from the brief) is rejected because `IStockValueService` is registered Scoped (`FinancialOverviewModule.cs:19`) and cannot be injected into a Singleton without introducing a captive-dependency bug or refactoring scope handling — out of scope for this surgical fix.

## Functional Requirements

### FR-1: Remove the `_refreshLock` field
Delete the `private readonly object _refreshLock = new();` declaration at `FinancialAnalysisService.cs:22`.

**Acceptance criteria:**
- The `_refreshLock` field no longer exists in `FinancialAnalysisService.cs`.
- No other file references `_refreshLock` (verified by repo-wide search).
- The project builds with `dotnet build` with zero warnings introduced by the change.

### FR-2: Remove the `lock` statement and keep the throttle check
Replace the `lock (_refreshLock) { ... }` block in `RefreshFinancialDataAsync` (`FinancialAnalysisService.cs:109-117`) with an unlocked equivalent that preserves the early-return behavior:

```csharp
var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;
if (DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(10))
{
    _logger.LogDebug("Skipping refresh, last refresh was too recent");
    return;
}
```

**Acceptance criteria:**
- No `lock` keyword remains in `FinancialAnalysisService.cs`.
- The throttle still skips refreshes when the last refresh was less than 10 minutes ago.
- The early-return path still emits the same `LogDebug("Skipping refresh, last refresh was too recent")` message at the same log level.
- The successful-refresh path still updates `LAST_REFRESH_CACHE_KEY` to `DateTime.UtcNow` with a 24-hour expiration (line 143 unchanged).

### FR-3: Preserve existing behavior for all other code paths
No changes to `GetFinancialOverviewAsync`, `RefreshMonthlyDataAsync`, `GetCacheStatus`, `GetCachedFinancialOverview`, `GetHybridWithCurrentMonthAsync`, `GetFinancialOverviewRealTimeAsync`, `CreateStockSummary`, or the DI registration (which remains Scoped).

**Acceptance criteria:**
- Diff is limited to (a) removing the `_refreshLock` field and (b) removing the `lock (_refreshLock) { ... }` braces around the throttle check.
- `FinancialOverviewModule.cs` is unchanged.
- The public surface of `IFinancialAnalysisService` is unchanged.

### FR-4: Adjust or add unit tests
Verify the throttle check still works without the lock. If existing tests assert lock-related behavior, update them to match the new structure. Add a regression test asserting that a second call to `RefreshFinancialDataAsync` within 10 minutes is a no-op (does not invoke `ILedgerService` again).

**Acceptance criteria:**
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs` contains a test demonstrating that two back-to-back `RefreshFinancialDataAsync` calls result in only one set of underlying `ILedgerService` / `IStockValueService` invocations when the cache key `financial_last_refresh` is already populated within the 10-minute window.
- All existing tests in `FinancialAnalysisServiceTests.cs` and `FinancialOverviewModuleTests.cs` pass.
- `dotnet test` for the touched test project is green.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance change is expected. Removing a single uncontended `Monitor.Enter`/`Monitor.Exit` pair is negligible. The hot path (cached read in `GetFinancialOverviewAsync`) is untouched.

### NFR-2: Concurrency semantics (documented honestly)
The code must not imply guarantees it cannot deliver. The 10-minute throttle is best-effort: between the `IMemoryCache` read at line 111 and the `IMemoryCache.Set` at line 143, two concurrent refresh callers may both pass the gate and both perform a full refresh. This is the *current* behavior — the lock never prevented it — and is acceptable because:
- The refresh is idempotent (it writes the same cache keys with the same TTLs).
- The downstream `ILedgerService` / `IStockValueService` calls are themselves safe under concurrent invocation in the existing system.
- The 10-minute throttle is a coarse rate-limit, not a correctness invariant.

No code comment is required to document this; the removal alone removes the false impression. If a future requirement demands at-most-one-refresh-in-flight, see Open Questions.

### NFR-3: Security
No security impact. No auth, input validation, or secret handling is changed.

### NFR-4: Backward compatibility
Fully backward compatible — no API, contract, configuration, or schema change.

## Data Model
Unchanged. No entities, DTOs, or cache key formats are modified. The cache keys (`financial_monthly_data_*`, `financial_stock_data_*`, `financial_last_refresh`) remain identical.

## API / Interface Design
Unchanged. `IFinancialAnalysisService.RefreshFinancialDataAsync(DateTime?, DateTime?, CancellationToken)` keeps the same signature, observable side effects, and idempotency characteristics.

## Dependencies
- `Microsoft.Extensions.Caching.Memory.IMemoryCache` — already a constructor dependency; thread-safe for the operations used here.
- `ILedgerService` — already registered Singleton in `FlexiAdapterServiceCollectionExtensions.cs:75`. No lifetime change.
- `IStockValueService` — registered Scoped in `FinancialOverviewModule.cs:19`. No lifetime change.
- No new packages or framework features.

## Out of Scope
- **Promoting `FinancialAnalysisService` to Singleton** (brief's Option B). Rejected because `IStockValueService` is Scoped and would become a captive dependency. Doing this properly requires refactoring scope handling inside `RefreshFinancialDataAsync` (e.g. resolving `IStockValueService` from a child scope per call), which is a larger architectural change and is not the goal of this finding.
- **Adding a true at-most-one-refresh-in-flight guarantee** (e.g. a distributed lock, a `SemaphoreSlim` on a singleton coordinator, or an `Interlocked`-based gate). Not required by current product behavior; the throttle is best-effort and the refresh is idempotent.
- **Changing the throttle duration** (10 minutes) or the `LAST_REFRESH_CACHE_KEY` TTL (24 hours).
- **Refactoring the surrounding `RefreshFinancialDataAsync` method** (month loop, error handling, etc.) beyond removing the lock block.
- **Touching any other module** of the application.

## Open Questions
None.

## Status: COMPLETE