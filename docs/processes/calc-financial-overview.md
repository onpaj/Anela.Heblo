---
process: calc-financial-overview
kind: calculation
module: financial-overview
summary: Monthly company income, expenses and stock value change (a P&L-style view) computed live from the Flexi general ledger and stock-to-date warehouse values, with a year-over-year comparison, shown on Finanční přehled.
owns:
  - backend/src/Anela.Heblo.Application/Features/FinancialOverview/**
  - backend/src/Anela.Heblo.Domain/Features/FinancialOverview/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs
  - backend/src/Anela.Heblo.API/Controllers/FinancialOverviewController.cs
verified_at: "5e993f9e2"
related: [calc-margins, sync-flexi-analytics]
---

# Financial overview (monthly income, expenses, stock change)

## Purpose
Answers "did the company make or lose money in a month, and how does this year compare to last
year?". For each calendar month it shows:

| Figure | Czech UI label | Meaning |
|---|---|---|
| Income | Příjmy | revenue booked on ledger class **6** (net of reversals) |
| Expenses | Náklady | costs booked on ledger class **5** (net of reversals) |
| Financial balance | Účetní bilance | Income − Expenses |
| Stock value change | Změna hodnoty skladu | value of stock at month end − value at month start, for the MATERIAL, POLOTOVARY and ZBOZI warehouses |
| Total balance | Celková bilance (vč. skladu) | Financial balance + stock value change |

It is a management view of the accounting, not the accountant's official P&L (see Known quirks).
Amounts are in CZK, excluding VAT (ledger amounts in local currency; VAT sits on balance-sheet
accounts, which are never read).

Consumers: page **Finanční přehled** (`/finance/overview`, menu Finance), in two view modes —
timeline (chart + cards + monthly table) and year-over-year comparison. API:
`GET /api/FinancialOverview` and `GET /api/FinancialOverview/comparison`, both gated by feature
`Finance_FinancialOverview` (role `finance.financial_overview.read`). No MCP tool exposes it.

## Trigger
No Hangfire job. Two paths:

1. **BackgroundRefresh cache warm-up** — task `IFinancialAnalysisService.RefreshFinancialDataAsync`
   (config `BackgroundRefresh:IFinancialAnalysisService:RefreshFinancialDataAsync`): hydration
   tier 3, `InitialDelay` 5 min, then every 4 h. It skips itself when the previous successful run
   was less than 10 minutes ago. Can be forced via `POST /api/backgroundrefresh/tasks/{taskId}/force-refresh`.
   Disabled in Conductor dev instances (`BackgroundServices:EnableHydration=false`).
2. **On request** (`GetFinancialOverviewHandler`, `GetFinancialComparisonHandler`) — reads the cache
   where it can, otherwise queries Flexi live (rules below).

## Data flow
Sources (both FlexiBee REST, read-only):
- **Ledger** — `ILedgerService.GetLedgerItems` (`LedgerService`, FlexiBee `ucetni-denik` query),
  called twice per period: once filtered on debit account prefixes `5`,`6` and once on credit
  account prefixes `5`,`6`. Each result is cached 15 min in `LedgerService` under
  `ledger_{from}_{to}_{debit}_{credit}_{department}`.
- **Stock value** — `IStockValueService`, implemented by Catalog's
  `FinancialOverviewStockValueAdapter` → `IErpStockClient.StockToDateAsync` → FlexiBee
  `stav-skladu-k-datu` for warehouses **5 MATERIAL, 20 POLOTOVARY, 4 ZBOZI**. Not cached.

Cache warm-up (`RefreshFinancialDataAsync`):
1. Window = the last `MonthsToCache` **completed** months, ending on the last day of the previous
   month (UTC).
2. Month by month, newest first, sequentially: ledger debit + credit query and stock change for
   that month, in parallel.
3. Writes `IMemoryCache` entries, 24 h expiry:
   `financial_monthly_data_{year}_{month}` (`MonthlyFinancialData`: income, expenses),
   `financial_stock_data_{year}_{month}` (`MonthlyStockChange`), and `financial_last_refresh`.

Overview request (`GET /api/FinancialOverview?months&includeStockData&excludedDepartments&includeCurrentMonth`):

| Request | Path |
|---|---|
| `excludedDepartments` non-empty | **Real-time**: one ledger query pair over the whole range, filtered in memory by department, split by month; stock change month by month live |
| `includeCurrentMonth=true`, no department filter | **Hybrid**: current month (1st → today) live + `months − 1` completed months from cache; full real-time if the cache is empty or the hybrid fails |
| neither | **Cached**: the last `months` completed months from cache; full real-time if no month is cached or reading fails |

Comparison request (`GET /api/FinancialOverview/comparison?years&includeStockData&excludedDepartments&includePartialMonth`):
1. `years` clamped to 2..3. **Cutoff date** = today (UTC) − `PartialMonthLagDays`; its year is the
   anchor year, its month the partial month, its day the cut day.
2. For each year (anchor, anchor−1, anchor−2), months 1..12; anchor-year months after the partial
   month are skipped; the partial month is skipped when `includePartialMonth=false`.
3. A full month without department filter is taken from the cache when present; anything else
   (partial month, department filter, cache miss) is computed live for that single month.
4. The partial month is cut at the same day in every year (`min(cutDay, daysInMonth)`), so
   e.g. 1.–27. September 2026 is compared with 1.–27. September 2025 and 2024. Its stock change is
   value(cut day) − value(1st).
5. Per year, YTD totals sum the months up to and including the partial month.

Nothing is persisted; a restart empties the cache and the next warm-up refills it.

## Logic & formulas
Per period, from the debit-filtered rows `D` and credit-filtered rows `C`:
```
Expenses = Σ D.amount where debit account starts with "5"  −  Σ C.amount where credit account starts with "5"
Income   = Σ C.amount where credit account starts with "6"  −  Σ D.amount where debit account starts with "6"
FinancialBalance = Income − Expenses
```
A row posted 5x→5x (reclassification) appears in both lists and nets to zero.

Stock value per warehouse and date = Σ (`Stock` × `Price`) over the stock-to-date rows, where
`Price` is FlexiBee's exact average warehouse price (`ExactAveragePrice` = tuz / stavMJ), not the
purchase price.
```
StockChange(month) = Σ_warehouse [ value(last day of month) − value(1st of month) ]
TotalBalance       = FinancialBalance + StockChange       (stock change 0 when unavailable)
```

**Department filter** (`excludedDepartments`, Flexi cost-centre codes, e.g. `BUVOL`): ledger rows
whose cost centre code is in the list are dropped (case-insensitive). Rows without a cost centre
are always kept. The stock change is never filtered by department.

**Summary** (timeline view): totals and plain arithmetic monthly averages of income, expenses,
balance, stock change and total balance over the returned months.

**Page defaults** (`FinancialOverview.tsx`): period "current year" = completed months of this
year (`months = current month index`, +1 when "include current month" is ticked); other periods
6, 13, 26 months or current + previous year. Stock data on, current month off, comparison 2
years. The **Buvol** cost centre is excluded by default once the department list loads
(`GET /api/Departments`, Flexi `stredisko`, cached 10 min). The comparison passes the page's
"include current month" toggle as `includePartialMonth`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `FinancialAnalysisOptions:MonthsToCache` | 24 (class default); **30** in `appsettings.Production.json` | completed months the warm-up caches |
| `FinancialAnalysisOptions:PartialMonthLagDays` | 5 | days subtracted from today to pick the comparison cutoff, absorbing late bookkeeping |
| `BackgroundRefresh:IFinancialAnalysisService:RefreshFinancialDataAsync:InitialDelay` | `00:05:00` | delay before the first warm-up during hydration |
| `…:RefreshInterval` | `04:00:00` | warm-up interval |
| `…:HydrationTier` | 3 | runs after tiers 1–2; above the production readiness tier (2), so a failure does not block start-up |
| `…:Enabled` | true | |

## Runtime facts
None.

## Known quirks
- **The default page view never uses the cache.** The page excludes the Buvol cost centre by
  default, and any department filter forces the real-time path. The warm-up therefore only helps
  when a user clears the department filter. A default comparison view is fully live too: up to
  3 years × 12 months × (2 ledger + 6 stock-to-date calls) against Flexi per load.
- **Class 5 is not all operating cost** (agent memory, 2026-09): the expense figure takes every
  `5…` account, so it includes group **58x** (změna stavu zásob vlastní činnosti / aktivace, a
  contra-cost, on 2020–2026 about −3.33 M Kč net) and **59x** (income tax, about +1.85 M Kč).
  Expenses therefore do not reconcile with the accountant's operating costs.
- **Possible double count in "Celková bilance".** 58x already moves with the value of own-production
  inventory, and the separately added stock change of POLOTOVARY/ZBOZI measures that movement
  again. Inferred from the account semantics, not verified against booked data.
- **A failed stock-to-date call reads as 0.** `GetWarehouseStockValueAsync` catches every
  exception and returns 0 for that warehouse/date, so a Flexi timeout shows as a swing of the whole
  warehouse value in one month — and the warm-up caches it for 24 h.
- **A failed month shows as zero.** The warm-up logs and skips a month whose ledger query fails;
  the cached path then returns income 0 / expenses 0 for it instead of falling back to live data
  (it only falls back when *no* month is cached).
- **Day-1 stock movements may be lost.** Month change = value(last day) − value(1st). If
  `stav-skladu-k-datu` returns the end-of-day state (not verifiable in the repo), movements booked
  on the 1st fall between two months and are counted in neither.
- **Month boundaries are UTC.** "Previous month", "today" and the comparison cutoff come from
  `DateTime.UtcNow`; for the first 1–2 h of a month (Prague time) the app still treats the
  previous month as current.
- **Dead config**: `FinancialAnalysisOptions:RefreshInterval` in `appsettings.Development.json` and
  `appsettings.Test.json` binds to nothing; the interval lives under `BackgroundRefresh`.
- The cache status check only looks at the last 24 months, regardless of `MonthsToCache`.
- Stock change ignores the department filter, so "Celková bilance" with Buvol excluded still
  contains any Buvol stock movement in those three warehouses.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` — every formula, cache key and path choice
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs` — DI + BackgroundRefresh registration
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialAnalysisOptions.cs` — options
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs` — stock value per warehouse/date
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Ledger/LedgerService.cs` — ledger query + 15-min cache (owned by `calc-margins`)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockClient.cs`, `FlexiStockMappingProfile.cs` — stock-to-date call and price mapping
- `backend/src/Anela.Heblo.API/Controllers/FinancialOverviewController.cs` — endpoints and defaults
- `frontend/src/components/pages/FinancialOverview.tsx` — page defaults (Buvol exclusion, period → months)
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/` — service, handler and stock adapter tests
