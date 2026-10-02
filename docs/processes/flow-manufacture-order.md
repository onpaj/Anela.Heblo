---
process: flow-manufacture-order
kind: workflow
module: manufacture
summary: Lifecycle of a manufacture order (výrobní příkaz) in Heblo — create from the batch tools, plan, confirm the bulk and filling phases, cancel or revert — and the side effects each state change triggers (Flexi postings, sklad výroby write-down, room-conditions snapshot, planned-stock refresh).
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderSchedule/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrder/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrders/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetCalendarView/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/NextDayProductionTile.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/ManualActionRequiredTile.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ManufactureCatalogSourceExtensions.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureOrderMappingProfile.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/*ProductNameFormatter.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder*.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureType.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs
  - backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrder*.cs
  - backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs
  - backend/src/Anela.Heblo.API/Controllers/ManufactureSettingsController.cs
  - backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs
verified_at: "5e993f9e2"
related:
  - feed-manufacture-to-flexi
  - flow-manufactured-inventory
  - sync-manufacture-conditions
  - flow-manufacture-pdfs
  - calc-batch-calculation
  - calc-batch-planning
---

# Manufacture order lifecycle

## Purpose
A manufacture order (výrobní příkaz, number `MO-YYYY-NNN`) is the plan and the record of one
production batch: which bulk semi-product (meziprodukt) is mixed, how much, on which day, who is
responsible, and how many pieces of which finished products (výrobky) it is filled into. Staff
plan the week in the calendar, production confirms the real quantities, and the confirmations
post the stock movements to Flexi. Open (Planned / SemiProductManufactured) orders are also
what the catalog shows as **planned stock** (`Stock.Planned`), which purchase and batch planning
count on.

Screens: `/manufacturing/orders` (list, grid and weekly calendar), `/manufacturing/orders/:id`
(detail). Dashboard tiles: "Dnešní výroba", "Zítřejší výroba" (next working day), "Výrobní
příkazy" (count with manual action required). MCP (read-only): `GetManufactureOrders`,
`GetManufactureOrder`, `GetCalendarView`.

## Trigger
User-driven, permission `Manufacture_ManufactureOrders` (read for lists, write for changes):

| Action (UI) | Endpoint | Effect |
|---|---|---|
| Create — from batch calculator / batch planning | `POST /api/ManufactureOrder` | New order in **Draft** |
| Duplicate (Duplikovat) | `POST /api/ManufactureOrder/{id}/duplicate` | Copy in Draft, planned today, new lot/expiration |
| Edit quantities, lot, expiration, responsible, note | `PUT /api/ManufactureOrder/{id}` | Fields editable in Draft/Planned in the UI |
| Move in calendar | `PATCH /api/ManufactureOrder/{id}/schedule` | New `PlannedDate` |
| Next / previous state arrows, Cancel (Stornovat) | `PATCH /api/ManufactureOrder/{id}/status` | State change + side effects |
| Confirm semi-product / products | `POST …/confirm-semi-product`, `…/confirm-products` | See `feed-manufacture-to-flexi` |
| Resolve manual action | `POST /api/ManufactureOrder/{id}/resolve-manual-action` | Clears the flag |

States (`ManufactureOrderState`): Draft(1) → Planned(2) → SemiProductManufactured(4) → Completed(5); Cancelled(6) is terminal.

## Data flow
1. **Create** (`CreateManufactureOrderHandler`): number = next `MO-{UTC year}-{seq:000}`;
   reads the semi-product from the catalog (`ExpirationMonths`); writes `ManufactureOrders`
   + one `ManufactureOrderSemiProducts` row + `ManufactureOrderProducts` rows. Multi-phase: the
   semi-product row is the bulk (grams, `BatchMultiplier` = calculator scale factor); an optional
   extra product row with the **semi-product code** holds the "direct semi-product output"
   (bulk sold as-is, grams). Single-phase: the semi-product row is a placeholder copied from the
   first product. Planned = actual quantity at creation.
2. **Edit** (`UpdateManufactureOrderHandler`): updates given fields; product rows are updated by
   id or, if any row lacks an id, all replaced. Products always inherit the semi-product's lot +
   expiration. When product rows change → `RefreshPlannedDataAsync` (catalog planned cache).
3. **State change** (`UpdateManufactureOrderStatusHandler`), in one save:
   - `CanTransitionTo` check, `State`/`StateChangedAt`/`StateChangedByUser`, optional ERP numbers, doc codes,
     weight tolerance fields, `ManualActionRequired`, note → `ManufactureOrderNotes`.
   - Into SemiProductManufactured or Completed (first time per stage): Home Assistant snapshot →
     `ManufactureOrderConditionsReadings` (`sync-manufacture-conditions`).
   - Into Completed: write the products into sklad výroby (`flow-manufactured-inventory`).
   - After save, always: refresh catalog planned quantities (failure only logged).
4. **Resolve manual action**: optionally sets `ErpOrderNumberSemiproduct`, `ErpOrderNumberProduct`,
   `ErpDiscardResidueDocumentNumber`, adds a note, sets `ManualActionRequired = false`.
5. **Planned stock read** (`ManufactureCatalogSourceAdapter` → `GetPlannedQuantitiesAsync`):
   Σ `ManufactureOrderProducts.PlannedQuantity` per product code over orders not in Draft,
   Completed or Cancelled → catalog `Stock.Planned` (BackgroundRefresh, plus the on-write refresh above).

## Logic & formulas
- **Allowed transitions** (`ManufactureOrder.CanTransitionTo`):
  Draft → Planned, Cancelled · Planned → Draft, SemiProductManufactured, Completed, Cancelled ·
  SemiProductManufactured → Planned, Completed, Cancelled · Completed → SemiProductManufactured,
  Planned, Cancelled · Cancelled → nothing.
- **UI path**: multi-phase Draft → Planned → (confirm semi-product) → SemiProductManufactured →
  (confirm products) → Completed; back arrow one step (Completed → SemiProductManufactured).
  Single-phase Planned → (confirm products) → Completed, back arrow Completed → Planned.
  Cancel (Stornovat) is offered in every state except Completed and Cancelled.
- **Default lot** = ISO week + 2-digit year of the creation date (`wwyy`, e.g. `4026`).
- **Default expiration** = last day of the month `ExpirationMonths + 1` months after the creation
  month (e.g. created 2026-10-02 with 12 months → 2027-11-30).
- **Schedule**: refused for Cancelled/Completed orders and for dates before today.
- **Manual action required** is set by the confirmation workflows when Flexi posting or a BoM
  update failed; the dashboard tile counts these orders and drills to the filtered list.
- **Today / next-day tiles** list up to 5 non-cancelled orders planned for that date (next = next
  Monday–Friday day, holidays not considered), showing semi-product name, quantity, responsible
  person and whether each phase is done.
- `GET /api/manufacture/settings` returns `ManufactureErp:ManufactureGroupId` — the Entra ID group whose
  members the UI offers as responsible person.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ManufactureErp:ManufactureGroupId` | `your-entra-id-group-id-here` (placeholder; real value per environment) | Group listing selectable responsible persons |
| `BackgroundRefresh` planned-data refresh | catalog-owned | Periodic reload of `Stock.Planned`; this flow refreshes it on write as well |

## Runtime facts
None.

## Known quirks
- **Reverting or cancelling never undoes side effects.** Going back from Completed or
  SemiProductManufactured, or cancelling a SemiProductManufactured order, leaves the Flexi
  documents, the BoM rewrite and the sklad výroby rows in place. Re-confirming posts to Flexi
  again; the inventory write-down is skipped for an order already written (idempotency guard,
  see `flow-manufactured-inventory`).
- **The raw status endpoint bypasses Flexi.** `PATCH …/status` to Completed (e.g. Planned →
  Completed on a multi-phase order, which the state machine allows) writes the products into
  sklad výroby without posting anything to Flexi. The UI never does this; API/MCP callers could.
- **`ChangeReason` is accepted but never stored** by the status handler; only `Note` becomes an order note.
- **Duplicate does not copy `ManufactureType`.** The copy gets the entity default MultiPhase, so
  duplicating a single-phase order yields a multi-phase order with a placeholder semi-product
  (read from code, not observed).
- **Order number after 999 orders a year.** The next number is taken from the lexically largest
  `OrderNumber`; `MO-YYYY-999` sorts after `MO-YYYY-1000`, so the 1001st order would regenerate
  `MO-YYYY-1000` and hit the unique index.
- **Lot year is the calendar year**, the week is ISO: 2027-01-01 (ISO week 53 of 2026) gets lot `5327`.
- **Direct-output row is in grams but sits among pieces.** Planned-stock sums it under the
  semi-product code; harmless while semi-products are not shown with planned stock — memory
  note gotcha_stock_planned_disjoint_from_total.
- `Stock.Planned` excludes Draft, so a freshly created order does not show as planned until it is moved to Planned.

## Code entry points
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs` — state machine
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderExtensions.cs` — default lot and expiration
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` — creation, direct-output row
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` — side effects of a state change
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs` — edits
- `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs` — order number, planned quantities
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — endpoints
- `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx` — which transitions the UI offers
