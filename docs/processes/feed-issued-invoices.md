---
process: feed-issued-invoices
kind: feed
module: invoices
summary: Copies issued invoices (vydané faktury) from Shoptet into ABRA Flexi every night (EUR 04:00, CZK 04:15, yesterday's invoices) or on demand from the Issued invoices page, fixing product codes on the way and logging every attempt in IssuedInvoices / IssuedInvoiceSyncData.
owns:
  - backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Jobs/**
  - backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Transformations/**
  - backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IIssuedInvoiceImportTransformation.cs
  - backend/src/Anela.Heblo.Application/Features/Invoices/Services/**
  - backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/EnqueueImportInvoices/**
  - backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetInvoiceImportJobStatus/**
  - backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/**
  - backend/src/Anela.Heblo.Application/Features/Invoices/ProductMappingOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesMappingProfile.cs
  - backend/src/Anela.Heblo.Domain/Features/Invoices/**
  - backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs
  - backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceConfiguration.cs
  - backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceSyncDataConfiguration.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Invoices/**
verified_at: "5e993f9e2"
related: []
---

# Issued invoice import (Shoptet → ABRA Flexi)

## Purpose
Every invoice the e-shop issues to a customer exists first in **Shoptet**. Accounting, VAT and
stock write-off happen in **ABRA Flexi**, so each Shoptet invoice has to be re-created there as an
issued invoice (vydaná faktura, document type `FAKTURA`) with the right products, prices, VAT
rates, payment and delivery type, and a stock issue from warehouse `ZBOZI`. This process does that
copy automatically every night and lets staff re-run it by hand.

Heblo keeps its own row per invoice (`public."IssuedInvoices"`) with the result of the last attempt
and a full history of attempts (`public."IssuedInvoiceSyncData"`). Staff check it on page
**Vydané faktury** (`/customer/issued-invoices`): which invoices failed (red), the Flexi error
text, the exact payload sent and Flexi's raw response, and a **Znovu importovat** (re-import)
button. The same rows are later read by other modules (packing-material consumption, revenue for
marketing performance, the import-count chart) — see *Data flow → Downstream readers*.

## Trigger
| What | Id / route | When |
|---|---|---|
| Nightly EUR import | Hangfire recurring job `daily-invoice-import-eur` (category Finance) | `0 4 * * *` Europe/Prague, enabled by default |
| Nightly CZK import | Hangfire recurring job `daily-invoice-import-czk` (category Finance) | `15 4 * * *` Europe/Prague, enabled by default |
| Manual import | `POST /api/invoices/import/enqueue-async` (button **Import** on `/customer/issued-invoices`) | On demand; enqueues a Hangfire fire-and-forget job named `Import faktur: {description}` |
| Re-import one invoice | Same endpoint, from **Znovu importovat** in the invoice detail modal | On demand |
| Job progress | `GET /api/invoices/import/job-status/{jobId}`, `GET /api/invoices/import/running-jobs` | Polled by the page |

Both nightly jobs check `IRecurringJobStatusChecker` first and skip when disabled in Recurring
Jobs. Each takes `DateTime.Today − 1` (container time zone is Europe/Prague via `TZ`) as both
`DateFrom` and `DateTo`, sets the currency and calls `IInvoiceImportService.ImportInvoicesAsync`
with description `denní import {currency} za {dd.MM.yyyy}`. Per-invoice failures do not fail the
job; only an exception outside the per-invoice loop (e.g. Shoptet listing returns 401/403/5xx)
is re-thrown so Hangfire's retry policy applies.

Manual job descriptions: `faktura {InvoiceId}` (by invoice), `{d.M.yyyy} - {d.M.yyyy}` (by date)
or `obecný import` (neither set — see Known quirks). The running-jobs endpoint lists Hangfire
running + pending jobs whose display name starts with `Import faktur:`, cached in memory under
`invoices:running-import-jobs` for `Hangfire:RunningJobsCacheSeconds` (default 2 s). The nightly
jobs do not carry that display name, so they never show in the page's "Běžící importy" box.

## Data flow
1. **List** (`ShoptetApiInvoiceSource.GetAllAsync` → `ShoptetInvoiceClient`):
   - By date: `GET /api/invoices?creationTimeFrom={DateFrom}T00:00:00Z&creationTimeTo={DateTo+1}T00:00:00Z&page={n}&itemsPerPage=50`,
     all pages. If a date is missing the default is UTC yesterday / UTC today+1.
     The list carries only codes and the price block, so it is **filtered in memory by
     `price.currencyCode == query.Currency`** (Shoptet has no currency filter), then
   - `GET /api/invoices/{code}` per remaining invoice (404 → skipped silently).
   - By invoice: a single `GET /api/invoices/{InvoiceId}` — no currency filter applies.
   Header `Shoptet-Private-API-Token`. 401/403 become `InvalidOperationException` ("token is
   invalid or expired" / "access denied").
   All invoices of one run form a single batch (`BatchId` = request id).
2. **Map Shoptet → domain** (`ShoptetInvoiceMapper.Map`) — rules in *Logic & formulas*.
3. **Per invoice** (`InvoiceImportService.ExecuteImportInvoice`):
   1. Load `IssuedInvoices` row by `Id` = invoice `Code`, or create and track a new one.
   2. Overwrite its header fields from the fresh Shoptet data (`InvoicesMappingProfile`).
   3. Run the transformations in registration order (gift → "remove D" → product mapping).
   4. `FlexiIssuedInvoiceClient.SaveAsync` → AutoMapper `FlexiInvoiceMappingProfile` →
      `IssuedInvoiceDetailFlexiDto.Validate()` → FlexiBee SDK
      `IIssuedInvoiceClient.SaveAsync(invoice, unpairIfNecessary: true)`:
      if an invoice with that `kod` already exists in Flexi, its bank and cash-register payment
      pairings are **unpaired first**, then the invoice is POSTed to evidence `faktura-vydana`
      with `id = code:{Code}` (create or overwrite; `polozkyFaktury@removeAll = true`, so the
      item rows are replaced, not appended).
   5. Success → `SyncSucceeded`; any exception (incl. Flexi HTTP 400 → `IssuedInvoiceClientException`
      with the raw response) → `SyncFailed`. Either way a new `IssuedInvoiceSyncData` row is added
      with `Data` = JSON of the transformed invoice, `AdapterResponse` = Flexi's JSON reply, and the
      header gets `IsSynced`, `LastSyncTime` (UTC), `ErrorMessage`, `ErrorType`, `SyncHistoryCount`.
   6. `SaveChanges` per invoice. If something throws outside the Flexi call (DB error,
      transformation), the new row is detached / the existing row's tracked changes reverted so a
      later invoice's save cannot flush it; the invoice is counted as failed.
4. **Result**: `ImportResultDto { RequestId, Succeeded[], Failed[] }` (codes). `CommitAsync` /
   `FailAsync` on the Shoptet source are no-ops.

**Downstream readers** (other modules, read-only on `IssuedInvoices`):
- PackingMaterials — job `daily-consumption-calculation` (06:00) reads yesterday's headers
  (`Id`, `ItemsCount`) by `InvoiceDate` via `IInvoiceConsumptionSource`.
- MarketingPerformance — `IssuedInvoiceMonthlyRevenueSource`: CZK rows by `TaxDate`, sum of
  `Price` (with VAT), split retail/wholesale by `VatPayer`; EUR rows only counted.
- Analytics — daily counts by `InvoiceDate` or `LastSyncTime` (`IInvoiceImportStatisticsSource`)
  for the import-statistics chart.
- DataQuality — job `daily-invoice-dqt` (05:00) does **not** read the table: it re-reads
  Shoptet through this module's `IIssuedInvoiceSource` and Flexi through
  `IIssuedInvoiceClient.GetAllAsync` and compares them.

## Logic & formulas
**Identity.** Heblo `IssuedInvoices.Id` and the Flexi `kod` are both the Shoptet **order code**
(`src.OrderCode`); the Shoptet invoice code is kept only as `IssuedInvoiceDetail.OrderCode`
(the names are deliberately swapped in `ShoptetInvoiceMapper`). Re-importing the same invoice
always targets the same Flexi document.

**Header (Shoptet → domain → Flexi)**
| Field | Source | Flexi field |
|---|---|---|
| Code | `orderCode` | `id = code:{Code}`, `kod` |
| Issue date | `creationTime` | `datVyst`, and also `duzpPuv` + `duzpUcto` (tax dates use the **creation** date, not Shoptet `taxDate`) |
| Due date | `dueDate` | `datSplat` |
| Variable symbol | `varSymbol` | `varSym` |
| Currency | `price.currencyCode` (default CZK) | `mena = code:{CZK/EUR}` |
| Customer | `billingAddress.fullName`, street, city, `countryCode`, `companyId`, `vatId` | `nazFirmy` = **full name** (company name is not sent), `ulice`, `mesto`, `stat = code:{country}`, `ic`, `dic` |
| Payment | `billingMethod.id` 1 Dobírka→`DOBIRKA`, 2 Převodem→`PREVOD`, 3 Hotově→`HOTOVE`, 4 Kartou→`KARTA`; unknown id → by name (`bankTransfer`/`Převodem`, `cash`, `cashOnDelivery`, `creditCard`/`Kartou`, `comgate`→`KARTA`); else `PREVOD` (warning logged) | `formaUhradyCis` |
| Delivery | keyword in **item names**: exact `PPL - ParcelShop` or contains `PPL` → `PPL`; `Osobn` → `OSOBNÍ ODBĚR`; `GLS` → `GLS`; `zásilk` → `ZASILKOVNA`; nothing → `OSOBNÍ ODBĚR` | `formaDopravy` |
| VAT payer flag | `billingAddress.vatId` non-empty | — (Heblo `VatPayer` only) |
| Rounding | — | `zaokrNaSumM` / `zaokrNaDphM` (foreign-currency rounding) = `zaokrNa.zadne`; non-CZK invoices: SDK `Validate` sets all four (`zaokrNaSumK/DphK/SumM/DphM`) to `zaokrNa.setiny`; CZK invoices leave the `…K` fields to Flexi's default |
| Exchange rate | `price.exchangeRate` | **not sent** — Flexi applies its own rate |

**Items**
- Rows with `itemType` `discount-coupon`, `volume-discount` or `gift` are **not** sent. Their
  `itemPrice` (without / with VAT) is spread over the product rows in proportion to each row's
  `TotalWithoutVat`, rounded to 2 decimals; unit prices are recomputed (4 decimals).
- Per-row discount: `priceRatio` in [0, 1) multiplies `unitPrice` (0 = free item); otherwise 1.
- Unit price `cenaMj` = discounted unit price **without VAT** (`typCenyDphK = typCeny.bezDph`);
  row totals `sumZkl`/`sumCelkem` = amount × unit price, 2 decimals. For non-CZK invoices the
  totals go to `sumZklMen`/`sumCelkemMen` and the CZK ones are nulled.
- VAT rate: Shoptet numeric → named → Flexi: 21 → `high` → `typSzbDph.dphZakl`;
  12 and 15 → `first` → `dphSniz`; 10 → `second` → `dphSniz2`; 0 → `zero` → `dphOsv`;
  anything else → `dphZakl`. `kopClenKonVykDph`/`kopClenDph` = true copy the VAT-report classification from the price list.
- Price list `cenik = code:{Code}` and warehouse `sklad = code:ZBOZI` — except rows whose code is
  empty or starts with `SHIPPING`/`BILLING` (no price list, no warehouse), non-stock rows
  (no warehouse) and code `DARBAL` (warehouse removed by the SDK).

**Transformations (in this order)**
| Transformation | Effect |
|---|---|
| `GiftWithoutVATIssuedInvoiceImportTransformation` | Product `GOODYDO0001` (free gift) is marked non-stock → no warehouse. The Flexi profile also books it to credit account `zklDalUcet = code:325002`. Despite the name it does **not** change VAT. |
| `RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation` | **No-op** (TODO in code): meant to turn `TON100050D` into `TON100050`. |
| `ProductMappingIssuedInvoiceImportTransformation` | Item code `ProductMapping:ShoptetCode` (`1287`) → `ProductMapping:ErpCode` (`SLU000001`). |

**Heblo row values**
- `Price` = invoice total **with VAT**: Shoptet `toPay` when it differs from `withVat` by < 1 Kč
  (absorbs the "Zaokrouhlení" rounding line), else `withVat`; and if that header value differs
  from the item sum by ≥ 1 Kč (e.g. a deposit was deducted) the item sum is used instead. The
  item sum for this check skips zero-VAT rows. `PriceC` is never written (always 0).
- `Currency`, `InvoiceDate` (= `creationTime`), `TaxDate` (= Shoptet `taxDate`), `DueDate`,
  `VarSymbol`, `BillingMethod`, `ShippingMethod`, `VatPayer`, `ItemsCount` (number of item rows
  after removing discount rows — shipping and payment rows **are** counted),
  `CustomerName` = `"{Company} - {Name}"` or just the name.
- `ErrorType` is always `General` on failure (code `GENERAL_ERROR`); `InvoicePaired` and
  `ProductNotFound` exist but nothing sets them, so every failure counts as "critical".

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ProductMapping:ShoptetCode` | `1287` | Shoptet item code to replace (validated at startup, required) |
| `ProductMapping:ErpCode` | `SLU000001` | Flexi code it becomes |
| `Shoptet:BaseUrl`, `Shoptet:ApiToken` | (Key Vault) | Shoptet REST API used by `ShoptetInvoiceClient` |
| `Hangfire:RunningJobsCacheSeconds` | `2` (code default) | Cache of the running-imports list; 0 disables |
| Recurring Jobs enable flag | enabled | `daily-invoice-import-eur` / `daily-invoice-import-czk` on/off in Recurring Jobs |

## Runtime facts
- `IssuedInvoices.Price` is the with-VAT total and `PriceC` is 0 on every row 2023–2026 — live
  `Heblo_V3` query — 2026-09-16 (agent memory `gotcha_issued_invoice_price_is_with_vat`).
- About 20 % of rows per month have `IsSynced = false`, so "invoices in Heblo" ≠ "invoices in
  Flexi" — same source — 2026-09-16.
- Invoice counts/sums by `TaxDate` run 5–15 % above the owner's Shoptet-statistics Excel for 2026;
  treat as a different metric, not a bug — same source — 2026-09-16.

## Known quirks
- **Failures are never retried automatically.** The nightly job only imports yesterday; an
  invoice Flexi rejected stays `IsSynced = false` until someone presses **Znovu importovat** or
  runs a manual import. This is the likely source of the ~20 % unsynced rows.
- **Date-range manual import ignores the dates and the currency (bug, code reading 2026-10-02).**
  `IssuedInvoicesPage` sends `dateFromString`/`dateToString`, which are read-only computed
  properties on `IssuedInvoiceSourceQuery`, and never sends the selected currency. The server
  therefore sees no dates (description `obecný import`) and currency `CZK`, and imports CZK
  invoices created from UTC-yesterday to UTC-today, whatever range the user picked. EUR can only
  be imported by the nightly job or a single-invoice re-import.
- **Re-import by invoice uses the order code as the Shoptet invoice code.** `IssuedInvoices.Id`
  is the order code, but `GET /api/invoices/{code}` expects the invoice code. It works only while
  both numbers coincide (both are `126xxxxxx` in Anela's store); otherwise Shoptet returns 404,
  the batch is empty, and the job "succeeds" with 0 invoices. Not verified live.
- **Re-import unpairs payments in Flexi.** `unpairIfNecessary: true` removes bank and
  cash-register pairings of an existing Flexi invoice before overwriting it; they must be paired
  again (by bank-statement processing or by hand).
- Tax dates in Flexi (`duzpPuv`, `duzpUcto`) are the invoice **creation** date, while Heblo's
  `TaxDate` stores Shoptet's `taxDate`; monthly revenue in Heblo is grouped by the latter.
- The by-date window is built as `{date}T00:00:00Z`, i.e. UTC midnight, so "yesterday" covers
  01:00/02:00 Prague time yesterday until the same time today. Windows of consecutive nights
  join without gaps.
- Flexi `nazFirmy` gets the person's full name; the Shoptet company name is not sent (it only
  reaches Heblo's `CustomerName`).
- Delivery type is guessed from item names; `ShippingMethodMapper` (GUID map
  `Shoptet:InvoiceShippingGuidMap`) is registered but not used by the invoice mapper, and that
  key is not in `appsettings.json`.
- `RemoveDAtTheEndOfProductCode…` is a registered no-op; `GiftWithoutVAT…` does not touch VAT.
- `ImportInvoiceRequestDto` (incl. `TryUnpairIfNecessary`) and the domain interfaces
  `IInvoicePriceCalculator`, `IPaymentMethodResolver`, `IShippingMethodResolver` have no
  implementation or caller — leftovers of the old Playwright/Pohoda-XML import replaced by the
  REST adapter in #639.
- `ItemsCount` includes shipping and payment rows, so per-product packing-material consumption
  (`ConsumptionRate × ItemsCount`) counts them as products.
- The DQT adapter (`InvoiceShoptetSourceAdapter`) does not set a currency, so the query defaults
  to `CZK` and `daily-invoice-dqt` compares CZK invoices only.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBase.cs` — nightly job logic (EUR/CZK subclasses hold id + cron)
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — per-invoice pipeline, sync history, DbContext poison guard
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — transformation order, cross-module adapters
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/ShoptetApiInvoiceSource.cs` — list + currency filter + detail fetch
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Mapping/ShoptetInvoiceMapper.cs` — discounts, totals, VAT names, delivery keywords
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Invoices/FlexiInvoiceMappingProfile.cs` — every Flexi field, payment/delivery codes, warehouse rules
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Invoices/FlexiIssuedInvoiceClient.cs` — save (unpair) and read-back for DQT
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/EnqueueImportInvoices/EnqueueImportInvoicesHandler.cs` — manual job naming
- `docs/integrations/shoptet-api.md` §10 — Shoptet invoices API and discount handling
