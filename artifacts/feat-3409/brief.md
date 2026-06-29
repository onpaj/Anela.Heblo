## Module / File
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`

## Coverage
Line coverage: 24.8% (filter threshold: 60%)
6 existing tests — most cover only the `GetFinancialOverviewAsync` caching/hybrid paths.

## What's not tested

**`RefreshFinancialDataAsync`** (the method that populates the cache month-by-month):
- The 10-minute throttle guard: if `DateTime.UtcNow - lastRefresh < 10min`, the method returns early. No test verifies that a second refresh within 10 minutes is silently skipped.
- Default date range calculation: `endDate` defaults to the last day of the previous month; `startDate` defaults to `endDate - MonthsToCache + 1`. Off-by-one in this logic would cause the oldest month to be missed on every refresh.
- The month-by-month loop processing sequentially from newest to oldest — no test exercises this path at all (no calls to `RefreshMonthlyDataAsync` are verified).

**`CalculatePeriodTotals` (private static)**:
- Routes ledger items to either expenses or income based on account number prefix: accounts starting with `"5"` → expenses, accounts starting with `"6"` → income. Debit and credit sides are summed and subtracted (`debit5 - credit5` for expenses; `credit6 - debit6` for income). This formula is the foundation of every monthly figure shown in the financial dashboard — a sign error here would silently invert a revenue or cost line without any assertion catching it.

## Why it matters
A bug in the throttle guard means financial data is never refreshed (stale until restart). A sign error in `CalculatePeriodTotals` silently corrupts all income/expense figures across the overview — no existing test would catch either regression.

## Suggested approach
- Unit test `RefreshFinancialDataAsync` directly: mock `ILedgerService` and `IStockValueService`, call refresh twice within 10 minutes, assert the ledger is called only once (throttle path). Also test that date range defaults produce the expected `startDate`/`endDate`.
- Exercise `CalculatePeriodTotals` indirectly via `GetFinancialOverviewRealTimeAsync` with a small set of seeded `LedgerItem` objects covering: a debit-5xx item, a credit-5xx item, a credit-6xx item, a debit-6xx item, and assert the resulting income/expense totals match expected arithmetic. ~1 day effort.

---
_Filed by weekly coverage-gap routine on 2026-06-29. Based on CI run #28295125598 (23c3b5d571c976074ee31869c96e29487098040c)._
