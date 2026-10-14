---
process: module-marketing-performance
kind: module
module: marketing-performance
summary: Marketing → Analýzy — monthly advertising spend (Meta, Google, Seznam) against e-shop orders and revenue, with PNO, ROAS and year-over-year comparison.
owns: []
verified_at: "5e993f9e2"
related: [calc-marketing-performance, sync-flexi-analytics]
---

# Marketing performance (Analýzy)

## Purpose
Shows, month by month, how much Anela spent on online advertising and what the e-shop sold in
the same month, so marketing can judge whether the ads pay off. It replaces the owner's manual
spreadsheet `Naklady_reklamy.xlsx`. Key figures: ad cost per channel (FB/IG, Google, S-Klik),
orders (Objednávky), revenue without VAT (Tržby bez DPH), **PNO** (ad cost as % of revenue —
lower is better), **ROAS** (revenue per crown of ads, in % — higher is better), average order
value, cost per order, and the same month a year earlier (r/r).

The numbers are a stored monthly snapshot recomputed every morning for the current and previous
month; older months stay frozen unless someone runs a manual recompute.

## Users & screens
- **Marketing → Analýzy** (`/marketing/performance`), permission `marketing.performance.read`
  (feature `Marketing_Performance`). Toolbar: Zobrazení (view), Období (12/24/36 months),
  Počet roků (2–3), Metrika, "včetně velkoobchodu" (add wholesale), hide the running month in the
  charts, last refresh time with a warning icon when a month has an error or is not fully computed.
  - **Vývoj** (trend): channel costs as stacked bars + one chosen metric as a line; table
    "Měsíční přehled".
  - **Meziroční srovnání** (year comparison): Jan–Dec, one line per year, YTD cards (cost,
    revenue, orders, PNO) and table "Měsíce podle roku".
- **Přepočítat** button (permission `marketing.performance.write`): dialog with Od/Do months,
  queues a recompute of that range and points to the Recurring Jobs page (Naplánované úlohy).
- Recurring Jobs page: job `marketing-performance-refresh` ("Marketing — výkon reklamy (měsíční
  snapshot)") can be disabled, run or re-scheduled there.
- No MCP tool and no dashboard tile.

## Processes
- `calc-marketing-performance` — builds the monthly snapshot: revenue/orders from issued
  invoices, ad cost from Flexi received invoices by supplier DIČ, ratios at read time. Daily
  Hangfire job `marketing-performance-refresh` at 05:00 Europe/Prague, plus manual recompute
  (`POST /api/MarketingPerformance/recompute`).

Plain read actions (no doc needed): `GET /api/MarketingPerformance/months` and
`GET /api/MarketingPerformance/comparison` behind the page. The module writes nothing to any
external system.

## Data owned
- `public."MarketingPerformanceMonths"` — one row per calendar month (unique Year+Month): retail
  and wholesale invoice counts and with-VAT revenue, number of skipped EUR invoices, `IsLocked`,
  `RevenueComputedAt`, `CostsComputedAt` (UTC, `timestamp without time zone`), `LastError`.
  Sums only; every ratio is derived when read.
- `public."MarketingPerformanceChannelCosts"` — one row per month × channel code (unique
  MonthId+ChannelCode, cascade-deleted with the month): cost without VAT and invoice count.
- `MarketingPerformanceRunGuard` — in-memory "a run is in progress" flag (not persisted).

## External systems
- **ABRA Flexi** (read only) — received invoices (`faktura-prijata`) via
  `POST /c/{company}/faktura-prijata/query`, filtered by accounting date `datUcto` within the
  month and supplier `dic` in the configured VAT IDs; one call per recomputed month. Fields used:
  `dic`, `datUcto`, `sumZklCelkem`, `storno`.

Revenue does not call any external system: it reads Heblo's own `IssuedInvoices` table, filled
from Shoptet by the Invoices module's issued-invoice import.

## Dependencies
- **Invoices module** — owns `IssuedInvoices` and registers `IssuedInvoiceMonthlyRevenueSource`
  (implements this module's `IMonthlyRevenueSource`).
- **Flexi adapter** — `IReceivedInvoicesClient` (shared with invoice classification) and
  `FlexiMonthlyAdCostSource`.
- **Background jobs** — recurring-job registry, enable/disable state, Hangfire.
- Read by: nobody else in Heblo. The analytics schema's `flexi_raw.v_ad_spend_monthly`
  (`sync-flexi-analytics`) computes the same ad spend independently from the ledger and is the
  cross-check.

## Known quirks
- S-Klik cost is stored **negative or too low** in some 2026 months while Meta and Google match
  the ledger exactly (cross-check 2026-09-22); details and likely cause in
  `calc-marketing-performance`.
- Revenue is with-VAT invoice totals divided by a flat 1.21; EUR invoices are excluded; order
  counts run 5–15 % above the old spreadsheet (2026-09-16).
- Cost month follows the invoice's accounting date, not when the ads ran.
- A recompute job that never runs blocks further recomputes (409) and makes the daily refresh skip
  silently until restart.
- `docs/features/marketing-performance.md` still describes a recompute race that has since been
  fixed.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs` — DI registration (no-op cost source overridden by the Flexi adapter)
- `backend/src/Anela.Heblo.API/Controllers/MarketingPerformanceController.cs` — the three endpoints and permissions
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/MarketingPerformanceRefreshService.cs` — the calculation
- `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/MarketingPerformanceMonth.cs` — stored month
- `frontend/src/components/marketing/performance/MarketingPerformancePage.tsx` — the page; metric texts in `metrics.ts`
- `docs/features/marketing-performance.md` — feature notes, Flexi query traps
