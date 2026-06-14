# Architecture Review: Remove misleading instance-level lock in FinancialAnalysisService

## Skip Design: true

No UI/UX work — pure backend refactor that removes dead concurrency code in an internal service. No new components, screens, or visual design.

## Architectural Fit Assessment

The change aligns cleanly with existing patterns. `FinancialAnalysisService` already follows the project's conventions: Scoped lifetime per `docs/architecture/development_guidelines.md`, MediatR-driven handler usage, vertical-slice placement under `Features/FinancialOverview/Services/`, and shared state externalized to `IMemoryCache`. The instance-level `_refreshLock` is the one piece that contradicts those conventions — it implies cross-request coordination that a Scoped service cannot deliver. Removing it brings the implementation back in line with the rest of the module.

Integration points unaffected:
- `IBackgroundRefreshTaskRegistry` / `BackgroundRefreshSchedulerService` (confirmed at `BackgroundRefreshSchedulerService.cs:86` — creates a fresh DI scope per tick, so it always sees a *different* `_refreshLock` than user-request flows).
- `IFinancialAnalysisService` public contract — unchanged.
- DI graph — `IStockValueService` stays Scoped (`FinancialOverviewModule.cs:19`); `ILedgerService` stays Singleton (`FlexiAdapterServiceCollectionExtensions.cs:75`). No lifetime renegotiation required.

The grep `_refreshLock` returned three hits: the file under change, a different lock with the same name in `ManufactureBasedMaterialCostProvider.cs` (unrelated, do not touch), and a planning doc under `docs/plans/`. Only the service file should be modified.

## Proposed Architecture

### Component Overview

```
[ HTTP request ]                         [ BackgroundRefreshSchedulerService ]
       |                                              |
       v                                              v (per-tick: CreateScope())
[ DI scope #1 ]                                [ DI scope #2 ]
       |                                              |
       v                                              v
[ FinancialAnalysisService instance A ]      [ FinancialAnalysisService instance B ]
       |                                              |
       +---------------+      +-----------------------+
                       v      v
                  [ IMemoryCache (Singleton, thread-safe) ]
                       - financial_monthly_data_{year}_{month}
                       - financial_stock_data_{year}_{month}
                       - financial_last_refresh   <-- the actual cross-request gate
```

Before: each instance also holds a `_refreshLock` that only ever serializes the single request thread that owns it — i.e. no contention is possible, so the lock is decorative.

After: the only synchronization primitive is `IMemoryCache`, which is genuinely shared and thread-safe for the operations used here (`Get<T>` / `Set` with a TTL). The 10-minute throttle remains a best-effort rate-limit, not a correctness invariant.

### Key Design Decisions

#### Decision 1: Remove the lock; keep the service Scoped
**Options considered:**
- A. Remove the lock; leave lifetime as Scoped (spec choice).
- B. Promote service to Singleton so the lock would actually serialize refreshes.
- C. Replace the lock with a static `SemaphoreSlim` or `Interlocked.CompareExchange` gate to provide a real at-most-one-refresh-in-flight guarantee without changing lifetime.

**Chosen approach:** A.

**Rationale:** Option B is rejected because `IStockValueService` is Scoped (`FinancialOverviewModule.cs:19`); injecting it into a Singleton creates a captive-dependency bug. Option C is over-engineering for a finding whose stated goal is to remove misleading code, not to introduce a stronger guarantee. The refresh is idempotent (writes the same keys with the same TTLs); the downstream services are concurrent-safe; the 10-minute window is a coarse rate-limit. Spec is correct to stop there.

#### Decision 2: Do not add a "this is best-effort" comment
**Options considered:**
- Add a comment above the throttle check explaining the race window between read and write.
- Leave the code uncommented.

**Chosen approach:** Leave it uncommented.

**Rationale:** Per the project's "Surgical changes" rule and the global "default to no comments" guidance, the removal itself eliminates the misleading signal. Adding prose about a race window the lock never prevented would replace one piece of decorative code with another. If a future requirement demands a real guarantee, that work introduces its own context and its own documentation.

#### Decision 3: Regression test seeds `financial_last_refresh` directly
**Options considered:**
- A. Drive the throttle by calling `RefreshFinancialDataAsync` twice and asserting the second is a no-op.
- B. Pre-seed `LAST_REFRESH_CACHE_KEY` in the shared `IMemoryCache` and assert one call is a no-op.

**Chosen approach:** B.

**Rationale:** Option A couples the test to the full refresh pipeline (24-month loop, ledger/stock fan-out) and runs many more mocked calls than necessary. Option B isolates the throttle behavior — exactly what the spec asks to verify in FR-4 — and matches the seeding pattern already used in `FinancialAnalysisServiceTests.cs:174` (`SeedCacheForMonth`).

## Implementation Guidance

### Directory / Module Structure

No new files. All changes touch:

```
backend/
├── src/Anela.Heblo.Application/Features/FinancialOverview/Services/
│   └── FinancialAnalysisService.cs        # delete line 22, replace lock block at 109-117
└── test/Anela.Heblo.Tests/Application/FinancialOverview/
    └── FinancialAnalysisServiceTests.cs   # add one regression test
```

`FinancialOverviewModule.cs` — unchanged. Public surface of `IFinancialAnalysisService` — unchanged.

### Interfaces and Contracts

No interface changes. `IFinancialAnalysisService.RefreshFinancialDataAsync(DateTime?, DateTime?, CancellationToken)` keeps its signature, observable side effects, and idempotency.

Cache key contract unchanged:
- `financial_monthly_data_{year}_{month}` — 24h TTL
- `financial_stock_data_{year}_{month}` — 24h TTL
- `financial_last_refresh` — 24h TTL, value is `DateTime` (UTC)

### Data Flow

```
RefreshFinancialDataAsync(start, end, ct)
  │
  ├─► read LAST_REFRESH_CACHE_KEY from IMemoryCache
  │     └─► if (now - last) < 10min → LogDebug + return (no-op)
  │
  ├─► default start/end if null
  │
  ├─► for each month, newest → oldest:
  │     RefreshMonthlyDataAsync(monthStart, monthEnd, ct)
  │       ├─► await Task.WhenAll(
  │       │       ledger.GetLedgerItems(debit),
  │       │       ledger.GetLedgerItems(credit),
  │       │       stockValue.GetStockValueChangesAsync())
  │       └─► IMemoryCache.Set(monthly + stock keys, 24h TTL)
  │
  └─► IMemoryCache.Set(LAST_REFRESH_CACHE_KEY, UtcNow, 24h)
```

The only structural change: the first step drops its `lock (_refreshLock) { ... }` wrapper. Everything downstream is byte-identical.

### Test Plan

Add one xUnit test to `FinancialAnalysisServiceTests.cs`. Suggested shape (use FluentAssertions; reuse the existing `_service` and `_memoryCache` fields):

```csharp
[Fact]
public async Task RefreshFinancialDataAsync_WhenLastRefreshWithinThrottleWindow_DoesNotInvokeDownstreamServices()
{
    // Arrange: seed last-refresh timestamp inside the 10-minute window
    _memoryCache.Set("financial_last_refresh", DateTime.UtcNow.AddMinutes(-1), TimeSpan.FromHours(24));

    // Act
    await _service.RefreshFinancialDataAsync(startDate: null, endDate: null);

    // Assert: throttle short-circuits before any downstream call
    _ledgerServiceMock.Verify(
        x => x.GetLedgerItems(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
    _stockValueServiceMock.Verify(
        x => x.GetStockValueChangesAsync(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

No existing tests in `FinancialAnalysisServiceTests.cs` reference the lock; nothing else needs editing. `FinancialOverviewModuleTests.cs` is untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Reader assumes the throttle is a hard at-most-one-in-flight guarantee | Low | Spec explicitly documents (NFR-2) the throttle is best-effort. Refresh is idempotent — concurrent overlapping refreshes converge on the same cache state. |
| Background scheduler and a user-triggered cache miss perform a duplicate refresh in the narrow window after a 10-minute throttle expires | Low | This is the *current* observable behavior — the lock never prevented it. Downstream services (`ILedgerService`, `IStockValueService`) are safe under concurrent invocation. If a future change must eliminate this, introduce a `static SemaphoreSlim` or an Interlocked gate then. |
| Tests inadvertently rely on lock ordering | Very low | Verified `FinancialAnalysisServiceTests.cs` — no test references `_refreshLock` or lock semantics. |
| Unrelated `_refreshLock` symbol in `ManufactureBasedMaterialCostProvider.cs` gets edited by mistake | Low | Surgical-changes rule. Reviewer should confirm the diff is limited to `FinancialAnalysisService.cs` + the new test. |
| `docs/plans/2025-12-21-margin-cache-architecture.md` references `_refreshLock` and becomes stale | Negligible | Out of scope; planning docs reflect historical context. Spec is explicit (Out of Scope: "Touching any other module"). |

## Specification Amendments

None. The spec is internally consistent and accurately describes the code as it exists (verified against `FinancialAnalysisService.cs:22` and `:109-117`, `FinancialOverviewModule.cs:19,28`, `BackgroundRefreshSchedulerService.cs:86`).

One optional clarification for the implementer, not a spec change: the regression test in FR-4 should pre-seed `financial_last_refresh` directly in `IMemoryCache` rather than relying on two back-to-back calls. This isolates the throttle behavior under test and matches the `SeedCacheForMonth` pattern already established in the test file.

## Prerequisites

None. No migrations, no configuration changes, no infrastructure work, no new packages. The change is a pure code edit + one new test inside an existing test class. Validation gate is the standard project gate from `CLAUDE.md`:

- `dotnet build` clean
- `dotnet format` clean
- `dotnet test` green for `Anela.Heblo.Tests` (touched test project)