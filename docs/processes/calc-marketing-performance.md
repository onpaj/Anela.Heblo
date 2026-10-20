---
process: calc-marketing-performance
kind: calculation
module: marketing-performance
summary: Monthly snapshot of advertising spend (Flexi received invoices from Meta, Google and Seznam) against e-shop orders and revenue (issued invoices), with PNO, ROAS and year-over-year ratios for the Marketing → Analýzy page.
owns:
  - backend/src/Anela.Heblo.Application/Features/MarketingPerformance/**
  - backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/MarketingPerformance/**
  - backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs
  - backend/src/Anela.Heblo.Persistence/Marketing/MarketingPerformance*.cs
  - backend/src/Anela.Heblo.API/Controllers/MarketingPerformanceController.cs
verified_at: "5e993f9e2"
related: [sync-flexi-analytics]
---

# Marketing performance snapshot (ad spend vs. revenue)

## Purpose
Answers "how much did advertising cost us this month, and what did the e-shop sell for it?".
It replaces the owner's hand-kept spreadsheet `Naklady_reklamy.xlsx`. For every calendar month
Heblo stores:

- **ad cost per channel** (Náklady na reklamu) — FB/IG (Meta), Google, S-Klik (Seznam), without VAT;
- **orders and revenue** (Objednávky, Tržby) — retail and wholesale separately.

From these sums the page derives **PNO** (podíl nákladů na obratu — ad cost as % of revenue,
lower is better), **ROAS** (revenue per crown of ad spend, in %), average order value
(Průměrná objednávka), cost per order (Cena za nákup), profit (revenue − ad cost; not a real
profit, only "after ads") and year-over-year (r/r) percentages.

Consumers: page Marketing → **Analýzy** (`/marketing/performance`) and its API
`GET /api/MarketingPerformance/months`, `GET /api/MarketingPerformance/comparison`. No MCP tool
reads it. The same ad spend is independently available to Metabase as
`flexi_raw.v_ad_spend_monthly` (from the ledger, see `sync-flexi-analytics`).

## Trigger
1. **Scheduled refresh** — Hangfire recurring job `marketing-performance-refresh`
   (`MarketingPerformanceRefreshJob`, category Marketing, display name "Marketing — výkon reklamy
   (měsíční snapshot)"), cron `0 5 * * *` = **05:00 daily, Europe/Prague**, enabled by default.
   Recomputes the **current and previous month** (`RecomputeWindowMonths` = 2), then locks every
   older month. `[AutomaticRetry(Attempts = 1)]`, `[DisableConcurrentExecution(600)]`.
   Can be disabled or re-scheduled on the Recurring Jobs page (Naplánované úlohy).
2. **Manual recompute** — button "Přepočítat" on the Analýzy page (needs permission
   `marketing.performance.write`, i.e. `Marketing_Performance` Write) opens a dialog with
   Od/Do months → `POST /api/MarketingPerformance/recompute` `{from, to}` ("yyyy-MM").
   The handler validates the range, reserves the run guard and enqueues the fire-and-forget
   Hangfire job `MarketingPerformanceRecomputeJob.RunAsync(fromYear, fromMonth, toYear, toMonth)`;
   it returns **202** with the Hangfire job id and month count. The job recomputes every month in
   the range **regardless of locks**. `[AutomaticRetry(Attempts = 0)]`,
   `[DisableConcurrentExecution(300)]`. Progress is visible only on the Recurring Jobs / Hangfire
   pages; the dialog says "queued", never "done".

Reading the page never recalculates; it only reads the two tables.

## Data flow
Per month `M` (`MarketingPerformanceRefreshService.RefreshMonthAsync`), in this order:

1. **Load or create** the row in `public."MarketingPerformanceMonths"` for (Year, Month), with its
   child rows in `public."MarketingPerformanceChannelCosts"`.
2. **Revenue step** — `IssuedInvoiceMonthlyRevenueSource` (Invoices module implements the
   contract) runs two queries on Heblo's own `public."IssuedInvoices"` table (the e-shop invoices
   imported by the Invoices module):
   - `Currency = 'CZK'` and `TaxDate` (DUZP) in `[1st of M, 1st of M+1)`, grouped by
     `VatPayer == true` → count + `SUM(Price)` per group;
   - count of `Currency = 'EUR'` invoices in the same window → `SkippedEurInvoiceCount`.
   Writes `RetailOrderCount`, `RetailRevenueWithVat`, `WholesaleOrderCount`,
   `WholesaleRevenueWithVat`, `SkippedEurInvoiceCount`, `RevenueComputedAt` (UTC now).
3. **Cost step** — `FlexiMonthlyAdCostSource` → `IReceivedInvoicesClient.SearchByVatIdsAsync`
   → **one** ABRA Flexi call per month: received invoices (`faktura-prijata`, via
   `POST /c/{company}/faktura-prijata/query`) with accounting date `datUcto` from the 1st to the
   last day of M and supplier DIČ (`dic`) in the configured VAT IDs. Each invoice is mapped to
   `AdCostInvoice` (number, `dic`, `datUcto`, `sumZklCelkem` = base amount without VAT,
   `storno`).
4. **Bucketing** — `ChannelCostBucketer.Bucket`: drop storno invoices, assign each remaining
   invoice to the channel whose `VatIds` contains its DIČ (case-insensitive), sum
   `AmountWithoutVat` and count invoices. Always produces **one row per configured channel**
   (0 when no invoice). DIČs matching no channel are only counted and logged (the values are not
   logged — a Czech DIČ can be derived from a personal birth number).
5. Old channel rows of the month are replaced by the new ones; `CostsComputedAt` = UTC now.
6. `LastError` = joined step errors or `null`; `SaveChanges`. A failed save is logged, the row and
   its children are detached from the DbContext, and the loop continues with the next month.
7. Scheduled refresh only: `LockMonthsBeforeAsync(windowStart)` sets `IsLocked = true` on every
   unlocked month older than the window.

Read path (`GetMarketingPerformanceMonthsHandler`, `GetMarketingPerformanceComparisonHandler`):
`GetRangeAsync` loads the requested months **plus 12 months earlier** (for r/r) →
`MarketingMetricsCalculator.Build` derives every ratio → DTO. Months without a row come back as
`HasData = false`.

## Logic & formulas
**Units.** Revenue is stored **with VAT** (`IssuedInvoices.Price` is the VAT-inclusive total;
`PriceC` is never populated). Costs are stored **without VAT** (Flexi `sumZklCelkem`).
All money is CZK; EUR issued invoices are excluded from revenue and only counted. Amounts are
`numeric(18,2)`; ratios are not rounded on the server.

**Retail vs. wholesale.** Wholesale (velkoobchod) = the invoice's customer is a VAT payer
(`VatPayer == true`) — the same rule Flexi sales user query 37 uses. `VatPayer` `false` or `null`
= retail. By default the page shows **retail only**; the switch "včetně velkoobchodu"
(`includeWholesale=true`) adds the wholesale count and revenue at read time.

**Orders.** One issued-invoice row = one order. There is no filter on document type.

**Derived per month** (`MarketingMetricsCalculator`), with `Orders` and `RevenueWithVat` per the
wholesale switch, `TotalCost` = sum of all channel rows of the month:

```
RevenueWithoutVat = RevenueWithVat / VatRate          (VatRate = 1.21, flat)
PNO               = TotalCost × 100 / RevenueWithoutVat
ROAS              = RevenueWithoutVat × 100 / TotalCost
Profit            = RevenueWithoutVat − TotalCost
AvgOrderValue     = RevenueWithoutVat / Orders
CostPerOrder      = TotalCost / Orders
Yoy*Percent       = this month / same month last year × 100   (cost, revenue with VAT, orders)
```
Any division by 0 (or a missing / zero prior-year month) gives `null`, shown as "—".

**Channel list.** Channels are shown in configured order; a channel without a stored row shows
0. Stored rows whose code is no longer configured are still appended (label = code) so nothing
disappears silently; rows differing only by letter case are summed.

**Partial month.** The current month is flagged `IsPartial` ("(probíhá)" in the table). The
charts hide it by default (checkbox), because a few days of revenue against a whole invoice or a
credit note distorts the ratios.

**Year comparison** (`/comparison`, view "Meziroční srovnání"): 2–3 calendar years (clamped),
Jan–Dec each. YTD card per year sums the months with data; for past years only months
`<= current month number` count, for the current year every month with data (including the
running one). `YtdPno = YtdTotalCost × 100 / YtdRevenueWithoutVat`.

**Range rules** (`MonthRangeParser`): "yyyy-MM", `from <= to`, nothing after the current month,
nothing before **2020-01**. Months view: default last 36 months, max 60 per request. Recompute:
max `MaxRecomputeRangeMonths` (60). Errors: `MarketingPerformanceInvalidMonthRange` (3701),
`MarketingPerformanceRangeTooLarge` (3702), `MarketingPerformanceRecomputeAlreadyRunning` (3703,
HTTP 409), `MarketingPerformanceEnqueueFailed` (3704).

**Locking.** "Locked" means only that the month left the 2-month window: the scheduled job never
iterates older months anyway. A manual recompute rewrites locked months and leaves them locked.

**One run at a time.** `MarketingPerformanceRunGuard` is a process-wide in-memory flag. The
recompute handler takes it before enqueueing and the recompute job releases it when finished.
The scheduled job takes it at start; if it is taken, the job logs a warning and **returns
successfully without doing anything**.

**Failure handling.** Revenue and cost steps fail independently; a failed step keeps the
month's previous values and writes `LastError` (e.g. `Revenue: step failed (TimeoutException);
see server logs`). A job run is marked Failed in Hangfire only when **every** month failed both
steps.

## Configuration
Section `MarketingPerformance` in `backend/src/Anela.Heblo.API/appsettings.json`, validated at
startup (`MarketingPerformanceOptionsValidator` — app does not start on invalid values).

| Key | Repo default | Meaning |
|---|---|---|
| `MarketingPerformance:RecomputeWindowMonths` | `2` | Months (current included) the daily job recomputes; older months get locked. ≥ 1 |
| `MarketingPerformance:VatRate` | `1.21` | Divisor turning with-VAT revenue into without-VAT revenue. > 1 |
| `MarketingPerformance:CronExpression` | `0 5 * * *` | Seed cron of `marketing-performance-refresh` (Europe/Prague); see quirks |
| `MarketingPerformance:MaxRecomputeRangeMonths` | `60` | Max months per manual recompute; 1–120 |
| `MarketingPerformance:Channels` | `meta` "FB/IG" `IE9692928F`; `google` "Google" `IE6388047V`; `sklik` "S-Klik" `CZ26168685` | Channel code, label and supplier DIČs. ≥ 1 channel, each with ≥ 1 VAT ID, no duplicate code, no VAT ID in two channels |

Hangfire job state (enabled, cron override) lives in the recurring-job configuration table and is
edited on the Recurring Jobs page.

## Runtime facts
- Flexi query for August 2026 filtered to the three configured VAT IDs returned 18 invoices;
  manual totals Meta 307 520.35, Google 113 366.42, **Seznam: no invoice under its VAT ID** —
  `docs/features/marketing-performance.md` — 2026-09-18.
- Invoice-based order counts and revenue run 5–15 % **above** the owner's old spreadsheet
  (Shoptet statistics); accepted, the app is the definition now — `docs/features/marketing-performance.md`,
  agent memory `gotcha_issued_invoice_price_is_with_vat` — 2026-09-16.
- Cross-check against the ledger (`flexi_raw.v_ad_spend_monthly`) for 2026-01 … 2026-09: Meta
  and Google agree to the haléř every month; **S-Klik is wrong in four months and stored
  negative** (−7 350.00, −18 763.50, −22 690.50 where the ledger shows 35 000.00, 89 350.00,
  108 050.00) — `docs/architecture/metabase.md` — 2026-09-22.

## Known quirks
- **S-Klik cost can be negative / too low** (2026-09-22, see Runtime facts). Cause not fixed.
  Likely shape: Seznam spend is invoiced under a supplier record whose `dic` is not
  `CZ26168685` (the ledger shows two counterparty spellings), so the DIČ filter catches only the
  credit notes (dobropisy, negative `sumZklCelkem`). Until fixed, trust
  `flexi_raw.v_ad_spend_monthly` for S-Klik.
- **Credit notes are summed in** on both sides. Received credit notes are not storno, so they
  reduce the channel cost of the month they are booked in; an issued credit-note row counts as an
  order and its amount is summed into revenue. A month with only a credit note can show negative
  cost or odd ratios.
- **Cost month = accounting date (`datUcto`)**, not the period the ads ran. Invoices near the
  month boundary land in a different month than in the owner's spreadsheet.
- **VAT is a flat 1.21.** Revenue without VAT is `Price / 1.21` for every invoice, including
  items sold at reduced rates and wholesale invoices; the true without-VAT revenue is not stored
  anywhere (`PriceC` is always 0).
- **EUR revenue is ignored**, only counted in `SkippedEurInvoiceCount`. EUR-paid ad invoices are
  still counted, because Flexi's `sumZklCelkem` is the domestic (CZK) amount.
- **Changing `CronExpression` in config does not reschedule an existing environment.** The
  recurring-job seeder writes the cron only when the job row is first created and afterwards
  preserves the stored (admin) value; change it on the Recurring Jobs page instead.
- **Stuck run guard.** The guard is an in-memory flag released only when the recompute job
  finishes. If an accepted recompute job never runs (deleted from the Hangfire queue, or still
  waiting behind other jobs — Hangfire runs one worker by default, `HangfireOptions.WorkerCount` = 1), every further recompute is rejected
  with 409 and the daily refresh silently skips (still reported Succeeded) until the job runs or
  the app restarts.
- **Daily refresh skipped by a recompute is invisible.** It returns success after logging
  "another marketing performance run is active", so Hangfire shows Succeeded with no data change.
- **"Locked" protects nothing by itself** — the daily job never touches months outside the
  window; the flag is informational.
- **Year-comparison YTD is not like-for-like for the current month**: past years include the
  full month with the current month number, the current year only its days so far.
- **Overriding `Channels` with fewer entries via Key Vault / env vars merges index-wise** with
  the `appsettings.json` array instead of replacing it (.NET config binder), so removed channels
  survive. Change the channel list in `appsettings.json`.
- **If the Flexi adapter were not registered**, `NoOpMonthlyAdCostSource` would return no
  invoices; the month then gets 0 cost, no `CostsComputedAt` and `LastError` "Costs: ad-cost
  source is not configured…", which the page shows as a warning. In every current environment
  the Flexi source is registered (it overrides the no-op because `AddFlexiAdapter` runs after
  `AddApplicationServices` in `Program.cs`).
- `docs/features/marketing-performance.md` → "Limitations → Recompute race" is outdated: the
  handler now reserves the guard with `TryBegin` before enqueueing, so a second concurrent request
  gets 409 instead of a silent no-op job.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/MarketingPerformanceRefreshService.cs` — per-month refresh, window, locking, error handling
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Infrastructure/Jobs/MarketingPerformanceRefreshJob.cs` — daily Hangfire job
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Infrastructure/Jobs/MarketingPerformanceRecomputeJob.cs` — manual recompute job
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/UseCases/RecomputeMarketingPerformance/RecomputeMarketingPerformanceHandler.cs` — validation, run guard, enqueue
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs` — revenue/order queries
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/MarketingPerformance/FlexiMonthlyAdCostSource.cs` — Flexi received-invoice cost source
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoicesClient.cs` — `SearchByVatIdsAsync` (datUcto + dic filter)
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/ChannelCostBucketer.cs` — DIČ → channel assignment
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/MarketingMetricsCalculator.cs` — PNO, ROAS, r/r formulas
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Configuration/MarketingPerformanceOptionsValidator.cs` — startup config rules
