---
process: module-packing-materials
kind: module
module: packing-materials
summary: Tracks stock of packing consumables (boxes, filler, tape…), deducts an estimated daily usage from invoiced orders and forecasts how many days the stock will last.
owns: []
verified_at: "5e993f9e2"
related:
  - calc-packing-material-consumption
---

# Packing materials (Sledování materiálů)

## Purpose
Lets the warehouse see when packing consumables — shipping boxes, filler, tape, stickers and
similar — will run out, without counting them every day. Each material has a consumption rule
(per order, per invoice row, or per day); every morning Heblo deducts yesterday's estimated
usage and shows the remaining stock and a "days left" forecast. Staff overwrite the quantity
with a real count after stock-taking or a delivery. This stock is Heblo-only: it is not in
Flexi or Shoptet, and it does not feed margins or product costs.

## Users & screens
Warehouse / logistics staff, permission feature `Warehouse_Logistics` (Read to view, Write to
change anything).

- **`/logistics/packing-materials`** — sidebar "Sledování materiálů", page title
  "Sledování materiálů", two tabs:
  - **Nastavení** — grid of materials: name, consumption rule (za zakázku / za produkt /
    za den) and rate, current quantity, forecast days. Buttons **Přidat materiál**,
    **Odečíst spotřebu** (manual daily run), per row **Upravit množství**, **Editovat
    materiál**, **Smazat materiál**. Clicking a row opens the detail with the last 60 days of
    quantity changes (grid + chart).
  - **Historie spotřeby** — paged list (max 100 per page) merging consumption rows
    ("Spotřeba") and quantity changes ("Změna množství"); filters date from/to, material,
    consumption type, product code, invoice id. A consumption-only filter (type, product,
    invoice) hides the quantity changes.
- No dashboard tile and no MCP tool.

**Forecast days** (`PackingMaterial.CalculateForecastedDays`): current quantity ÷ average of
the stock **decreases** logged in the last month (log `CreatedAt` ≥ now − 1 month, UTC). Every
decrease counts — automatic daily deductions and manual corrections down; increases are
ignored. No decreases → no forecast (shown "N/A"); quantity ≤ 0 → 0. The API rounds to 0.1
day; the UI shows whole days ("12 dní") and "∞" above 365.

## Processes
- `calc-packing-material-consumption` — deducts yesterday's estimated consumption from every
  material; Hangfire `daily-consumption-calculation` 06:00, or manually via Odečíst spotřebu.

Plain CRUD (no process doc): create/edit/delete material, set quantity (writes a `Manual` log
with the user id and the chosen date), allocation CRUD (API only, see quirks), read endpoints
(list, logs, consumption breakdown, consumption history).

## Data owned
All in schema `public`:
- `PackingMaterials` — one material: `Name`, `ConsumptionType` (1 PerOrder, 2 PerProduct,
  3 PerDay), `ConsumptionRate`, `CurrentQuantity` (decimal(18,6)).
- `PackingMaterialLogs` — one quantity change: `Date`, `OldQuantity`, `NewQuantity`,
  `LogType` (1 Manual, 2 AutomaticConsumption), `UserId` (manual only).
- `PackingMaterialConsumptions` — one consumption fact: material, `Date`, `ConsumptionType`,
  `Amount`, `InvoiceId` (null for PerDay); `ProductCode` / `ProductQuantity` columns are never
  filled.
- `PackingMaterialDailyRuns` — one processed date (unique `Date`), `MaterialsProcessed`;
  the once-per-day guard.
- `PackingMaterialAllocations` — material × `ProductCode` × `AmountPerUnit` (unique per
  material + product); unused by any calculation.

Deleting a material cascades to its logs, consumptions and allocations.

## External systems
None. Invoices are read from Heblo's own `IssuedInvoices` table, not from Shoptet directly.

## Dependencies
- **Invoices** — implements `IInvoiceConsumptionSource` (`InvoiceConsumptionSourceAdapter`):
  invoice id + `ItemsCount` for one `InvoiceDate`. Those rows are loaded by the nightly
  Shoptet → Flexi invoice import (Invoices module, process `feed-issued-invoices`, jobs
  `daily-invoice-import-eur` 04:00 / `daily-invoice-import-czk` 04:15).
- No other module reads packing-material data. Not to be confused with the **packaging**
  module (packing desk / Balení statistics) or the product-level packaging in Catalog.

## Known quirks
- **Allocations are an unfinished feature.** Table, domain, CRUD API
  (`/api/packing-materials/{id}/allocations`) and a "group by product" breakdown exist since
  #1029, but no UI creates allocations and the calculation ignores them, so product-level
  attribution is always empty.
- **`GET /api/packing-materials/consumption`** (daily breakdown by material/product/order) has
  no frontend caller.
- **Forecast is "per deduction", not strictly per day**: it averages log entries. Normally
  that is one automatic entry per day, but manual corrections down also count as a day's
  consumption and skew it; days with zero consumption are left out of the average.
- **Estimated stock drifts** from reality (rows ≠ pieces, missed days, clamping at 0); it is
  only as good as the last manual count. Details in `calc-packing-material-consumption`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/PackingMaterialsModule.cs` — DI registration
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — all endpoints and permissions
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/PackingMaterial.cs` — quantity update, forecast
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` — history query, daily-run guard
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/` — CRUD and read handlers
- `frontend/src/pages/PackingMaterialsPage.tsx`, `frontend/src/components/packing-materials/` — UI
