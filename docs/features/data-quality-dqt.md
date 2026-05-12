# Data Quality Tests (DQT) — Issued Invoice Comparison

## Overview

The DQT feature runs automated data quality checks that compare issued invoices between Shoptet and Abra Flexi for a defined time period. It surfaces mismatches on a dashboard tile and a dedicated `/data-quality` page, and runs automatically on a weekly Hangfire schedule.

## How it works

The comparison is a **bulk load → in-memory join**:

1. Fetch all issued invoices from Shoptet for the period → `List<IssuedInvoiceDetail>`
2. Fetch all issued invoices from Flexi for the period → `List<IssuedInvoiceDetail>`
3. Build lookup dictionaries keyed by `InvoiceCode`
4. Union all codes from both sides
5. For each code: flag missing invoices or price/item differences
6. Persist the run summary (`DqtRun`) and per-invoice results (`InvoiceDqtResult`)

Both sources are fully loaded before any comparison happens. If either fetch fails the entire run fails — no partial results are saved.

## Mismatch types

| Flag | Meaning |
|------|---------|
| `MissingInFlexi` | Invoice exists in Shoptet but not in Flexi |
| `MissingInShoptet` | Invoice exists in Flexi but not in Shoptet |
| `TotalWithVatDiffers` | Header total with VAT differs by more than 0.02 |
| `TotalWithoutVatDiffers` | Header total without VAT differs by more than 0.02 |
| `ItemsDiffer` | One or more line items differ in amount or unit price |

Item-level comparison matches lines by product code. Items without a product code (shipping, billing, discount lines) are skipped — they cannot be reliably matched cross-system.

## Schedule

- **Automatic**: every Monday at 23:00 CEST via Hangfire recurring job (`InvoiceDqtJob`)
- **Manual trigger**: `POST /api/data-quality/runs`

## API

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/data-quality/runs` | GET | Paginated list of DQT runs |
| `/api/data-quality/runs/{id}` | GET | Run detail with paginated per-invoice results |
| `/api/data-quality/runs` | POST | Manual trigger |

## Architecture

**Domain** (`Anela.Heblo.Domain/Features/DataQuality/`):
- `DqtRun` — run record (status, period, stats, trigger type)
- `InvoiceDqtResult` — per-invoice mismatch record
- `IDqtRunRepository` — persistence interface

**Application** (`Anela.Heblo.Application/Features/DataQuality/`):
- `InvoiceDqtComparer` — fetches both sources and produces `InvoiceDqtComparisonResult`
- `InvoiceDqtJobRunner` — orchestrates a full run: creates `DqtRun`, calls comparer, persists results, updates status
- `InvoiceDqtJob` — Hangfire `IRecurringJob` that schedules weekly runs
- Use cases: `GetDqtRuns`, `GetDqtRunDetail`, `RunDqt`

**Persistence** (`Anela.Heblo.Persistence/DataQuality/`):
- `DqtRunConfiguration` / `InvoiceDqtResultConfiguration` — EF Core table configs
- `DqtRunRepository` — EF Core implementation
- Migration: `20260424060451_AddDataQualityTables` (tables: `dqt_runs`, `invoice_dqt_results`)

**Adapter** (`Anela.Heblo.Adapters.Flexi/Invoices/`):
- `FlexiIssuedInvoiceClient.GetAllAsync` — fetches invoices from Flexi by date range via `Rem.FlexiBeeSDK.Client` v0.1.134+

**API** (`Anela.Heblo.API/Controllers/`):
- `DataQualityController`

**Frontend** (`frontend/src/`):
- `DataQualityStatusTile` — dashboard tile showing last run status and mismatch count
- `DataQualityPage` — full page at `/data-quality` with run history and drill-down

## Known constraints

- Flexi item `Code` field is not reliably preserved on read-back; `PriceList` (`code:PRODUCT-CODE`) is used as the authoritative product identifier instead.
- The weekly job compares the previous 7 days by default.
- Large date ranges (thousands of invoices) hold both full sets in memory simultaneously.
