---
process: flow-gift-package-manufacture
kind: workflow
module: logistics
summary: Lets warehouse staff assemble gift packages (dárkové balíčky) from their components or take them apart again, shows which packages are running low from sales velocity, and books each run as Shoptet stock-up operations plus an audit log.
owns:
  - backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/**
  - backend/src/Anela.Heblo.Domain/Features/Logistics/GiftPackageManufacture/**
  - backend/src/Anela.Heblo.Persistence/Logistics/GiftPackageManufacture/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs
  - backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsGiftPackageItem.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/CriticalGiftPackagesTile.cs
  - backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs
verified_at: "5e993f9e2"
related:
  - feed-stock-up
  - calc-bundle-sales-expansion
---

# Gift package manufacture (Výroba dárkových balíčků)

## Purpose
Gift packages (*dárkové balíčky*, catalog products of type **Set** — BAL…/SET… codes) are
assembled in the warehouse from individual products. This screen answers "which packages should
we make today and how many?" and records the assembly: the components leave e-shop stock and the
finished packages arrive in it. The reverse operation, *rozebrání*, takes unsold packages apart and
returns the components.

Page `/logistics/gift-package-manufacturing` (list with severity, detail with components,
"Vyrobit" and a disassembly tab). Dashboard tile "Kritické balíčky" (`criticalgiftpackages`)
counts packages in Critical state.

## Trigger
User-driven; no scheduled work.
- `GET /api/logistics/gift-packages/available` — list with metrics.
- `GET /api/logistics/gift-packages/{code}/detail` — one package with its components.
- `POST /api/logistics/gift-packages/manufacture` — `{giftPackageCode, quantity, allowStockOverride}`.
- `POST /api/logistics/gift-packages/disassemble` — `{giftPackageCode, quantity}`.
- `GET /api/logistics/gift-packages/manufacture-log?count=10` — latest runs.

## Data flow
1. **Package list** (`LogisticsCatalogSourceAdapter`, Catalog-owned): all catalog cache aggregates
   with `ProductType.Set` → code, name, `Stock.Available`, sales in the window
   (`GetTotalSold` = B2B + B2C pieces from the catalog sales history), `StockMinSetup`,
   `OptimalStockDaysSetup` (catalog product properties).
2. **Components (BOM)**: `IManufactureClient.GetSetPartsAsync` → `FlexiManufactureClient` →
   Flexi product-set definition (*sady a komplety*), read live on every detail call. Each
   component's stock is the catalog's `WarehouseStock` (warehouse only).
3. **Manufacture** (`GiftPackageManufactureService.CreateManufactureAsync`): re-reads the detail,
   checks stock (unless overridden), then in one DB transaction
   - inserts a `GiftPackageManufactureLogs` row (`OperationType` Manufacture, quantity, override
     flag, UTC time, user) and saves it to get the id;
   - per component: a `GiftPackageManufactureItems` row and a `StockUpOperations` row
     `GPM-{logId:000000}-{component}` with amount **−(int)(required × quantity)**;
   - for the package: `GPM-{logId:000000}-{package}` with **+quantity**.
4. **Disassembly** (`DisassembleGiftPackageAsync`): same transaction shape, log
   `OperationType` Disassembly, `GPD-{logId:000000}-{package}` with −quantity and
   `GPD-{logId:000000}-{component}` with +(int)(required × quantity) per component.
5. The `StockUpOperations` rows (source `GiftPackageManufacture`) are pushed to Shoptet stock by
   `feed-stock-up` within about a minute. Nothing is written to Flexi.

## Logic & formulas
- Window: `toDate` default = now (UTC), `fromDate` default = `toDate` − 1 year;
  `days = max(whole days between, 1)`.
- `dailySales = soldInWindow × salesCoefficient / days` (`salesCoefficient` default 1.0, set on the
  page to anticipate a season).
- `suggestedQuantity = (int) max(0, dailySales × OptimalStockDaysSetup)`.
- Severity: **Critical** if `available < StockMinSetup`; else **Severe** if
  `available < suggestedQuantity`; else **Optimal**. `available` is `(int) Stock.Available`.
- `stockCoveragePercent = available / (dailySales × OptimalStockDaysSetup) × 100`, 0 when either
  factor is 0.
- Manufacture stock check: for each component `(int)(required × quantity) > WarehouseStock` →
  refused with "Nelze vyrobit … nedostatek zásob na skladě" (400, `InvalidOperation`). Quantity ≤ 0
  → `InvalidValue`.
- `allowStockOverride = true` skips the check, but only for users with
  `warehouse.stock_override.read`; others get `InsufficientPermissions`. The flag is stored on the log.
- Disassembly check: `quantity > package Stock.Available` → refused (no override).
- Units are whole pieces; component amounts are truncated by the `(int)` cast.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Permission `warehouse.gift_packages.read` / `.write` | — | page and `LogisticsController` read / manufacture + disassemble (`Feature.Warehouse_GiftPackages`) |
| Permission `warehouse.stock_override.read` | — | may manufacture despite missing components |
| `DataSource:SalesHistoryDays` | 400 (`appsettings.json`), 100 in Staging/Test | how much sales history the catalog cache holds, so the effective window behind `dailySales` |

## Runtime facts
- 2026-09-15: the ingredient check used `Stock.Available` (warehouse + transport + sklad výroby),
  showed 349 pcs where the warehouse held 184 and drove warehouse stock negative; switched to
  `WarehouseStock` — agent memory `gotcha_stock_available_includes_manufacture_warehouse` — 2026-09-15.
- Around 2026-09-16 (PR #4198, v3.152.0) the page was re-gated to `warehouse.gift_packages.read`,
  which no production permission group had; the menu item vanished for everyone except super
  users until reported on 2026-09-29. `warehouse.stock_override.read` was likewise granted to no
  group — agent memory `gotcha_new_permission_not_granted_in_prod_db` — 2026-09-30.

## Known quirks
- **Flexi (ERP) stock is not moved.** Assembly and disassembly only change Shoptet e-shop stock
  (via `feed-stock-up`) and Heblo's log; no Flexi document is created. `IManufactureClient` is used
  only to read the BOM.
- **Package-side figures still use `Stock.Available`**: the package's displayed stock, its
  severity, and the disassembly limit include goods in transport and in *sklad výroby*, so a
  package can look covered (or be "disassembled") with fewer pieces actually in the warehouse.
- **The stock check is best effort**: stock comes from the catalog cache read before the
  transaction, so two concurrent runs on the same component can both pass, and stock-up operations
  not yet reflected in the cache are not counted.
- **Truncation**: a fractional BOM quantity (e.g. 0.5 × 3 = 1.5) books 1 piece for both the check
  and the stock movement.
- **Staging under-states daily sales**: the default 1-year window divides by ~365 days while the
  staging catalog holds only 100 days of sales.
- **Shoptet refuses stock changes on product sets** (`docs/integrations/shoptet-api.md` §8.5). If
  a package is a Shoptet product set, its `GPM-…-{package}` operation ends Failed while the
  component operations succeed.
- `DisassembleGiftPackageHandler` turns any `InvalidOperationException` / `ArgumentException`
  (including EF tracking errors) into a 400 with the raw message; the manufacture handler
  deliberately does not.
- The "Kritické balíčky" tile declares no required permission, so its count is computed for users
  who cannot open the page.
- Each detail call reads the BOM live from Flexi; the transaction is opened only after those
  reads, by design.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — metrics, stock check, both transactions, GPM-/GPD- numbers
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureHandler.cs` — override permission, error mapping
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs` — which catalog fields feed the page
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` — `GetSetPartsAsync` (BOM from Flexi)
- `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureLog.cs` — log entity
- `backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs` — endpoints
- `frontend/src/components/pages/GiftPackageManufacturing/index.tsx` — page
- `docs/features/gift-package-manufacture.md` — older feature spec
