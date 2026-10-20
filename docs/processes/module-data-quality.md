---
process: module-data-quality
kind: module
module: data-quality
summary: Nightly data-quality tests (DQT) that cross-check invoices, product pairing, stock write-back, material lots and retail prices between Heblo, Shoptet and Flexi, and report every mismatch on the Kvalita dat page and dashboard tiles.
owns: []
verified_at: "5e993f9e2"
related:
  - feed-stock-up
---

# Data quality (Kvalita dat)

## Purpose
Anela's data lives in three places that must agree: the Shoptet e-shop (orders, invoices,
retail prices, e-shop catalogue), ABRA Flexi (accounting, warehouse stock, lots, ERP prices)
and Heblo itself (stock movements it pushed to Shoptet). Nothing forces them to stay in
sync, so this module runs a fixed set of **data-quality tests (DQT)** every morning and
records each discrepancy it finds. It answers "can I trust yesterday's numbers?" for the
owner and for whoever fixes the data (missing invoice in Flexi, unpaired product, failed
stock push, lot that doesn't add up, price that differs between Shoptet and Flexi).

The module only **reads and compares**. It never corrects anything in Shoptet, Flexi or
Heblo; it stores a run record plus one row per finding in Heblo's own DB.

## Users & screens
- **Kvalita dat page** — route `/automation/data-quality` (sidebar *Administrace → Kvalita dat*), permission
  `Admin_DataQuality` (read; *Kvalita dat* in the access matrix). Shows summary cards for the
  latest run, the run history (`DqtRunsTable`) and the detail of a
  selected run with its findings (`DqtRunDetail`).
- **Spustit DQT button** (`RunDqtButton`, needs `Admin_DataQuality` **write**) — starts any of
  the five tests manually for a chosen date range: *Porovnání faktur*, *Párování produktů*,
  *Zpětný zápis skladu*, *Šarže vs. ERP sklad*, *Kontrola cen*.
- **Dashboard tiles** (category Data quality, enabled by default, not auto-shown, no extra
  permission):
  - `dataqualitystatus` *Kvalita dat* — latest invoice-comparison run: green/amber/red.
  - `dqtyesterdaystatus` *DQT včera* — the invoice-comparison run covering yesterday.
  - `pricecomparisonstatus` *Kontrola cen* — latest price-comparison run, plus "N bez ceny v
    Shoptetu"; drills down to `/products/pricing` instead of the DQT page.
- **API** — `GET /api/data-quality/runs`, `GET /api/data-quality/runs/{id}`,
  `POST /api/data-quality/runs` (`DataQualityController`). No MCP tool exposes DQT results.

## Processes
All five are Hangfire recurring jobs (category DataQuality, enabled by default, Europe/Prague),
deliberately staggered one hour apart:

| Doc | What it checks | Job id, cron |
|---|---|---|
| `calc-dqt-invoices` | Yesterday's issued invoices: Shoptet vs Flexi (presence, totals, lines) | `daily-invoice-dqt`, `0 5 * * *` |
| `calc-dqt-product-pairing` | Every Shoptet product maps to a sellable Flexi product and vice versa | `daily-product-pairing-dqt`, `0 6 * * *` |
| `calc-dqt-stock-writeback` | Yesterday's Heblo→Shoptet stock movements and stock-takings that failed or got stuck | `daily-stock-writeback-dqt`, `0 7 * * *` |
| `calc-dqt-lot-stock` | Sum of lots vs Flexi on-hand stock for materials with expiration | `daily-lot-stock-dqt`, `0 8 * * *` |
| `calc-dqt-price-comparison` | Retail price with VAT: Shoptet (source of truth) vs Flexi | `daily-price-comparison-dqt`, `0 9 * * *` |

The manual **Spustit DQT** action runs the same code for any of the five (no external writes,
so no separate workflow doc). Reading run lists/details is plain read-only CRUD.

## Data owned
All in schema `public` of the Heblo DB:
- `DqtRuns` — one row per test run: `TestType` (1 invoices, 2 product pairing, 3 stock
  write-back, 4 lot stock, 5 price), covered `DateFrom`/`DateTo`, `Status` (1 Running,
  2 Completed, 3 Failed), `TriggerType` (1 Scheduled, 2 Manual), `TotalChecked`,
  `TotalMismatches`, `ErrorMessage`, `StartedAt`/`CompletedAt` (UTC, `timestamp without time zone`).
- `InvoiceDqtResults` — one row per mismatching invoice of an invoice run (cascade-deleted
  with the run): invoice code, `MismatchType` bit flags, Shoptet/Flexi values, `Details` (≤ 4000 chars).
- `DqtDriftResults` — one row per finding of the four other tests: `EntityKey` (product
  code), `MismatchCode` (test-specific), `HebloValue`, `ShoptetValue`, `Details` (≤ 4000 chars).
  The two value columns mean different things per test — see each process doc.

Nothing is ever deleted or archived; rows accumulate indefinitely.

## External systems
Through other modules' adapters only (consumer-owned contracts in
`DataQuality/Contracts`, implemented by Invoices, Catalog and ProductPricing):
- **Shoptet** (read): `GET /api/invoices` + `/api/invoices/{code}` (invoices), the products CSV
  export at `StockClient:Url` (product pairing), `GET /api/pricelists/{id}` (prices).
- **Flexi** (read): issued invoices via the FlexiBee SDK, stock-to-date (`stav-skladu-k-datu`)
  for warehouse 4, user query 41 (ceník prices). Lot stock reads Flexi indirectly via the
  catalog cache.
- **Heblo DB** (read): `StockUpOperations`, `StockTakingRecords`.

## Dependencies
- Reads from: **Invoices** (Shoptet/Flexi invoice clients and the shared Shoptet invoice
  mapper), **Catalog** (catalog cache, e-shop/ERP stock clients, `StockUpOperations` and
  `StockTakingRecords` repositories, catalog resilience pipeline), **ProductPricing**
  (`PriceComparisonService`). It checks the output of `feed-stock-up` and of the Shoptet→Flexi
  invoice import (Invoices module).
- Read by: **Dashboard** (the three tiles). No other module consumes DQT results.

## Known quirks
- **A run can stay `Running` forever.** Nothing reaps stale runs. A manual run executes
  fire-and-forget (`Task.Run`) inside the web process, so an app restart mid-run strands it;
  and if the final `SaveChanges` itself fails (e.g. a `Details` string over 4000 characters on
  a heavily mismatched invoice), the status update is lost with the results. The *DQT včera*
  and *Kontrola cen* tiles show a Running run as amber. (Read from code, not observed.)
- **Three of the four drift tests ignore the date range.** Product pairing, lot stock and
  price comparison are snapshots of "now"; the dates on a manual run only label the run. Only
  invoices and stock write-back honour `DateFrom`/`DateTo`.
- **Adding a test type needs more than enum + comparer + job**: `GetDqtRunDetailHandler` has a
  per-type branch (a missing value throws `NotSupportedException` on opening the detail) and the
  frontend (`DqtRunDetail.tsx`, `DqtRunsTable.tsx`, `RunDqtButton.tsx`, `i18n.ts`) has per-type
  label maps. The frontend half was missed twice (2026-07-08 lot stock, 2026-09-10 price
  comparison — the detail then showed "Žádné neshody" over real mismatches) — repo note
  `memory/gotchas/dqt-new-test-type-checklist.md`.
- **Table-name drift once took the page down**: the tables were created snake_case
  (`dqt_runs`) and renamed to PascalCase by `StandardizeTableNamingToPascalCase`; a production
  DB/app mismatch produced HTTP 500s on `GET /api/data-quality/runs` (SQLSTATE 42P01).
  `DataQualitySchemaHealthCheck` now probes `DqtRuns` on `/health/ready` — repo note
  `memory/gotchas/ef-migration-codebase-drift.md`.
- **`docs/features/data-quality-dqt.md` is partly outdated**: it gives the tables as
  `dqt_runs`/`invoice_dqt_results` and the page as `/data-quality`; the real names are
  `DqtRuns`/`InvoiceDqtResults` and `/automation/data-quality`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` — DI: runners, comparers, tiles
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/` — the five recurring jobs
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs` — run lifecycle for invoices
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs` — run lifecycle for the four drift tests
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — manual trigger
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` — per-type result shaping
- `backend/src/Anela.Heblo.Domain/Features/DataQuality/` — `DqtRun`, result entities, mismatch enums
- `backend/src/Anela.Heblo.Persistence/DataQuality/` — tables, `DqtRunRepository`
- `backend/src/Anela.Heblo.API/Controllers/DataQualityController.cs` — API + permissions
- `backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs` — schema probe
- `frontend/src/pages/customer/DataQualityPage.tsx`, `frontend/src/components/data-quality/` — UI
