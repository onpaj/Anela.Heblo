---
process: module-financial-overview
kind: module
module: financial-overview
summary: Company-level monthly income, expenses and stock value change from the Flexi ledger, with a year-over-year comparison (Finanční přehled).
owns: []
verified_at: "5e993f9e2"
related: [calc-financial-overview, calc-margins, sync-flexi-analytics]
---

# Financial overview (Finanční přehled)

## Purpose
Gives the owners a quick monthly read of how the company is doing: how much was earned
(Příjmy), spent (Náklady), the difference (Účetní bilance) and — because a month that builds up
stock looks worse in cash terms than it is — how much the value of stock changed (Změna hodnoty
skladu) and the resulting total (Celková bilance vč. skladu). A second view compares the same
months across 2–3 years, with the current month cut at the same day in each year so the
comparison is fair.

It is a read-only view over Flexi accounting. It owns no data and writes nothing to Flexi.

## Users & screens
- **Finanční přehled** — `/finance/overview` (menu Finance). Filters: period (current year,
  current + previous year, last 6 / 13 / 26 months), include stock data, include the current
  (incomplete) month, excluded cost centres (střediska; **Buvol** is excluded by default).
  View modes: timeline (chart, summary cards, monthly table) and year comparison (2 or 3 years,
  metrics Příjmy / Náklady / Účetní bilance / Celková bilance, calendar or rolling 12-month axis).
- Access: feature `Finance_FinancialOverview` (role `finance.financial_overview.read`).
- API: `GET /api/FinancialOverview`, `GET /api/FinancialOverview/comparison`; the cost-centre
  list comes from `GET /api/Departments` (UserManagement module).
- No MCP tool and no dashboard tile.

## Processes
- `calc-financial-overview` — monthly income/expenses from the Flexi ledger plus stock value
  change from Flexi stock-to-date; BackgroundRefresh task
  `IFinancialAnalysisService.RefreshFinancialDataAsync` (every 4 h) warms an in-memory cache of
  completed months, requests compute the rest live.

No plain CRUD actions — the module has no editable data.

## Data owned
No tables. In-process `IMemoryCache` only (24 h expiry, lost on restart):
- `financial_monthly_data_{year}_{month}` — income and expenses of one completed month (all cost centres).
- `financial_stock_data_{year}_{month}` — stock value change of one completed month, per warehouse type.
- `financial_last_refresh` — time of the last successful warm-up.

## External systems
- **Flexi (ABRA FlexiBee)**, read only:
  - `ucetni-denik` (general ledger) via `ILedgerService` — rows with debit or credit account
    starting `5` or `6`.
  - `stav-skladu-k-datu` (stock to date) via `IErpStockClient` — warehouses 5 MATERIAL,
    20 POLOTOVARY, 4 ZBOZI.
  - `stredisko` (cost centres) via `IDepartmentClient` — for the filter list (UserManagement).

## Dependencies
- **Catalog** — provides `IStockValueService` through `FinancialOverviewStockValueAdapter`
  (registered in `CatalogModule`), which uses Catalog's ERP stock client.
- **Shared Flexi ledger adapter** — `LedgerService`, also used by product margins (`calc-margins`)
  and cost pools; its 15-minute query cache is shared.
- **UserManagement** — department list for the filter.
- Nothing in Heblo reads from this module. The same ledger is copied separately into
  `flexi_raw` for Metabase (`sync-flexi-analytics`); the two are independent and may differ by
  sync timing.

## Known quirks
- Expenses include every class-5 account, including 58x (contra-cost) and 59x (income tax), so
  they do not match the accountant's operating costs; "Celková bilance" double-counts stock
  movement that Flexi already books to 501/58x (e.g. the August 2026 label stocktake). Details in `calc-financial-overview`.
- With the default Buvol exclusion every page load goes live to Flexi; the BackgroundRefresh
  cache is used only without a cost-centre filter.
- Flexi stock-to-date failures are read as value 0, producing fake swings in the stock change.
- Month boundaries use UTC, not Prague time.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/` — module, options, handlers, service
- `backend/src/Anela.Heblo.Domain/Features/FinancialOverview/` — `MonthlyFinancialData`, `MonthlyStockChange`, `IStockValueService`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs` — stock valuation
- `backend/src/Anela.Heblo.API/Controllers/FinancialOverviewController.cs` — endpoints
- `frontend/src/components/pages/FinancialOverview.tsx`, `frontend/src/components/pages/financial-overview/` — page, filters, chart, comparison
