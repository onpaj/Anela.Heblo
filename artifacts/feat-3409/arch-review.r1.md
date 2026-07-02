# Architecture Review: FinancialAnalysisService Coverage Gaps

## Skip Design: true
This is a backend-only, test-only change. No new visual components, screens, or UI elements are required.

## Architectural Fit Assessment
This feature is a pure test-coverage addition. No production architecture changes. The existing `FinancialAnalysisServiceTests.cs` already establishes the test class structure with a shared constructor, mocks (`ILedgerService`, `IStockValueService`), and a real `MemoryCache` instance. New tests must follow the same constructor-based setup pattern already in use.

The implementation in `FinancialAnalysisService.cs` uses `IMemoryCache` directly (not via an interface wrapper), which means the test can manipulate cache state by writing directly to the same `MemoryCache` instance passed to the constructor — a pattern already used by `SeedCacheForMonth` and by the throttle test (`_memoryCache.Set("financial_last_refresh", ...)`).

## Proposed Architecture

### Component Overview
```
backend/test/Anela.Heblo.Tests/Application/FinancialOverview/
  FinancialAnalysisServiceTests.cs   ← add new test methods here
```

No new files needed. All new tests are added as `[Fact]` methods in the existing class.

### Key Design Decisions

#### Decision 1: Where to add tests
**Options considered:** New file vs. existing file  
**Chosen approach:** Add to `FinancialAnalysisServiceTests.cs`  
**Rationale:** The constructor already wires up all required mocks and the `MemoryCache`. A second file would need to duplicate this setup with no benefit. All tests are for the same service class.

#### Decision 2: Verifying the month-by-month loop
**Options considered:** Verify via `ILedgerService` call count vs. exposing a protected virtual method  
**Chosen approach:** Verify via `ILedgerService.GetLedgerItems` call count (Times.Exactly(N×2))  
**Rationale:** `RefreshMonthlyDataAsync` is private; the only externally observable effect is the calls it makes to `_ledgerService` and `_stockValueService`. Counting calls is the cleanest way to verify the loop ran N times. `ILedgerService` mock already supports `Verify`.

#### Decision 3: Verifying the default date range
**Options considered:** Assert dates via `It.Is<DateTime>` lambda vs. capture via `Capture`  
**Chosen approach:** Use `It.Is<DateTime>` with precise date assertions  
**Rationale:** The expected `startDate` and `endDate` are deterministic for any given `MonthsToCache` and a known `DateTime.UtcNow`. The test should freeze or constrain the date range by computing expected values using the same formula as production code and matching with Moq's `It.Is`.

## Implementation Guidance

### Directory / Module Structure
- Modify only: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`

### Interfaces and Contracts
Reuse existing mock setup pattern:
```csharp
_memoryCache.Set("financial_last_refresh", someDateTime, TimeSpan.FromHours(24));
```
The test must NOT pre-set the `financial_last_refresh` key (or set it to a value older than 10 minutes) to ensure `RefreshFinancialDataAsync` proceeds past the throttle check.

### Data Flow
For FR-2 (month loop test):
1. Clear / don't set `financial_last_refresh` so throttle check passes
2. Call `RefreshFinancialDataAsync(null, null)` with `MonthsToCache = 3`
3. Production code computes: `endDate = last day of previous month`, `startDate = endDate - 2 months` (3 - 1)
4. The while-loop runs for each of the 3 months; each iteration calls `_ledgerService.GetLedgerItems` twice (debit + credit queries)
5. Verify: `_ledgerServiceMock.Verify(..., Times.Exactly(6))` — or verify individual month ranges

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `DateTime.UtcNow` non-determinism in date assertions | Low | Compute expected dates using the same relative formula as production code, not hardcoded dates |
| Test isolation failure if cache is not per-instance | Low | Each test class instance gets a fresh `new MemoryCache(...)` from the constructor — already guaranteed |

## Specification Amendments
None. Spec FR-3 (`CalculatePeriodTotals`) is already covered by the existing `GetFinancialOverviewAsync_RealTime_ComputesIncomeAndExpensesByAccountPrefix_PreservingAllFr4Cases` test. Only FR-1 and FR-2 require new test methods.

## Prerequisites
None. All dependencies (`Moq`, `FluentAssertions`, `Microsoft.Extensions.Caching.Memory`) are already in the test project.
