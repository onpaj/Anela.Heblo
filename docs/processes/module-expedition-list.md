---
process: module-expedition-list
kind: module
module: expedition-list
summary: Turns Shoptet orders waiting for dispatch into printed per-carrier picking lists for the warehouse and hands the orders on to the packing desk.
owns: []
verified_at: "5e993f9e2"
related:
  - flow-picking-list
  - flow-expedition-list-reprint
---

# Expedition list (Tisk expedice)

## Purpose
The start of the warehouse's daily dispatch. Customers order on the anela.cz e-shop (Shoptet);
customer service leaves orders ready to ship in state "Vyřizuje se". This module prints, twice a
day, the **picking lists (expediční listy)**: one A4 PDF per carrier batch telling the picker
which products to take from which shelf for which order, flagging cooled parcels and gift
eligibility. Printed orders move to "Balí se" so the packing desk (Balení) can scan them.
Orders with an incomplete home-delivery address are parked in "Poznámka" with a note instead.
It also lets staff print one forgotten order, or re-run the print for orders put in the
"Oprava robot" state.

## Users & screens
- Warehouse staff and the warehouse lead, page **Tisk expedice** `/logistics/expedition-archive`
  (sidebar Logistika; feature `Warehouse_Expedition`). The top bar (`ExpeditionJobControlsBar`) has:
  - **Expediční robot** switch + "Další běh" (next run) — enable/disable the scheduled job
    (permission `jobs.disable.read`);
  - **Spustit tisk** — run the scheduled job now (permission `jobs.trigger.read`);
  - **Spustit tisk oprav** — print orders in state 73 "Oprava robot" (Write);
  - **Tisknout zakázku** — modal: print one order by its number (Write).
  The rest of the page is the archive (module `expedition-list-archive`).
- Settings that shape the PDF live elsewhere: carrier cooling matrix and gift badge on
  **Nastavení expedice** `/customer/expedition-settings` (carrier-cooling and logistics modules).
- No MCP tools.

## Processes
- `flow-picking-list` — scheduled job `print-picking-list` (03:00 and 08:00 Prague) plus the three
  manual buttons: Shoptet orders → per-carrier PDF → archive + CUPS printer (+ optional email) →
  Shoptet state 26 "Balí se"; incomplete addresses → state 35 + e-shop remark; cooled orders →
  Shoptet `additionalFields[6] = "CHLAZENE"`.

No plain CRUD in this module.

## Data owned
No database tables. Writes:
- Temporary PDFs in the server temp directory, deleted at the end of each run.
- Azure Blob container `expedition-lists` (production; `expedition-lists-stg` elsewhere), blob
  `{yyyy-MM-dd}/{yyyy-MM-ddTHHmmss}_{METHOD}_{n}.pdf` = one printed batch. Read by the archive module.
- Shoptet order state, e-shop remark and additional field 6 (see External systems).

## External systems
- **Shoptet REST API** (anela.cz store), read/write:
  `GET /api/orders?statusId=&page=&itemsPerPage=50`, `GET /api/orders/{code}`,
  `GET /api/orders/{code}?include=stockLocation,notes`, `GET /api/orders/{code}?include=notes`,
  `PATCH /api/orders/{code}/status`, `PATCH /api/orders/{code}/notes` (e-shop remark and
  `additionalFields`). No sandbox — every run touches live orders.
- **CUPS** print server (vmHebloInfra, IPP), write: A4 printer `Cups:PrinterName` (Brother-HL-L2442DW).
- **Azure Blob Storage**, write: archive container (connection `ExpeditionList:BlobConnectionString`).
- **SendGrid**, write: optional email copy (`IEmailSender`), off unless `DefaultEmailRecipients` is set.

## Dependencies
Reads from:
- Catalog — `ICatalogRepository` (cached catalog: e-shop stock, warehouse location, product
  cooling level, price with VAT) to enrich items.
- Carrier cooling — `public."CarrierCoolingSettings"` (cooling level and badge text per carrier ×
  delivery handling).
- Logistics — `public."GiftSettings"` (gift badge) and the `IPickingListSource` contract
  (Logistics/Picking), bridged by `LogisticsExpeditionPickingAdapter`.
- Shoptet orders — `IOrderStatusReader` (current order status for the single-order guard).
- Background jobs — `RecurringJobConfigurations` (enabled flag, cron) and Hangfire.

Read by:
- Packaging (Balení) — only orders in state 26 "Balí se" are eligible for packing, so the packing desk
  depends on this module having moved them.
- Expedition list archive — lists and reprints the uploaded PDFs.

## Known quirks
- A failure mid-run leaves earlier batches printed but their orders unmoved; retries and the next run print them again (details in `flow-picking-list`).
- Production Hangfire has one worker, so scheduled/**Spustit tisk** runs can wait behind slow jobs (incident 2026-09-29).
- New Shoptet shipping methods are invisible to the picking list until added to `ShippingMethodRegistry` in code.
- Staging and development use the `AzureBlob` sink: they archive but never print (Staging also has `Hangfire:SchedulerEnabled = false`).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/ExpeditionListModule.cs` — DI registration, config section `ExpeditionList`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs` — scheduled job
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs` — `run-fix`, `print-order`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — the actual picking-list builder
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `AddPrintQueueSink`
- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` — controls bar
- `frontend/src/components/modals/PrintOrderModal.tsx` — single-order print
