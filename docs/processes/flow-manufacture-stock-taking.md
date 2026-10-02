---
process: flow-manufacture-stock-taking
kind: workflow
module: manufacture
summary: Material stock taking (inventura materiálu) — staff count a raw material or packaging item, per lot when lot-tracked, and Heblo submits a Flexi inventory document for the material warehouse, records the result in StockTakingRecords and patches the catalog cache.
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufactureStockTaking/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureStockTakingHistory/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogStockSync.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IErpStockDomainService.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockTakingDomainService.cs
  - backend/src/Anela.Heblo.API/Controllers/ManufactureStockTakingController.cs
verified_at: "5e993f9e2"
related:
  - feed-manufacture-to-flexi
  - calc-batch-calculation
---

# Material stock taking (Inventury materiálu)

## Purpose
Raw materials and packaging (Flexi product type Material, warehouse 5) are counted regularly
so that Flexi stock — and therefore manufacture postings, the stock pre-check and the batch
calculator's "last stock taking" column — matches reality. Staff do it on page
`/manufacturing/inventory` ("Inventury materiálu"): pick a material, enter the counted amount
(or each lot with expiration and amount for lot-tracked items) and confirm. An unchanged
count is recorded as a "soft" check without touching Flexi.

## Trigger
On demand (permission `Manufacture_MaterialInventory`):
- `POST /api/ManufactureStockTaking/submit` (write) `{productCode, targetAmount?, softStockTaking, lots?[{lotCode, expiration, amount, softStockTaking}]}`.
- `GET /api/ManufactureStockTaking/history` — the product's stock-taking history from the catalog (sortable, paged).

The UI sends `softStockTaking = true` when the entered amount equals the current stock (per lot:
when the lot amount is unchanged).

## Data flow
1. Catalog cache → product; refused unless `Type == Material` (`InvalidOperation`), unknown code → `ProductNotFound`.
2. Build items: lot-tracked (`HasLots`) → one item per sent lot (lots required); otherwise one
   item with `targetAmount` (required). Missing input → HTTP error (ArgumentException propagates).
3. `FlexiStockTakingDomainService.SubmitStockTakingAsync` with `RemoveMissingLots = true`:
   - **Soft** (every item soft): no Flexi call; `AmountOld = AmountNew` = entered total.
   - **Real**: in Flexi create an inventory header (warehouse 5, owner "Heblo", type
     `Material-{productCode}`, executor = current user, date = now UTC) → add the counted
     item(s) with lot + expiration → add missing lots of the product (so lots not counted are
     zeroed) → read items (ERP amounts) → submit the inventory with document type id 60 → read
     items again. `AmountOld` = Σ ERP amounts before, `AmountNew` = Σ found amounts after.
4. Insert a `StockTakingRecords` row (`Type = Erp`, code, old/new amount, user, date).
5. Patch the catalog: the product's in-memory stock-taking history, then
   `IManufactureCatalogStockSync.SyncErpStockTakingAsync` (catalog module) writes the new stock
   level and reloads the product's lots from Flexi into the source cache. A failure here is only
   logged; the value then appears after the next background ERP refresh.

## Logic & formulas
- Units = the material's Flexi unit (grams, pieces …). Amount range 0 – 999,999.99 (attribute on `targetAmount`).
- "Soft" is decided per request: `ErpStockTakingRequest.SoftStockTaking` is true only when **all** items are soft.
- Missing lots are always added (`RemoveMissingLots = true`), so a lot not entered is counted as 0.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Warehouse | 5 (constant `MaterialWarehouseId`) | Only the material warehouse is supported |
| Submit document type | 60 (constant `SubmitDocumentTypeId`) | Flexi document type used when the inventory is submitted |

## Runtime facts
- Material stock takings I+00068 (2026-07-09), I+00076 (2026-08-19) and I+00097 (2026-09-29)
  absorbed most of the issue lines Flexi had silently not issued during manufacture (see
  `feed-manufacture-to-flexi`) — memory note gotcha_flexi_issue_doc_silently_skips_short_lines — 2026-10-01.

## Known quirks
- **A failed Flexi stock taking is not recorded.** Any exception returns a record with `Error`
  (shown to the user as `StockTakingFailed`) but nothing is written to `StockTakingRecords`; an
  inventory header created before the failure stays in Flexi un-submitted.
- **The in-code comment is inverted**: the branch commented "No real stock taking, just a
  record in DB" is the real Flexi stock taking; the soft branch is the DB-only one.
- **Only materials.** Semi-products and products cannot be counted here; product stock taking
  lives in the catalog module (e-shop / ERP stock taking).
- A soft record stores the entered amount as both old and new, so history cannot show a
  difference for it.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufactureStockTaking/SubmitManufactureStockTakingHandler.cs` — validation, lots vs simple, catalog patch
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockTakingDomainService.cs` — Flexi inventory document sequence
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogManufactureStockSyncAdapter.cs` — catalog cache refresh after a stock taking
- `frontend/src/components/inventory/ManufactureInventoryDetail.tsx` — when the UI marks a count as soft
