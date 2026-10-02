---
process: module-invoices
kind: module
module: invoices
summary: Moves the e-shop's issued invoices (vydané faktury) from Shoptet into ABRA Flexi every night and keeps a per-invoice log of what was sent and whether Flexi accepted it.
owns: []
verified_at: "5e993f9e2"
related:
  - feed-issued-invoices
---

# Issued invoices (Vydané faktury)

## Purpose
Customers' invoices are created by **Shoptet** when an e-shop order is invoiced. Anela's
accounting, VAT returns and stock write-off live in **ABRA Flexi**, so every Shoptet invoice must
also exist in Flexi as an issued invoice that issues the goods from the `ZBOZI` warehouse. This
module does that copy (nightly, plus manual re-runs) and keeps a log so that someone in finance
can see which invoices did not make it into Flexi and why, fix the cause and re-import them.

Its table of invoices is also Heblo's only local list of issued invoices, so other modules use it
as a cheap source of "how many invoices / how much revenue per day or month".

## Users & screens
| Screen | Route | What it does |
|---|---|---|
| Vydané faktury (sidebar) | `/customer/issued-invoices` | Paged grid of `IssuedInvoices` with filters (invoice number, customer, date range, only unsynced, only with errors) and sorting; sync statistics tiles (default last 30 days); **Import** dialog (by date range or invoice number); running-imports box; second tab with the import-statistics chart. Route permission `customer.bank_statements.read`. |
| Invoice detail modal | (from the grid) | Header, last error, full sync history with the JSON sent to Flexi and Flexi's raw response; **Znovu importovat** re-imports that one invoice. |
| Invoice import statistics | `/automation/invoice-import-statistics` | Daily count of invoices by invoice date or by last sync time (Analytics module chart over this table). Route permission `finance.margin_analysis.read`. |

API: `GET /api/invoices` (list), `GET /api/invoices/{id}` (detail, `withDetails` adds sync
history), `GET /api/invoices/stats`, `POST /api/invoices/import/enqueue-async`,
`GET /api/invoices/import/job-status/{jobId}`, `GET /api/invoices/import/running-jobs`.
No MCP tool reads this module.

## Processes
- `feed-issued-invoices` — Shoptet → Flexi invoice import with all transformation rules. Hangfire
  `daily-invoice-import-eur` (`0 4 * * *`) and `daily-invoice-import-czk` (`15 4 * * *`), both for
  yesterday; plus the manual import / re-import from the page.

Plain reads (no process doc): the invoice list, detail, sync statistics
(`GetIssuedInvoiceSyncStats`: total / synced / unsynced / with errors / critical / last sync
time / success rate by `InvoiceDate`) and job-status polling.

## Data owned
- `public."IssuedInvoices"` — one row per Shoptet invoice, key `Id` = Shoptet **order code**
  (also the Flexi `kod`). Header data from Shoptet (`InvoiceDate` = creation time, `TaxDate`,
  `DueDate`, `VarSymbol`, `BillingMethod`, `ShippingMethod`, `VatPayer`, `ItemsCount`,
  `CustomerName`, `Currency`, `Price` = total **with VAT** in the invoice currency; `PriceC`
  unused, always 0) and the result of the **last** sync attempt (`IsSynced`, `LastSyncTime` UTC,
  `ErrorMessage`, `ErrorType`, `SyncHistoryCount`).
- `public."IssuedInvoiceSyncData"` — one row per sync attempt (`IssuedInvoiceId`, `SyncTime`,
  `IsSuccess`, `Data` = JSON of the invoice as sent, `AdapterResponse` = Flexi's reply,
  `Error_*` columns).
- Memory cache key `invoices:running-import-jobs` (running/pending manual import jobs, 2 s).

## External systems
| System | Direction | What |
|---|---|---|
| Shoptet REST API | read | `GET /api/invoices` (list by `creationTimeFrom`/`creationTimeTo`, 50 per page) and `GET /api/invoices/{code}` (detail); token header `Shoptet-Private-API-Token` |
| ABRA Flexi | write | Evidence `faktura-vydana` via FlexiBee SDK `IIssuedInvoiceClient.SaveAsync(…, unpairIfNecessary: true)` — upsert by `code:{orderCode}`; may unpair existing bank / cash-register payments first |
| ABRA Flexi | read | `IIssuedInvoiceClient.GetAllAsync(from, to)` — only for the DataQuality invoice comparison |
| Hangfire | write | Two recurring jobs; manual imports as fire-and-forget jobs `Import faktur: …` |

## Dependencies
Reads from no other Heblo module. Other modules read from it through adapters registered in
`InvoicesModule`:
- **PackingMaterials** — `IInvoiceConsumptionSource`: yesterday's invoice ids and `ItemsCount`
  for job `daily-consumption-calculation` (06:00, scheduled after both imports).
- **MarketingPerformance** — `IMonthlyRevenueSource` (`IssuedInvoiceMonthlyRevenueSource`):
  monthly CZK revenue with VAT by `TaxDate`, retail vs. wholesale by `VatPayer`.
- **Analytics** — `IInvoiceImportStatisticsSource`: daily invoice counts for the chart.
- **DataQuality** — `IInvoiceShoptetSource` and `IInvoiceErpClient`: job `daily-invoice-dqt`
  (05:00) compares yesterday's Shoptet invoices with Flexi item by item (it does not read
  `IssuedInvoices`).

## Known quirks
- ~20 % of rows per month are `IsSynced = false` (live, 2026-09-16) and nothing retries them
  automatically; only a manual re-import fixes them. Counting invoices in Heblo is not the same
  as counting them in Flexi.
- `Price` is with VAT and in the invoice's own currency; `PriceC` is always 0. Anything summing
  revenue must filter by `Currency` and divide out VAT itself.
- The page's date-range import ignores the chosen dates and currency (it sends read-only
  `dateFromString`/`dateToString` and no `currency`), so it always imports CZK invoices from
  UTC-yesterday to now — see `feed-issued-invoices`.
- Single-invoice re-import passes the order code to Shoptet's invoice-detail endpoint; it works
  only while order and invoice numbers coincide.
- Error types `InvoicePaired` / `ProductNotFound` are never assigned, so "critical errors" on the
  statistics tiles always equals "invoices with errors".
- The page route reuses permission `customer.bank_statements.read`; there is no invoice-specific
  permission.
- Dead code from the replaced Playwright/Pohoda import (#639): `ImportInvoiceRequestDto`,
  `IInvoicePriceCalculator`, `IPaymentMethodResolver`, `IShippingMethodResolver`, the no-op
  "remove trailing D" transformation, and the unused `Shoptet:InvoiceShippingGuidMap` mapper.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — DI, transformation order, cross-module adapters
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — the import pipeline
- `backend/src/Anela.Heblo.API/Controllers/InvoicesController.cs` — API surface
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — list filters, stats, daily counts
- `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs` — entity, sync history, critical-error rule
- `frontend/src/pages/customer/IssuedInvoicesPage.tsx` — page and import dialog
