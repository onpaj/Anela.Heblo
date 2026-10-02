---
process: module-manufacture
kind: module
module: manufacture
summary: Plans and records Anela's own production — mixing bulk semi-products and filling them into finished cosmetics — and posts every confirmed batch to ABRA Flexi as stock documents.
owns: []
verified_at: "5e993f9e2"
related:
  - feed-manufacture-to-flexi
  - flow-manufacture-order
  - flow-manufactured-inventory
  - flow-manufacture-stock-taking
  - flow-manufacture-pdfs
  - sync-manufacture-conditions
  - calc-batch-calculation
  - calc-batch-planning
  - calc-manufacture-stock-analysis
  - calc-manufacture-output
  - calc-margins
  - feed-stock-up
---

# Manufacture (Výroba)

## Purpose
Anela makes most of its cosmetics in-house. Production works in two phases:
1. **Semi-product** (meziprodukt / polotovar): a bulk batch (e.g. 10 kg of cream) mixed from raw
   materials by a recipe (Flexi bill of materials, kusovník).
2. **Products** (výrobky): the bulk is filled into pack sizes, consuming packaging and labels.
"Single-phase" products skip the bulk step and are made directly from materials.

The module helps decide **what** to make (stock analysis), **how much** (batch calculator and
batch planning), schedules it as manufacture orders (výrobní zakázky), and when production
confirms real quantities it posts the material issues and product receipts to ABRA Flexi —
the stock and accounting system of record — with lots (šarže), expirations and cost. It also
keeps a small ledger of finished pieces still in the production room ("sklad výroby"), counts
material stock (inventury), records room conditions and prints batch protocols.

## Users & screens
Production staff and the production manager. Menu "Výroba":

| Page | Route | Use |
|---|---|---|
| Zásoby výrobků | `/manufacturing/stock-analysis` | Which products run low vs optimal days |
| Inventury materiálu | `/manufacturing/inventory` | Count materials → Flexi stock taking |
| Souhrn výroby | `/manufacturing/output` | Monthly difficulty-weighted output |
| Kalkulačka dávek | `/manufacturing/batch-calculator` | Scale a recipe, print recipe sheet, create order |
| Plánování dávek | `/manufacturing/batch-planning` | Split a bulk batch across pack sizes, create order |
| Výrobní zakázky | `/manufacturing/orders`, `/manufacturing/orders/:id` | Calendar, order detail, confirmations, protocol |
| Sklad výroby | `/manufacturing/product-inventory` | Finished pieces waiting in production |

("Šarže" `/manufacturing/material-containers` is in the same menu but belongs to the catalog module.)

Dashboard tiles: Dnešní výroba, Zítřejší výroba, Výrobní příkazy (manual action required),
Podmínky ve výrobně. MCP tools: `GetBatchTemplate`, `CalculateBatchBySize`,
`CalculateBatchByIngredient`, `CalculateBatchPlan`, `GetManufactureOrders`,
`GetManufactureOrder`, `GetCalendarView` (all read-only).

Permissions (features): `Manufacture_ManufactureOrders`, `Manufacture_BatchPlanning`,
`Manufacture_ManufactureStock`, `Manufacture_ManufactureOutput`, `Manufacture_MaterialInventory`,
`Manufacture_ProductInventory` (read / write).

## Processes
- `flow-manufacture-order` — order lifecycle Draft → Planned → SemiProductManufactured → Completed / Cancelled and the side effects of each change; user-driven.
- `feed-manufacture-to-flexi` — confirm semi-product / products: Flexi issue + receipt documents (V-VYDEJ-MATERIAL, V-PRIJEM-POLOTOVAR, V-VYDEJ-POLOTOVAR, V-PRIJEM-VYROBEK), FEFO lots, cost, residue distribution, BoM rewrite; user-driven.
- `flow-manufactured-inventory` — sklad výroby ledger: write-down on completion, transport-box consumption/restore, manual edits; feeds `Stock.Manufactured`.
- `flow-manufacture-stock-taking` — material stock taking into Flexi (warehouse 5); user-driven.
- `sync-manufacture-conditions` — Home Assistant temperature/humidity → tile and per-order readings.
- `flow-manufacture-pdfs` — manufacture protocol PDF (with Flexi document lines) and recipe PDF; on demand.
- `calc-batch-calculation` — recipe scaled by batch size or by one ingredient; on demand.
- `calc-batch-planning` — equal-coverage split of a bulk batch across pack sizes; on demand.
- `calc-manufacture-stock-analysis` — product stock days vs optimal days, severity; on demand.
- `calc-manufacture-output` — monthly output from Flexi manufacture receipts × difficulty; on demand.

Plain reads / CRUD without a process doc: order list, detail and calendar
(`GET /api/ManufactureOrder…`), stock-taking history, `GET /api/manufacture/settings`
(responsible-person Entra group). The module has **no Hangfire jobs** and no BackgroundRefresh
task of its own.

## Data owned
- `public."ManufactureOrders"` — one manufacture order: number, type, planned date, state + audit, responsible person, Flexi document codes, manual-action flag, weight tolerance.
- `public."ManufactureOrderSemiProducts"` — the bulk line of an order (grams, scale factor, lot, expiration, expiration months); a placeholder for single-phase.
- `public."ManufactureOrderProducts"` — finished-product lines (planned/actual pieces, lot, expiration); a row with the semi-product code = bulk sold as-is (grams).
- `public."ManufactureOrderNotes"` — notes, incl. automatic notes from confirmations (max 2000 chars).
- `public."ManufactureOrderConditionsReadings"` — temperature/humidity snapshot per order and stage.
- `public."ManufacturedProductInventoryItems"` / `…Logs` — sklad výroby rows per product + lot + expiration and their change log.
- `public."StockTakingRecords"` — written for material stock takings (table shared with the catalog module).
- In-memory cache `manufacture-template:{productCode}` — Flexi BoM + lot flags, 5 min.

## External systems
- **ABRA Flexi** (read/write): BoM (kusovník) read, "where used" and ingredient-amount update;
  stock-to-date per warehouse (5 material, 20 semi-products, 4 products); lots; stock movement
  documents created (`skladovy-pohyb`) and read back (`skladovy-pohyb-polozka`, also for the
  output report via `DataSourceOptions:ManufactureDocumentTypeIds`); inventory (stock-taking)
  documents for warehouse 5.
- **Home Assistant** (read): four sensor states via REST `/api/states/{entity}`.
- **Microsoft Entra ID** (indirect): the responsible-person picker lists members of `ManufactureErp:ManufactureGroupId`.

## Dependencies
- Reads **catalog** (`IManufactureCatalogSource`): product type, MMQ, expiration months, allowed
  residue %, net weight, stock, planned stock, sales and manufacture history, difficulty, name
  suffixes; calls `IManufactureCatalogStockSync` after a stock taking.
- Feeds **catalog** (`ICatalogManufactureSource`): planned quantities of open orders
  (`Stock.Planned`), Flexi manufacture history, sklad výroby totals (`Stock.Manufactured`); the
  margin calculation (`calc-margins`) uses the manufacture history and cost.
- Serves **logistics**: `IInventoryReservationService` (transport boxes consume/restore sklad
  výroby); logistics' gift-package manufacture uses `IManufactureClient.GetSetPartsAsync` (set
  composition) — gift packages themselves are documented by the logistics module.
- Shares `TimePeriodResolver` with purchase analysis.

## Known quirks
- **Flexi postings are not transactional** and reverting/cancelling an order never reverses them;
  a timeout or a partial failure needs a manual check in Flexi (`feed-manufacture-to-flexi`).
- **A breaker-open or failed posting still advances the order** with `ManualActionRequired`;
  the dashboard tile "Výrobní příkazy" is the to-do list for those.
- **Every multi-phase completion rewrites product BoMs** with the actual semi-product grams per piece.
- **The FluentValidation validators in `Features/Manufacture/Validators` are never registered** —
  their rules are not enforced.
- **Several `ManufactureAnalysis` settings are dead** (only `InfiniteStockIndicator` is read).
- **The output report and the M1 cost rate are dominated by semi-product grams** since doc types
  54/65 were added (2026-09-21) — `calc-manufacture-output`.
- **Duplicating a single-phase order produces a multi-phase order** (type not copied).
- `docs/features/` has no manufacture-order spec; `gift-package-manufacture.md` there is logistics.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — DI, options, tiles, cross-module adapters
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs` — order entity and state machine
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/` — confirmation workflows
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` — everything written to Flexi
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — order endpoints
- `frontend/src/components/manufacture/` — order list, calendar and detail UI
