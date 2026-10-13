---
process: flow-manufactured-inventory
kind: workflow
module: manufacture
summary: Keeps Heblo's own "sklad výroby" ledger of finished pieces that left production but are not yet in the warehouse — written down when an order completes, consumed when packed into a transport box, restored when unpacked, corrected by hand — and feeds it into catalog stock as Stock.Manufactured.
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureInventoryWriteDownService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureInventoryWriteDownService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufacturedInventoryItem/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufacturedInventoryItem/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DeleteManufacturedInventoryItem/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufacturedInventory/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ManufacturedProductInventory*.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/**
  - backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/**
  - backend/src/Anela.Heblo.API/Controllers/ManufacturedProductInventoryController.cs
verified_at: "5e993f9e2"
related:
  - flow-manufacture-order
  - feed-manufacture-to-flexi
  - feed-stock-up
---

# Sklad výroby (manufactured-product inventory)

## Purpose
Finished pieces are received into the Flexi products warehouse when an order is confirmed,
but physically they still stand in the production room until they are packed into transport
boxes and shipped to the warehouse. "Sklad výroby" (page `/manufacturing/product-inventory`)
is Heblo's own ledger of those pieces, per product + lot + expiration, with a full change log.
Logistics picks from it when filling a transport box, and the catalog adds it to product stock
(`Stock.Manufactured`), so the pieces are not invisible between production and warehouse.

## Trigger
- **Write-down**: automatically when a manufacture order enters **Completed** (status handler,
  see `flow-manufacture-order`).
- **Consume / restore**: Logistics transport-box actions — adding an item with a source
  inventory row (`AddItemToBox`), removing it (`RemoveItemFromBox`), and resetting a box from
  Opened back to New (`ChangeTransportBoxState`, restores every sourced item).
- **Manual** (permission `Manufacture_ProductInventory` write): `POST /api/manufactured-inventory`
  (new row), `PUT /api/manufactured-inventory/{id}` `{newAmount, note}` (set amount),
  `DELETE /api/manufactured-inventory/{id}` (remove row). List: `GET /api/manufactured-inventory`
  (search, only-with-stock, order id, paging; default page size 50).

## Data flow
1. **Write-down** (`ManufactureInventoryWriteDownService`): order product rows with
   `ActualQuantity > 0` → drop products whose catalog type is SemiProduct (so the direct bulk
   output row is skipped) → group by (product, lot, expiration), Σ pieces → for each group:
   - existing `ManufacturedProductInventoryItems` row with the same product + lot + expiration:
     if its log already references this order → skip; else add the amount and a log entry;
   - no such row → insert a row (with `ManufactureOrderId`) and an `InitialWriteDown` log entry.
   Saved in the same transaction as the order state change.
2. **Consume** (`ManufactureInventoryReservationAdapter.TryConsumeAsync`): amount −= pieces put
   in the box; refused (`InsufficientStock`) when it would go below 0 unless the box request
   allows negative stock; log `ConsumedByTransportBox` with box id + code. Saved with the box.
3. **Restore**: amount += pieces; log `RestoredFromTransportBox`. A missing row is skipped with a warning.
4. **Manual**: create → new row + `InitialWriteDown` log; set amount → `ManualAdjustment` log
   with the delta; delete → row and its log removed.
5. **Read by catalog** (`ManufactureCatalogSourceAdapter.GetManufacturedInventoryAsync`):
   Σ `Amount` per product over rows with Amount > 0 → `Stock.Manufactured`, refreshed by the
   catalog's BackgroundRefresh task `RefreshManufacturedData` (every 5 min).

## Logic & formulas
- Units: pieces of finished product. Lot and expiration are those of the order's products
  (inherited from the semi-product row).
- `Stock.Available` = warehouse + transport + **manufactured**; `Stock.Total` = Available + reserve.
- Log change types: `InitialWriteDown`(1), `ConsumedByTransportBox`(2), `RestoredFromTransportBox`(3),
  `ManualAdjustment`(4); `ManualRemoval`(5) and `ManualAddition`(6) exist but are never written.
- Order reference in the log: `ReferenceType = "ManufactureOrder"`, `ReferenceId` = order id;
  box reference: `ReferenceType = "TransportBox"`, `ReferenceId` = box id, note = box code.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `BackgroundRefresh:…:RefreshManufacturedData` | every 00:05:00, tier 1 (catalog-owned) | How fast catalog stock picks up ledger changes |

## Runtime facts
- Before the idempotency fix, complete → revert → complete produced duplicate identical rows
  (e.g. 152 + 152); duplicates already in the DB were not cleaned up — memory note
  gotcha_manufacture_writedown_idempotency.
- 2026-09-15: gift-package manufacture checked `Stock.Available` (incl. sklad výroby) and drove
  warehouse stock negative (349 shown vs 184 on hand) — memory note
  gotcha_stock_available_includes_manufacture_warehouse.

## Known quirks
- **Nothing here touches Flexi or Shoptet.** The pieces were already received into Flexi
  warehouse 4 by the completion posting; this ledger only tracks where they physically are.
- **Re-completing after an edit does not change the booked amount** — the order is treated as
  already written. Reverting an order never removes its rows.
- **Deleting a row erases its log**, including the order reference, so completing that order
  again would write the pieces down a second time. The `note` sent with DELETE is ignored.
- **No unique index** on (product, lot, expiration); de-duplication is application-level only and
  rows with NULL lot/expiration can still collide.
- **Manual edits are unchecked**: "set amount" accepts any value, negative included; manual
  create (amount > 0) always inserts a new row, even when one with the same product + lot +
  expiration exists. Rows with amount ≤ 0 are ignored by the catalog total.
- Sklad výroby counts in catalog availability; any flow that deducts from the **warehouse**
  must check `WarehouseStock`, not `Available` (see Runtime facts).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureInventoryWriteDownService.cs` — merge + idempotency
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/ManufacturedProductInventoryItem.cs` — amount changes and log
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs` — Logistics contract
- `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs` — totals for catalog
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs` — consumer side
