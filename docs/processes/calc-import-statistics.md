---
process: calc-import-statistics
kind: calculation
module: analytics
summary: Daily counts of issued invoices and bank statements in Heblo's own database, used to spot days when the Shoptet→Flexi invoice import or the bank statement import did not run (statistics page and dashboard tile).
owns:
  - backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/**
  - backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/**
  - backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/Analytics/InvoiceImportOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs
verified_at: "5e993f9e2"
related: []
---

# Invoice and bank-statement import statistics

## Purpose
A health check, not a finance report. It answers "did the invoice import run every day, and did it
move a normal number of invoices?". The page **Import vydaných faktur**
(`/automation/invoice-import-statistics`, not in the sidebar; reached from the tile) shows a bar per day for the last 30 days (by default) and
highlights days below a threshold. The dashboard tile **Faktury importované včera** ("invoices
imported yesterday", Finance category, shown by default) shows yesterday's count and links to that
page. There is a matching bank-statement statistics endpoint, but no page uses it.

The invoices counted are the issued invoices that the Shoptet → Flexi invoice import (jobs
`daily-invoice-import-czk` and `-eur`, owned by the Invoices module) stores in Heblo's
`IssuedInvoices` table. The bank statements counted are the rows written by the bank statement
import (owned by the Bank module). Access to the page and its endpoints is
`finance.margin_analysis.read`. The tile requires no permission.

## Trigger
On demand:
- `GET /api/analytics/invoice-import-statistics?dateType=InvoiceDate|LastSyncTime&daysBack=` —
  the page. It sends no `daysBack`, so the configured default applies.
- `GET /api/analytics/bank-statement-import-statistics?startDate=&endDate=&dateType=StatementDate|ImportDate`
  — no frontend caller.
- Dashboard tile id `invoiceimportstatistics`, loaded with the dashboard.

## Data flow
1. **Invoices.** `GetInvoiceImportStatisticsHandler` takes the window as `[today UTC − daysBack,
   today UTC 00:00]` and calls `IssuedInvoiceRepository.GetDailyCountsAsync`. That query groups the
   `IssuedInvoices` rows by the calendar day of either `InvoiceDate` (the invoice's issue date) or
   `LastSyncTime` (when Heblo last tried to push it to Flexi), and counts them.
2. Each day is marked `IsBelowThreshold = count < InvoiceImport:MinimumDailyThreshold`.
   **Days with zero invoices are not returned at all.** The page draws only the days it receives.
3. **Tile.** `InvoiceImportStatisticsTile` asks for one day: the `date` parameter if the caller
   passes one, otherwise **UTC yesterday**. It groups by `LastSyncTime` and returns `count`, or 0
   when there is no row.
4. **Bank statements.** `GetBankStatementImportStatisticsHandler` defaults the window to the last 30
   days (UTC). `BankStatementImportRepository.GetDailyCountsAsync` groups `BankStatements` by
   `ImportDate` (the default) or `StatementDate`, returning the number of imports and Σ `ItemCount`
   per day. The adapter **fills missing days with zeroes**.

## Logic & formulas
- `InvoiceDate` is `timestamp without time zone`, and so is `LastSyncTime`. The UTC window boundaries are passed
  as `Kind=Unspecified`, so "a day" is the stored calendar day. `LastSyncTime` is written as UTC.
- The invoice window ends at today 00:00 (UTC), so today's syncs are not counted, apart from
  anything stamped exactly at midnight. Grouped by `InvoiceDate`, today is included only for invoices
  whose stored time is 00:00.
- Threshold: `InvoiceImport:MinimumDailyThreshold` (10) is returned in the response, and the page
  shows it.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `InvoiceImport:MinimumDailyThreshold` | 10 | A day with fewer invoices is flagged. |
| `InvoiceImport:DefaultDaysBack` | 30 (`appsettings.json`; the class default is 14) | Window used when the caller sends no `daysBack`. |

## Runtime facts
None.

## Known quirks
- **`LastSyncTime` is the time of the last attempt, not the first import.** It is overwritten on
  every sync attempt, including failed ones (`IssuedInvoice.SetLastSync`). A re-synced invoice moves
  to the re-sync day, and failed attempts are counted as "imported". The tile ("imported
  yesterday") inherits both effects.
- **Missing days are invisible on the invoice chart.** A day with no import returns no row instead
  of a 0 bar, so a gap shows up only as a missing date. The bank endpoint zero-fills; the invoice
  endpoint does not.
- **The tile's "yesterday" is a UTC day.** Between 00:00 and 01:00/02:00 Prague time it still
  reports the day before yesterday. The frontend dashboard does not appear to send a `date`
  parameter.
- **The bank-statement endpoint is unused.** It has no UI and is reachable only through the API.
- **The permission is borrowed.** The page is gated by the margin-analysis permission, not an
  invoices permission.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs` — window, threshold
- `backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs` — tile, UTC-yesterday fallback
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` (`GetDailyCountsAsync`) — the grouping query
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` — zero-fill
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` (`GetDailyCountsAsync`) — bank grouping
- `frontend/src/components/pages/automation/InvoiceImportStatistics.tsx` — the page
