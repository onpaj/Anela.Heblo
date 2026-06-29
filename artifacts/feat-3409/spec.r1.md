# Specification: FinancialAnalysisService Coverage Gaps — RefreshFinancialDataAsync and CalculatePeriodTotals

## Summary
Two critical code paths in `FinancialAnalysisService` are untested: the `RefreshFinancialDataAsync` method's throttle guard, default date-range computation, and month-by-month loop logic; and the `CalculatePeriodTotals` private method's account-prefix routing and debit/credit arithmetic. This feature adds targeted unit tests for these paths, raising line coverage from 24.8% toward the 60% threshold and protecting against silent regressions in financial data integrity.

## Background
The existing test suite covers only the `GetFinancialOverviewAsync` caching/hybrid paths (6 tests). The financial refresh mechanism (`RefreshFinancialDataAsync`) and the core calculation formula (`CalculatePeriodTotals`) are completely untested. `CalculatePeriodTotals` is already partially tested indirectly via `GetFinancialOverviewAsync_RealTime_ComputesIncomeAndExpensesByAccountPrefix_PreservingAllFr4Cases`. The remaining gaps are the throttle guard that has already been added (`RefreshFinancialDataAsync_WhenLastRefreshWithinThrottleWindow_DoesNotInvokeDownstreamServices`), the date range defaults, and the month-by-month iteration.

## Functional Requirements

### FR-1: Test RefreshFinancialDataAsync default date range calculation
When `startDate` and `endDate` are both `null`, the method computes `endDate` as the last day of the previous month and `startDate` as `endDate - MonthsToCache + 1` months. A test must verify that the ledger service is invoked with parameters matching this computed range.

**Acceptance criteria:**
- Call `RefreshFinancialDataAsync(startDate: null, endDate: null)` with `MonthsToCache = 3`
- Assert `GetLedgerItems` is called with a start date equal to the first day of the month that is 2 months before the current month
- Assert `GetLedgerItems` is called with an end date equal to the last day of the previous month
- Assert the ledger service is called (i.e., the throttle window has not been entered)

### FR-2: Test RefreshFinancialDataAsync month-by-month loop
When called outside the throttle window, `RefreshFinancialDataAsync` must call `RefreshMonthlyDataAsync` (via the ledger/stock services) once for each month in the configured date range.

**Acceptance criteria:**
- Call `RefreshFinancialDataAsync` with `MonthsToCache = 3` and no pre-set throttle
- Assert the ledger service is called exactly 3 × 2 times (two `GetLedgerItems` calls per month — one for debit prefix, one for credit prefix)
- Assert the last-refresh cache entry is set after a successful call

### FR-3: Test CalculatePeriodTotals account routing
`CalculatePeriodTotals` routes items to expenses (accounts starting with "5") or income (accounts starting with "6") and applies the formula `expenses = debit5 - credit5`, `income = credit6 - debit6`. (This is already partially covered; this requirement captures complete coverage of this formula.)

**Acceptance criteria:**
- Provide a debit-5xx item (amount 100), a credit-5xx item (amount 20), a credit-6xx item (amount 200), and a debit-6xx item (amount 50)
- Assert `expenses = 80`, `income = 150` via the real-time path

## Non-Functional Requirements

### NFR-1: Test isolation
Each test must use a fresh `MemoryCache` instance; no shared state between tests.

### NFR-2: No production code changes
This feature adds only test code. No changes to `FinancialAnalysisService.cs` or any production files.

## Data Model
No new entities. Tests use `LedgerItem` (existing domain model with `Date`, `DebitAccountNumber`, `CreditAccountNumber`, `Amount` properties).

## API / Interface Design
N/A — test-only change. Tests are added to `FinancialAnalysisServiceTests.cs` in `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/`.

## Dependencies
- `Moq` — already in the test project for mocking `ILedgerService` and `IStockValueService`
- `FluentAssertions` — already in the test project
- `Microsoft.Extensions.Caching.Memory` — already in use

## Out of Scope
- Integration tests or E2E tests
- Production code modifications
- Testing `GetCacheStatus` or `GetCachedFinancialOverview` methods
- Testing `CreateStockSummary` methods

## Open Questions
None.

## Status: COMPLETE
