---
process: flow-picking-list
kind: workflow
module: expedition-list
summary: Pulls Shoptet orders waiting for dispatch, renders per-carrier picking-list PDFs (expediční list) for the warehouse, prints and archives them, and moves the orders to "Balí se" — twice a day, on demand, as a fix re-run, or for one order.
owns:
  - backend/src/Anela.Heblo.Application/Features/ExpeditionList/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/Picking/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapter.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/**
  - backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/**
  - backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs
verified_at: "5e993f9e2"
related: []
---

# Picking list print (Tisk expedice)

## Purpose
Every morning the warehouse needs a printed list of what to pick and pack. Heblo builds it from
the Shoptet e-shop: it takes every order waiting in state **"Vyřizuje se"**, groups the orders by
shipping method (Zásilkovna, PPL, GLS, personal pickup), and prints one A4 **picking list
(expediční list)** per batch on the warehouse printer (Brother, via CUPS). Each list has a block
per order (order number, CODE-128 barcode, customer name/address/phone, items, notes, a frost
badge for cooled parcels and a gift badge for large orders) and a final summary page with the
total quantity of every product, sorted by warehouse position, so the picker can walk the shelves
once. After printing, the orders move to **"Balí se"**, which is the state the packing desk
(Balení) accepts. Orders whose delivery address is incomplete are not printed: they are moved
to **"Poznámka"** with an internal note so customer service can fix them.

Staff see and control it on page **Tisk expedice** (`/logistics/expedition-archive`, sidebar
Logistika). The same page lists the archived PDFs (see `flow-expedition-list-reprint`).

## Trigger
Four entry points, all running the same pipeline (`ExpeditionListService.PrintPickingListAsync`):

| Entry point | Who / where | Source state | Prints | Emails | Changes state |
|---|---|---|---|---|---|
| Hangfire job `print-picking-list`, cron `0 3,8 * * *` (Europe/Prague → **03:00 and 08:00**) | automatic; `DefaultIsEnabled = true` | `SourceStateId` (-2 "Vyřizuje se") | if `SendToPrinterByDefault` | if `DefaultEmailRecipients` non-empty | if `ChangeOrderStateByDefault` |
| **Spustit tisk** button | `ExpeditionJobControlsBar`, needs permission `jobs.trigger.read`; enqueues the same Hangfire job via Recurring Jobs trigger | -2 | as job | as job | as job |
| **Spustit tisk oprav** button → `POST /api/expedition-list/run-fix` | Warehouse_Expedition **Write**; runs synchronously in the HTTP request | `FixSourceStateId` (73 "Oprava robot") | if `SendToPrinterByDefault` | never | if `ChangeOrderStateByDefault` |
| **Tisknout zakázku** (`PrintOrderModal`) → `POST /api/expedition-list/print-order` `{orderCode}` | Warehouse_Expedition **Write**; synchronous | any (single order by code) | always | never | always |

The **Expediční robot** switch on the same page (permission `jobs.disable.read`) enables/disables
the recurring job. When it is off, the scheduled run logs "disabled" and does nothing, and
**Spustit tisk** is refused (`RecurringJobDisabled`); the fix re-run and single-order print still work.
The cron is stored in `public."RecurringJobConfigurations"` and can be overridden by an admin;
the seeder keeps an admin-edited cron.

Order state flow (Shoptet status ids):
`-2 Vyřizuje se` (or `73 Oprava robot` for the fix run) → printed → `26 Balí se` → packing desk →
`52 Zabaleno` → `70 Předáno přepravci`. Incomplete address → `35 Poznámka`.

## Data flow
1. **Select orders** (`ShoptetApiExpeditionListSource.CreatePickingList`):
   - batch mode: `GET /api/orders?statusId={SourceStateId}&page={n}&itemsPerPage=50`, all pages;
   - single-order mode: `GET /api/orders/{code}` (404 → no orders).
2. **Map to shipping method**: each order's `shipping.guid` is looked up in the hard-coded
   `ShippingMethodRegistry` (25 methods, GUIDs of the anela.cz store, retail + wholesale).
   Unknown GUID → the order is silently skipped (left in its state). Batch modes also filter by
   carrier (`DefaultCarriers` = Zásilkovna, GLS, PPL, Osobák); single-order mode does not filter.
3. **Read settings once per run**: `public."CarrierCoolingSettings"` (cooling level + badge text per
   carrier × delivery handling, edited on `/customer/expedition-settings`, carrier-cooling module)
   and `public."GiftSettings"` (row Id 1: enabled, CZK threshold, badge text; logistics module).
4. **Per order**: `GET /api/orders/{code}?include=stockLocation,notes`.
   - "Do ruky" (home delivery) methods only: validate the delivery address (fallback billing):
     recipient name or company, street, house number, city, ZIP. Missing → `PATCH
     /api/orders/{code}/status` to `NoteStateId` (35), then append to the e-shop remark
     (`GET …?include=notes` + `PATCH /api/orders/{code}/notes`) the line
     `Robot expedice: neúplná adresa – chybí: <fields>.`; the order is counted as skipped.
     Both writes are best-effort (failures only logged).
   - Otherwise map to an `ExpeditionOrder` and attach carrier cooling, cooling text and gift badge.
5. **Batch** orders per shipping method: start a new batch when adding the order would exceed
   `MaxItems` item lines (default 13) or the batch already has `MaxOrders` orders (default 6).
   Osobní odběr: 1 order per PDF.
6. **Per batch** (`PickingListBatchProcessor.FlushAsync`):
   - enrich items from the catalog cache (`ICatalogRepository.GetByIdAsync`): stock
     `Stock.Eshop`, location (only where Shoptet gave none), product cooling level, and
     `PriceWithVat` (only where the Shoptet price is 0);
   - render the PDF (QuestPDF, `ExpeditionProtocolDocument`) to the server temp dir as
     `{yyyy-MM-ddTHHmmss}_{METHOD_NAME}_{batchIndex}.pdf` (Prague time; container `TZ=Europe/Prague`);
   - for every cooled order: `PATCH /api/orders/{code}/notes` with `additionalFields[6] = "CHLAZENE"`
     (best-effort, write-only marker; nothing in Heblo reads it back);
   - hand the file to the batch callback: print sink (if printing) and SendGrid email (if recipients).
7. **Print sink** (`IPrintQueueSink`, chosen by `ExpeditionList:PrintSink` at startup):
   - `Combined` (**production**): upload to Azure Blob `expedition-lists/{yyyy-MM-dd}/{file}.pdf`
     (overwrite), **then** send to CUPS (`{Cups:ServerUrl}/printers/{Cups:PrinterName}`, IPP Print-Job,
     `application/pdf`);
   - `AzureBlob` (repo default; staging/dev): blob upload only, nothing printed;
   - `Cups`: print only, no archive; `FileSystem` (code default if the key is unset): copy to `PrintQueueFolder`.
8. **Email copy** (only if `DefaultEmailRecipients` set, scheduled run only): from `EmailSender`,
   subject `Expedice yyyy-MM-dd`, the batch PDF attached — one email **per batch**.
9. **State change**, after all batches are rendered and sent: for every printed order
   `PATCH /api/orders/{code}/status` with `{"data":{"statusId": DesiredStateId}}` (26).
10. Delete the temp PDFs; return counts (printed orders, skipped for address).

## Logic & formulas
- **Cooled parcel** (`ExpeditionOrder.IsCooled`): some item has product cooling ≠ None and
  product cooling ≤ carrier cooling, where carrier cooling comes from the
  carrier × delivery-handling matrix (`Cooling` None=0, L1=1, L2=2). Delivery handling is derived
  from the method name: `DO_RUKY` → NaRuky; `PARCELSHOP`/`ZPOINT`/`BOX`/`VYDEJNI` → Box; anything
  else (`*_EXPORT`, `OSOBAK`) → none, so those orders are never cooled and never address-checked.
  Badge text = `CoolingText` from the matrix, default "CHLAZENÁ ZÁSILKA".
- **Gift badge**: shown when `GiftSettings.IsEnabled`, order currency is `CZK` and order total
  **with VAT** (`price.withVat` from the order list) ≥ `ThresholdCzk`. EUR orders never get it.
- **Items**: `product` and `gift` lines are printed as-is; a `product-set` line is replaced by its
  `product-set-item` components from `completion[]`, whose amount is already the order total
  (not multiplied again), printed in italics under "Sada: {name}". Quantity is cast to int.
- **Price column** ("Cena"): Shoptet `itemPriceWithVat` (unit price, with VAT, order currency);
  0 → catalog `PriceWithVat`; still 0 → blank. Always suffixed "Kč", `N0` format.
- **Stock column** ("Stav skladu"): catalog cache `Stock.Eshop` at print time (pieces), not live Shoptet.
- **Summary page**: items of the batch grouped by product code, quantities summed, sorted by
  warehouse position (empty positions first). Name, position and stock taken from the first line.
- **Single-order print guard** (`PrintExpeditionOrderHandler`): reads the current status
  (`GET /api/orders/{code}`) and refuses with `ExpeditionOrderInvalidState` when it is 26
  (`DesiredStateName` "Balí se"), -3 "zrušeno/blokováno", 52 "Zabaleno" or 70 "Předáno přepravci";
  404 → `ShoptetOrderNotFound`; nothing printed (unknown shipping method or incomplete address)
  → `ExpeditionOrderNotPrinted`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ExpeditionList:SourceStateId` | -2 | Shoptet state the scheduled run picks up ("Vyřizuje se") |
| `ExpeditionList:FixSourceStateId` | 73 | State the fix re-run picks up ("Oprava robot") |
| `ExpeditionList:DesiredStateId` / `DesiredStateName` | 26 / "Balí se" | State after printing; also the "already printed" guard |
| `ExpeditionList:NoteStateId` | 35 | State for incomplete address ("Poznámka"); single-order print uses the code constant 35 instead |
| `ExpeditionList:SendToPrinterByDefault` | true (class default false) | Scheduled + fix runs send PDFs to the print sink |
| `ExpeditionList:ChangeOrderStateByDefault` | true | Scheduled + fix runs move printed orders to `DesiredStateId` |
| `ExpeditionList:DefaultEmailRecipients` | not set (empty) | Email a copy of each batch (scheduled run only) |
| `ExpeditionList:EmailSender` | heblo@anela.cz | From address of the email copy |
| `ExpeditionList:PrintSink` | `AzureBlob`; Production `Combined` | `FileSystem` \| `AzureBlob` \| `Cups` \| `Combined` |
| `ExpeditionList:BlobConnectionString` | placeholder; KV `ExpeditionList--BlobConnectionString` | Storage account for the archive upload |
| `ExpeditionList:BlobContainerName` | `expedition-lists`; Staging/Dev/Test `expedition-lists-stg` | Archive container |
| `ExpeditionList:PrintQueueFolder` | `PDFPrints` | Target folder for the `FileSystem` sink |
| `Cups:ServerUrl` / `PrinterName` | Tailscale URL / `Brother-HL-L2442DW` | CUPS server and A4 printer |
| `Cups:Username` / `Password` | placeholders (KV) | Basic auth for CUPS |
| `Hangfire:SchedulerEnabled` | false; Production true, Staging false | Whether recurring jobs fire at all |

## Runtime facts
- Production Hangfire runs a single worker (`HangfireOptions.WorkerCount` default 1). On 2026-09-29
  12:05–13:50 slow `PlaudPollingJob` runs queued manual **Spustit tisk** runs 12–15 min; every batch
  printed once it ran (CUPS on vmHebloInfra, `docker exec cups-server lpstat -W completed -o`) —
  agent memory `gotcha_hangfire_single_worker_starves_print` — 2026-09-29.
- Shoptet status 55 "K Expedici" does not exist in the store; the live states used here are -2, 26,
  35, 52, 70, 73 — `docs/integrations/shoptet-api.md` — 2026-06.

## Known quirks
- **Partial failure reprints everything.** Order states are changed only after *all* batches are
  printed. If a later batch, the CUPS call or one status PATCH throws, earlier batches are already
  printed and archived but their orders stay in "Vyřizuje se"; the job has no `[AutomaticRetry]`,
  so Hangfire's default retries (and the next scheduled run) print them again. One failing status
  PATCH also stops the state change for all remaining orders.
- **"Printing is broken" is often queue latency**: scheduled and **Spustit tisk** runs share the single
  Hangfire worker; **Spustit tisk oprav** and **Tisknout zakázku** run in the HTTP request and are not delayed.
- In `Combined` mode the blob upload runs first; a blob failure means nothing is printed, a CUPS
  failure leaves the PDF archived but unprinted.
- New Shoptet shipping methods must be added to `ShippingMethodRegistry` by hand; until then their
  orders are silently left out of the list (no log, no count).
- `*_EXPORT` and personal-pickup orders are never cooled and never address-checked (no delivery handling).
- The Cena column always says "Kč", even for EUR orders priced in EUR.
- Set components get the catalog price (Shoptet returns 0) and the catalog location (Shoptet
  returns none), not what was sold in the set.
- `StockDemand` (Shoptet `stockStatus.allDemand`) is mapped onto every item but never printed.
- The code comment on the cron says "4:00 and 9:00 Prague time"; the cron is evaluated in
  Europe/Prague, so the runs are at 03:00 and 08:00 (unless overridden in the DB).
- The email copy goes out once per batch (several emails per run), and only from the scheduled run.
- Single-order print only checks for states 26/-3/52/70; an order in any other state (e.g. 35
  Poznámka, a custom state) is printed and moved to 26.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs` — Hangfire job, metadata, request built from options
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` — batch callback (print sink + email), temp cleanup
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs` — single-order print and state guard
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs` — fix re-run from state 73
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — `ExpeditionList` config section
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — bridge to the Logistics `IPickingListSource`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — order fetch, address check, item mapping, state change
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/PickingListBatchProcessor.cs` — catalog enrichment, PDF write, cooling marker
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs` — shipping GUIDs, batch limits, delivery handling
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — PDF layout
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `AddPrintQueueSink` (sink selection)
- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` — buttons and robot switch
