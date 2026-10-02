---
process: calc-dqt-invoices
kind: calculation
module: data-quality
summary: Every morning compares yesterday's issued invoices in Shoptet against Flexi — missing on either side, header totals with/without VAT, and per-product lines — and stores each mismatching invoice in InvoiceDqtResults.
owns:
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IInvoiceDqtComparer.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IInvoiceDqtJobRunner.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs
  - backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs
  - backend/src/Anela.Heblo.Domain/Features/DataQuality/InvoiceDqtResult.cs
  - backend/src/Anela.Heblo.Domain/Features/DataQuality/InvoiceMismatchType.cs
  - backend/src/Anela.Heblo.Persistence/DataQuality/InvoiceDqtResultConfiguration.cs
verified_at: "5e993f9e2"
related: []
---

# Invoice comparison Shoptet vs Flexi (Porovnání faktur)

## Purpose
Every invoice Shoptet issues must also exist in Flexi (accounting) with the same amounts —
Heblo's invoice import (Invoices module) copies them over. This check catches invoices the
import missed or wrote differently: an invoice missing in Flexi means revenue and VAT are
under-reported; one missing in Shoptet means something was entered or imported twice. Results
appear on the **Kvalita dat** page (`/automation/data-quality`, test *Porovnání faktur*) and
on the dashboard tiles *Kvalita dat* (latest invoice run) and *DQT včera* (the run that
covers yesterday).

## Trigger
- Hangfire recurring job `daily-invoice-dqt` (`InvoiceDqtJob`), cron `0 5 * * *`
  (05:00 Europe/Prague), enabled by default; skipped when disabled in Recurring Jobs.
  Covers **yesterday** only (`DateFrom = DateTo = UTC date − 1 day`).
- Manual: **Spustit DQT** → *Porovnání faktur* with any date range
  (`POST /api/data-quality/runs`, `TestType = IssuedInvoiceComparison`). The run starts in
  the background; the request returns its id immediately.

## Data flow
1. Create a `DqtRun` row (`TestType` 1, status Running) in `public."DqtRuns"`.
2. **Shoptet** (`InvoiceShoptetSourceAdapter` → `ShoptetApiInvoiceSource`):
   `GET /api/invoices?creationTimeFrom={from}T00:00:00Z&creationTimeTo={to+1}T00:00:00Z`
   (50 per page, all pages), keep only invoices whose currency is **CZK** (the query's default
   `Currency`), then `GET /api/invoices/{code}` for each one and map it with
   `ShoptetInvoiceMapper` — the same mapper the Shoptet→Flexi invoice import uses.
3. **Flexi** (`InvoiceErpClientAdapter` → `FlexiIssuedInvoiceClient.GetAllAsync`): all issued
   invoices for `from 00:00` – `to 23:59:59.9999999` through the FlexiBee SDK (no currency filter).
4. Both lists are mapped to `DqtInvoiceSnapshot` (code, totals, lines) and joined by invoice
   code (`InvoiceDqtComparer`).
5. Each invoice with at least one finding becomes a row in `public."InvoiceDqtResults"`;
   the run is set Completed with `TotalChecked` = distinct codes on both sides and
   `TotalMismatches` = mismatching invoices. Any exception → run Failed with the message, no results.

## Logic & formulas
- **Invoice code** = the Shoptet *order* code (`orderCode`), which is what the import stores
  as the Flexi invoice code.
- **Header totals** (CZK, tolerance **0.02**, absolute):
  - Shoptet *with VAT*: the header amount (`toPay`, or `withVat` when `toPay` is 1 Kč or more
    away from it) when it is within 1 Kč of the sum of non-zero-VAT product lines (absorbs the
    "Zaokrouhlení" rounding line), otherwise that line sum.
    Shoptet *without VAT*: sum of product lines.
  - Flexi *with VAT*: the invoice header total (`SumTotal`); *without VAT*: sum of the lines'
    base amounts (`SumBase`, falling back to `SumBaseC`).
- **Lines** are matched by product code; lines without a code (shipping, payment, unnamed
  discounts) are skipped. Shoptet discount lines of type `discount-coupon`, `volume-discount`
  and `gift` are first spread over the product lines in proportion to their price, and
  per-line `priceRatio` discounts are folded into the unit price. Flexi's line code is `kod`,
  falling back to the ceník reference (`code:XXX` → `XXX`).
  Per line: quantity must be **equal**; unit price with VAT and unit price without VAT must
  agree within **0.02**.
- **Mismatch flags** (`InvoiceMismatchType`, combinable bit flags):

  | Flag | Value | Meaning |
  |---|---|---|
  | `MissingInFlexi` | 1 | Code in Shoptet only |
  | `MissingInShoptet` | 2 | Code in Flexi only |
  | `TotalWithVatDiffers` | 4 | Header total with VAT differs > 0.02 |
  | `TotalWithoutVatDiffers` | 8 | Header total without VAT differs > 0.02 |
  | `ItemsDiffer` | 16 | Any line missing, duplicated or different; text in `Details`, e.g. `Item X: Amount shoptet=2 flexi=1` |
  | `DuplicateInvoiceCode` | 32 | The same code returned more than once by one side (first copy is compared) |

  `ShoptetValue`/`FlexiValue` hold the with-VAT totals when those differ, otherwise the
  without-VAT totals.
- **Tiles**: *Kvalita dat* (`dataqualitystatus`) uses the newest invoice run by start time;
  *DQT včera* (`dqtyesterdaystatus`) the newest run whose range contains yesterday. Failed →
  red, Running or mismatches > 0 → amber, otherwise green; no run → "no data".

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Recurring job `daily-invoice-dqt` | `0 5 * * *`, enabled | Schedule; can be changed/disabled in Recurring Jobs |
| `Shoptet:BaseUrl`, `Shoptet:ApiToken` | secrets | Shoptet REST API access |
| Flexi connection (`FlexiBeeSettings`) | secrets | Flexi API access |

Tolerance 0.02 and the CZK filter are code constants, not config.

## Runtime facts
None.

## Known quirks
- **Only CZK Shoptet invoices are read**, but the Flexi read has no currency filter, so any
  non-CZK (e.g. EUR) invoice in Flexi for the day should appear as `MissingInShoptet`.
  (Read from code, not observed.)
- **Day boundaries differ.** Shoptet is queried by creation time in whole **UTC** days, Flexi
  by a local-date range inside the SDK (which Flexi date field it filters on is not visible in
  this repo). An invoice created shortly after midnight Prague time falls into the previous UTC
  day, so it can show as missing on one side for one day and on the other side the next.
- **The import's transformations are not applied to the Shoptet side.** The check compares the
  raw mapped Shoptet invoice with Flexi; anything the import pipeline changes before writing to
  Flexi would show up as a difference. Today the only active one (`GOODYDO0001` marked
  non-stock) does not change amounts or codes; the "remove trailing D from product codes"
  transformation is a no-op.
- **The *Kvalita dat* tile follows the newest run of any kind**, so a manual run over an old
  date range replaces yesterday's result on that tile until the next scheduled run.
- **`Details` is limited to 4000 characters** and the line-diff text is not truncated; an
  invoice with very many differing lines could make the final save fail, leaving the run in
  Running with no results. (Read from code, not observed.)
- A Shoptet invoice that disappears between the list and the detail call (detail 404) is
  silently skipped, so it would surface as `MissingInShoptet`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs` — schedule, yesterday window
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs` — matching, tolerance, flags
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs` — run lifecycle, persistence
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs`, `InvoiceErpClientAdapter.cs`, `InvoiceDqtSnapshotMapper.cs` — providers from the Invoices module
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/ShoptetApiInvoiceSource.cs`, `ShoptetInvoiceClient.cs` — Shoptet list/detail calls, CZK filter
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Mapping/ShoptetInvoiceMapper.cs` — Shoptet totals, discount folding, rounding
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Invoices/FlexiIssuedInvoiceClient.cs` — Flexi totals and line codes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`, `DqtYesterdayStatusTile.cs` — tiles
