# Výkon reklamy (Marketing Performance)

Monthly snapshot of ad spend vs. e-shop orders/revenue, replacing the manual `Naklady_reklamy.xlsx`.
Spec: `docs/superpowers/specs/2026-09-18-marketing-performance-design.md`.
Plan: `docs/superpowers/plans/2026-09-18-marketing-performance.md`.

> **Ad costs are not flowing yet.** The FlexiBeeSDK change that lets the app query received
> invoices by VAT ID is committed locally but not published to NuGet, so the real Flexi cost
> adapter is not registered. `IMonthlyAdCostSource` currently resolves to
> `NoOpMonthlyAdCostSource`, which returns an empty list — **every channel cost reads zero in
> every environment right now.** Revenue and order counts (from `IssuedInvoices`) work today and
> are unaffected. Costs start flowing once the SDK package is published, the real adapter is
> registered in `MarketingPerformanceModule`, and the recompute/refresh job runs again. See
> "Known discrepancy vs. the spreadsheet" below for what the cost numbers look like when queried
> directly against Flexi, and Operations for the outstanding rollout steps.

## Data

- **Costs**: ABRA Flexi received invoices, one REST call per month (`datUcto` in month, `dic in (…)`), bucketed to
  channels by supplier DIČ from `MarketingPerformance:Channels`. `storno` skipped, `sumZklCelkem` summed (without
  VAT).
- **Revenue/orders**: `IssuedInvoices` (Shoptet-synced), CZK only, by `TaxDate`. `Price` is with VAT; without-VAT =
  ÷ `VatRate` (1.21). Wholesale = `VatPayer` (customer has VAT ID), same rule as Flexi sales query 37. EUR invoices
  are counted (`SkippedEurInvoiceCount`) and excluded.
- Tables: `MarketingPerformanceMonths`, `MarketingPerformanceChannelCosts` (sums only; PNO/ROAS/averages/YoY
  derived at read time by `MarketingMetricsCalculator`).

## Fetching Flexi costs — read this before touching the adapter

Ad costs are fetched by **`POST /c/{company}/faktura-prijata/query`** — note the `/query` segment. The SDK's
`FlexiQuery.IncludeQuerySegment` defaults to `true` and adds it automatically; that's what makes the filter (month
+ the three supplier VAT IDs) actually apply. Verified live for August 2026: filtered to the three configured VAT
IDs, this returned 18 rows.

Two traps to never reintroduce:

- **A POST to the bare evidence URL (`/faktura-prijata.json`, no `/query`) is a WRITE/import, not a query** — it
  will attempt to create a record, not filter existing ones.
- **A `?filter=` GET query-string is silently ignored.** It returns HTTP 200 with the entire ~7,660-row table, no
  error, no indication the filter was dropped. This is the single easiest way to accidentally reintroduce a
  full-table scan against a live, sandbox-less ERP.

See `docs/integrations/flexibee-api.md` for the rest of the FlexiBee integration notes (that doc did not yet
cover the query-vs-write and filter-vs-query-string distinctions above; add to it, don't duplicate, if it's
revisited).

## Configuration

Supplier VAT IDs are configured and verified, not placeholders: Meta `IE9692928F`, Google `IE6388047V`, Seznam
`CZ26168685` — all confirmed against live production data and already present in `appsettings.json`'s
`MarketingPerformance:Channels` section. Startup fails on duplicate/missing VAT IDs
(`MarketingPerformanceOptionsValidator`).

## Job

`marketing-performance-refresh` (Hangfire, default 05:00 daily). Window = current + previous month
(`RecomputeWindowMonths`, default 2). Older months are locked; `POST /api/MarketingPerformance/recompute` (write
permission) recomputes any range ≤ 60 months regardless of locks. Per-month failures are recorded in `LastError`;
the job fails only if every month failed.

## Screen

Marketing → Výkon reklamy (`/marketing/performance`), permission `Marketing_Performance` (read; write for
recompute). Views: Vývoj (stacked channel costs + one metric line), Meziroční srovnání (Jan–Dec, one line per
year, hover shows all years for the month). Switch "včetně velkoobchodu" adds wholesale at read time.

## API routes

`/api/MarketingPerformance/months`, `/api/MarketingPerformance/comparison`, `/api/MarketingPerformance/recompute`
— ASP.NET's `[controller]` token preserves the controller class's casing (`MarketingPerformanceController`), so
these are the real routes, not all-lowercase. Routing itself is case-insensitive, so the frontend client and any
manual calls work regardless of case — this is purely a "what will you actually see in logs/Swagger" note.

## Known discrepancy vs. the spreadsheet

Invoice-based numbers run 5–15% above the spreadsheet's order counts (which came from Shoptet statistics).
Accepted; the app is the definition from now on.

A second, separate discrepancy shows up on the cost side. **These are manual-verification figures, not numbers
the running application currently produces** — see the note at the top of this document: the running app's cost
side is still zero everywhere. For August 2026, a manual query run directly against Flexi on 2026-09-18 gave Meta
307 520.35 and Google 113 366.42, while **Seznam had no invoice at all under its VAT ID that month**. The
spreadsheet's August row reads FB/IG 371 564.6, Google 122 366, S-Klik 11 635 — same ballpark, not equal. This is
expected: the app filters on accounting date (`datUcto`), while the spreadsheet reflects the owner's own spend
accounting, so invoices near a month boundary land in different months for the two sources. Seznam's complete
absence is worth the owner looking into, though — it suggests Seznam may be invoiced on a different cycle, or
under a different supplier record than the configured VAT ID.

## Limitations

- **Recompute race.** `RecomputeMarketingPerformanceHandler` checks "is a run already in progress"
  (`MarketingPerformanceRunGuard.IsRunning`) before queueing the Hangfire job, but the guard's actual lock
  (`TryBegin`) is only taken inside `MarketingPerformanceRecomputeJob.RunAsync` when the job starts executing. Two
  rapid `POST /recompute` requests can therefore both pass the check and both get accepted; whichever job starts
  second then takes the lock, fails `TryBegin`, logs a skip, and does nothing. The operator sees a queued job id
  either way, with no indication that the second one is a no-op. The recompute dialog on the screen deliberately
  reflects this: it says only that the job was **queued** and points at the jobs page — it never claims the job
  finished.

## Operations

- Backfill: run the recompute 2023-01 → current month once after deploying to each environment.
- Grant `marketing.performance.read`/`.write` in `/admin/access` — seed groups don't update existing environments.
- The Flexi cost adapter is not wired yet (see `memory/context/state.md`): `IMonthlyAdCostSource` currently
  resolves to `NoOpMonthlyAdCostSource`, so ad costs read as zero in every environment until the FlexiBeeSDK
  package (VAT-ID filter + `dic` projection) is published to NuGet and the real adapter is registered.

### Validation gates

- `dotnet build` and the backend test suite (`backend/test/Anela.Heblo.Tests`).
- `CI=false npm run build` from `frontend/` — this DOES pass and is the genuine type gate for this feature,
  stricter than a plain `tsc` check.
- `npx eslint` scoped to the files a change touches, not repo-wide `npm run lint`. On a clean checkout,
  repo-wide `npm run lint` exits non-zero because of roughly 236 pre-existing errors in unrelated files — that
  failure predates this feature and is not something a change here can or should fix. "Lint is clean" for this
  feature means scoped-clean, not repo-clean.
