# Marketing Performance ("Výkon reklamy") — Design

**Date:** 2026-09-18
**Status:** approved in brainstorming, awaiting implementation plan
**Replaces:** the manually maintained `Naklady_reklamy.xlsx` workbook (sheet `2024-26`)

## 1. Goal

Replace the hand-filled monthly ad-performance spreadsheet with data the application
computes itself, once a day, from sources that already exist:

- **Ad spend** per channel (FB/IG, Google, S-Klik) from **ABRA Flexi received invoices**,
  bucketed by supplier VAT ID (DIČ).
- **Orders and revenue** from the **`IssuedInvoices` table** already synced from Shoptet.

The result is a new screen under **Marketing → Výkon reklamy** with a raw monthly table
(same columns as the spreadsheet), a trend chart, and a year-over-year comparison chart
where June 2026 can be read against June 2025 and June 2024.

### Explicitly out of scope (first version)

- The `Akce` sheet (per-campaign spend/revenue/orders). Natural second phase on top of
  existing `MarketingAction` date ranges.
- Per-channel revenue attribution (the `2023` sheet columns "Tržby FB/IG / Google / S-Klik").
- Importing the Excel history. Both sources hold history back to 2023, so the backfill is
  a recompute, not an import.
- EUR invoices in revenue (they are counted and shown as skipped, never summed).
- CSV/Excel export, editing of stored values, Shoptet API calls of any kind.
- The existing `ImportedMarketingTransactions` / Meta Ads / Google Ads API importers.
  They are empty in production and are not used by this feature.

## 2. Decisions taken during brainstorming

| Topic | Decision | Why |
|---|---|---|
| Storage | Persisted monthly snapshot table, never re-summed at read time | Owner requirement; history survives restarts; months can be frozen |
| Cost source | Flexi received invoices filtered by `dic in (...)` | Meta/Google direct API import proved unworkable; invoices are the accounting truth |
| Channel mapping | Config: channel → list of VAT IDs | Owner requirement; multiple IDs per channel; no code change for a new supplier |
| Flexi query style | Standard REST filter, **not** a user-defined query | REST supports `in`; user queries live in the ERP per company and already bit us (query 41) |
| Revenue source | `IssuedInvoices` (our DB), CZK only, by `TaxDate` | Already synced daily; no new integration |
| With/without VAT | Store with-VAT only; derive without-VAT by ÷ VAT rate (1.21) | `IssuedInvoices.Price` is the with-VAT total, `PriceC` is never populated; matches the spreadsheet formula |
| Retail vs wholesale | Wholesale = invoice has customer VAT ID (`IssuedInvoices.VatPayer`) | Same rule as Flexi sales query 37 (`LENGTH(D.DIC) > 0` → VO), so it agrees with margin reports |
| Wholesale in totals | Stored separately; user toggles `includeWholesale` at read time | Owner request; the toggle must not trigger a recompute |
| Recompute window | Current month + previous month, from setting `RecomputeWindowMonths` (default 2) | Owner request (three was too many) |
| Older months | Locked automatically; changed only by explicit recompute | Snapshot semantics |
| Gating | Permission only, **no feature flag** | Owner decision |

### Known discrepancy versus the spreadsheet

Live production data (2026, CZK, by tax date) runs 5–15 % above the spreadsheet's order
counts and 17–32 % above its "Shoptet s DPH" revenue. The spreadsheet was filled from a
different source (Shoptet statistics). The application defines the metric from now on; the
wholesale toggle covers the part of the gap the owner identified. This is accepted, not a bug.

## 3. Data model

Persistence lives under `backend/src/Anela.Heblo.Persistence/Marketing/` (existing marketing
folder), domain under `Domain/Features/MarketingPerformance/`.

### `MarketingPerformanceMonths`

| Column | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `Year`, `Month` | int | unique index `(Year, Month)` |
| `RetailOrderCount` | int | CZK invoices, `VatPayer = false` |
| `RetailRevenueWithVat` | numeric(18,2) | sum of `Price` |
| `WholesaleOrderCount` | int | CZK invoices, `VatPayer = true` |
| `WholesaleRevenueWithVat` | numeric(18,2) | |
| `SkippedEurInvoiceCount` | int | visibility of what is excluded |
| `IsLocked` | bool | set when the month leaves the recompute window |
| `RevenueComputedAt` | timestamptz? | null until first successful revenue step |
| `CostsComputedAt` | timestamptz? | null until first successful cost step |
| `LastError` | text? | last failure message of either step; cleared on success |

### `MarketingPerformanceChannelCosts`

| Column | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `MonthId` | int FK → months, cascade delete | |
| `ChannelCode` | varchar(32) | from settings; unique index `(MonthId, ChannelCode)` |
| `CostWithoutVat` | numeric(18,2) | Flexi `sumZklCelkem`, storno excluded |
| `InvoiceCount` | int | |

Channel code is a string, not an enum: adding a channel is a configuration change.

### Settings (`MarketingPerformance` section)

```json
"MarketingPerformance": {
  "RecomputeWindowMonths": 2,
  "VatRate": 1.21,
  "CronExpression": "0 5 * * *",
  "Channels": [
    { "Code": "meta",   "Label": "FB/IG",  "VatIds": ["<Meta DIČ>"] },
    { "Code": "google", "Label": "Google", "VatIds": ["<Google DIČ>"] },
    { "Code": "sklik",  "Label": "S-Klik", "VatIds": ["<Seznam DIČ>"] }
  ]
}
```

Validated with `ValidateOnStart`: at least one channel, every channel has ≥ 1 VAT ID, no VAT
ID appears in two channels, `RecomputeWindowMonths ≥ 1`, `VatRate > 1`. Real VAT IDs are
supplied by the owner (Key Vault override allowed, but they are not secrets).

### Derived metrics (never stored)

Computed in `MarketingMetricsCalculator` (pure, unit-tested) for a selected mode
(`includeWholesale` false → retail only; true → retail + wholesale):

- `Orders`, `RevenueWithVat`, `RevenueWithoutVat = RevenueWithVat / VatRate`
- `TotalCost = Σ channel CostWithoutVat`
- `Pno = TotalCost / RevenueWithoutVat × 100` (null when revenue is 0)
- `Roas = RevenueWithoutVat / TotalCost × 100` (null when cost is 0)
- `Profit = RevenueWithoutVat − TotalCost`
- `AvgOrderValue = RevenueWithoutVat / Orders` (null when 0 orders)
- `CostPerOrder = TotalCost / Orders` (null when 0 orders)
- Year-over-year ratios for cost, revenue, orders: `current / sameMonthLastYear × 100`
  (null when last year's month is missing or zero)

## 4. Refresh job

`MarketingPerformanceRefreshJob : IRecurringJob`, name `marketing-performance-refresh`,
cron from settings (default 05:00 daily, after the invoice imports at 04:00/04:15). Uses
`[DisableConcurrentExecution]` and the `IRecurringJobStatusChecker` guard like the other
jobs, so it appears in the Recurring Jobs admin page with enable/disable and "Run now".

For each month in the window (`now` back `RecomputeWindowMonths − 1` months), run two
independent steps, then upsert inside one transaction per month:

**Revenue step (local DB).** Single grouped query over `IssuedInvoices` where
`Currency = 'CZK'` and `TaxDate` within the month, grouped by `VatPayer`, returning
count and `SUM(Price)`. Second query counts `Currency = 'EUR'` rows in the month.
Stamps `RevenueComputedAt`.

**Cost step (Flexi).** One `SearchByVatIdsAsync(monthStart, monthEnd, allVatIds)` call
(filter: `datUcto` within month, `dic in (...)`, `limit = 0`). Each invoice is bucketed to
the channel owning its `dic`; `storno = true` documents are skipped; `sumZklCelkem` is summed.
Channel rows for the month are replaced as a set, so a channel with no invoices gets an
explicit zero row. Stamps `CostsComputedAt`.

**Failure semantics.** A failing step records `LastError` on the month and leaves the other
step's result intact. The job continues with the next month. It throws (→ Hangfire retry,
failed-jobs tile) only when every month failed.

**Locking.** After the window is processed, unlocked months older than the window are set
`IsLocked = true`. The scheduled run never touches locked months.

**Manual recompute.** `RecomputeMarketingPerformanceHandler` runs the same two steps for an
arbitrary month range, ignoring locks. Enqueued as a fire-and-forget Hangfire job by the API
endpoint; used once for the 2023→today backfill and later for corrections.

### Flexi SDK change (repo `FlexiBeeSDK`, then version bump in Heblo)

- `ReceivedInvoiceRequest`: optional `IEnumerable<string>? vatIds` → appends
  `and dic in ("A","B")` to the filter.
- `Detail` projection gains `dic` and `storno`; `ReceivedInvoiceFlexiDto` gains matching
  properties.
- Heblo adapter: `IReceivedInvoicesClient.SearchByVatIdsAsync(DateTime from, DateTime to,
  IReadOnlyCollection<string> vatIds, CancellationToken)` beside the existing methods;
  domain `ReceivedInvoice` gains `CompanyVat` if not already mapped and `IsStorno`.

### Assumptions to verify first (read-only, one month, before schema work)

1. Flexi received invoices from Meta, Google and Seznam carry a populated `dic`, and the
   exact strings to put in settings.
2. `sumZklCelkem` is the without-VAT base for reverse-charge (EU) invoices as well as
   domestic ones.

Findings go into `docs/integrations/flexibee-api.md`.

## 5. API

`MarketingPerformanceController`, `[FeatureAuthorize(Feature.Marketing_Performance)]` at class
level, route `/api/marketing-performance`. All contracts are classes; responses inherit
`BaseResponse`.

| Endpoint | Level | Purpose |
|---|---|---|
| `GET /months?from=YYYY-MM&to=YYYY-MM&includeWholesale=bool` | Read | Rows for the range with derived metrics; missing months returned as empty rows; default last 36 months; max 60 |
| `GET /comparison?years=2..3&includeWholesale=bool` | Read | One series per calendar year (12 cells + YTD), current month flagged partial; same shape idea as `GetFinancialComparisonResponse` |
| `POST /recompute` `{ "from": "YYYY-MM", "to": "YYYY-MM" }` | Write | Validates (≤ 60 months, not in future, no run in progress), enqueues job, returns Hangfire job id (202) |

New error codes in a dedicated module range: `InvalidMonthRange`, `MonthRangeTooLarge`,
`RecomputeAlreadyRunning`. Register the range in `ErrorHandlingTests` and add Czech
translations in `i18n.ts` in the same change.

Registration: `MarketingPerformanceModule.AddMarketingPerformanceModule(IConfiguration)`
binds options, registers repository, calculator and the Flexi client; DbSets added to
`ApplicationDbContext`; EF configurations picked up by assembly scan.

## 6. Permission

`access-matrix.json`: feature `{ "key": "Marketing_Performance", "label": "Výkon reklamy",
"hasWrite": true }`, menu path `/marketing/performance` requiring Read, roles added to seed
groups `Marketer` (read) and `Vedeni` (read). Regenerate the five artifacts. After deploy,
grant `marketing.performance.read` (+ `.write` for whoever runs recomputes) in
`/admin/access` on staging and production — seed groups do not update existing environments.

## 7. Frontend

Route `/marketing/performance` behind `guard(...)`; sidebar entry in the `marketing` section.
Files:

```
frontend/src/api/hooks/useMarketingPerformance.ts        // react-query hooks, absolute URLs
frontend/src/components/marketing/performance/
  MarketingPerformancePage.tsx                            // toolbar + view switch + status line
  PerformanceTrendChart.tsx                               // Chart.js: stacked channel cost bars + 1 metric line, 2 y-axes
  PerformanceComparisonChart.tsx                          // Jan–Dec x-axis, one line per year, fading alpha
  PerformanceTable.tsx                                    // spreadsheet columns, newest first, warning/lock icons
  RecomputeDialog.tsx                                     // write permission only
  metrics.ts                                              // metric keys, Czech labels, colours, formatters
frontend/src/components/charts/comparisonColors.ts        // extracted from financial-overview/comparisonUtils.ts, shared
```

Toolbar: range presets 12/24/36 months, `includeWholesale` switch ("včetně velkoobchodu"),
view toggle "Vývoj" / "Meziroční srovnání", metric selector (PNO, ROAS, orders, revenue,
average order value, cost per order), last-refresh timestamp with a warning when any month
in view has `LastError` or a null `*ComputedAt`.

Table columns, in spreadsheet order: cost per channel, total cost, revenue with VAT, revenue
without VAT, PNO, ROAS, profit, orders, average order value, cost per order, YoY cost %,
YoY revenue %, YoY orders %. Czech number formatting. Empty state when no rows exist yet.
Light and dark mode via the theme hook as in `FinancialChart.tsx`.

## 8. Error handling summary

- Job: per-month `LastError`, never a whole-job failure unless every month failed.
- Settings: `ValidateOnStart`; misconfiguration fails deployment, never yields silent zeros.
- API: typed error codes with Czech messages; the generated client throws on non-200, so the
  frontend reads `errorCode` from the caught exception.
- Flexi: existing resilience policy; failures logged with month and filter.

## 9. Testing

- **Unit** — `MarketingMetricsCalculator` (every metric, zero divisors, missing prior year);
  channel bucketing (unmatched DIČ, storno, multiple IDs per channel); window and locking
  with a fixed `TimeProvider`; options validator.
- **Handlers** (InMemory provider) — both GET handlers: empty-row filling, `includeWholesale`,
  partial-month flag; recompute handler: range validation, already-running guard.
- **Adapter** — Flexi received-invoice search asserted against a captured verbatim JSON
  response containing `dic`, `storno`, `sumZklCelkem` (lesson from query 41: never a
  hand-built DTO). Filter string asserted verbatim.
- **Job** — MarketingPerformanceRefreshJob with mocked Flexi client: one month fails, others
  persist; locking applied.
- **Frontend** — hooks and `PerformanceTable` formatting; comparison transform; contexts
  mocked as in existing shell tests.
- **E2E** — `frontend/test/e2e/marketing/marketing-performance.spec.ts`: open page, switch to
  comparison, toggle wholesale, assert table re-renders. Requires one recompute on staging.
- **Gates** — `dotnet build`, `dotnet format`, `CI=false npm run build`, `npm run lint`,
  contract tests (`BaseResponse`, error-code range) green.

## 10. Rollout

1. FlexiBeeSDK change + release; verify the two Flexi assumptions read-only; document.
2. Backend: options, domain, persistence + migration, Flexi adapter method, job, handlers,
   controller, permission, error codes.
3. Frontend: hooks, page, charts, table, dialog, sidebar, route.
4. Apply migration to staging; grant permission; run recompute 2023-01 → current; verify
   against the spreadsheet for two months (expect the documented discrepancy only).
5. Production: migration, permission grant, backfill recompute.
