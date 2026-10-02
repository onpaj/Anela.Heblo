---
process: module-logistics
kind: module
module: logistics
summary: Moves finished goods from production to the e-shop warehouse in transport boxes, assembles and disassembles gift packages, and holds the free-gift badge setting for the packing list.
owns: []
verified_at: "5e993f9e2"
related:
  - flow-transport-box
  - flow-gift-package-manufacture
  - feed-stock-up
---

# Logistics (Logistika)

## Purpose
The warehouse side between production and the e-shop:
- **Transport boxes** (*transportní boxy*): finished products leave the manufacturing warehouse
  (*sklad výroby*) in numbered crates `B001`…`B999`; when a box is received, the e-shop stock in
  Shoptet rises by its contents. Boxes can also be parked in a side store (Reserve, with a
  location) or held back (Quarantine).
- **Gift packages** (*dárkové balíčky*): staff see which packages are running low against sales
  and assemble them from components, or take unsold ones apart; both move Shoptet stock.
- **Free-gift badge** (*dárek k objednávce*): one setting that marks large CZK orders on the
  printed picking list so packers add a gift.

## Users & screens
- `/logistics/transport-boxes` — box list (filters by code, state, product; state counts) and
  box detail (items, history, state buttons, note).
- `/logistics/receive-boxes` — receive a box by scanning its code.
- `/logistics/gift-package-manufacturing` — package list with severity, detail, assemble,
  disassemble.
- Mobile terminal: `/terminal/box-fill` (open/resume a box by scanning, add items from sklad
  výroby, send), `/terminal/box-check` (look up a box's contents), `/terminal/receive`.
- Gift badge setting: the gifts tab of `/customer/expedition-settings?tab=gifts` (`GiftsTab.tsx`), API
  `GET/PUT /api/gift-settings`.
- Dashboard tiles: "Boxy v přepravě" (`intransitboxes`), "Boxy přijaté" (`receivedboxes`),
  "Boxy v chybě" (`errorboxes`), "Kritické balíčky" (`criticalgiftpackages`).
- Permissions: `warehouse.logistics.read/.write` (boxes, gift setting),
  `warehouse.gift_packages.read/.write` (gift packages), `warehouse.stock_override.read`
  (assemble despite missing components).

## Processes
- `flow-transport-box` — box lifecycle New → Opened → InTransit/Reserve/Quarantine → Received →
  Stocked/Error → Closed; filling consumes sklad výroby, receiving stages stock-up operations, a
  1-minute BackgroundRefresh task (`ITransportBoxCompletionService.CompleteReceivedBoxesAsync`)
  closes received boxes. User-driven.
- `flow-gift-package-manufacture` — assemble/disassemble gift packages, severity from sales,
  stock-up operations + audit log. User-driven.
- `feed-stock-up` (Catalog module) — pushes the operations created by both flows to Shoptet.

Plain CRUD (no process doc): box note (`PUT /api/transport-boxes/{id}/description`), box list and
summary queries, the manufacture log query, and the gift badge setting. The setting is a single
row (`GiftSettings`, id 1): enabled, threshold in CZK, text ≤ 50 chars (threshold > 0 and text
required when enabled). The expedition picking list (`ShoptetApiExpeditionListSource`, owned by
the expedition-list module) prints the text on orders whose total **with VAT** in **CZK** is ≥ the
threshold; other currencies never get it.

No Hangfire jobs live in this module.

## Data owned
- `public."TransportBoxes"` — one physical box trip: code, state, location, note, timestamps.
- `public."TransportBoxItems"` — box contents: product, amount (decimal pieces), lot, expiration,
  source inventory row.
- `public."TransportBoxStateLogs"` — every state change with user, time and error text.
- `public."GiftPackageManufactureLogs"` — one assembly or disassembly run (package, quantity,
  override flag, user, time, `OperationType`).
- `public."GiftPackageManufactureItems"` — components consumed or returned by a run.
- `public."GiftSettings"` — the free-gift badge setting (single row).

Writes into other modules' tables: `StockUpOperations` (Catalog; `BOX-`, `GPM-`, `GPD-` rows)
and `ManufacturedProductInventoryItems` / its log (Manufacture; consumed and restored when filling
boxes).

## External systems
- **Shoptet** — indirectly: stock movements via `feed-stock-up` (`PATCH /api/stocks/{id}/movements`).
- **Flexi** — read only: gift package BOM from the product-set definition (*sady a komplety*)
  via `IManufactureClient.GetSetPartsAsync`. Logistics writes nothing to Flexi.

## Dependencies
- Reads **Catalog** (cache aggregates: set products, stock, sales history, min/optimal stock
  setup) through `ILogisticsCatalogSource`, and creates/queries stock-up operations through
  `ILogisticsStockOperationService` / `ILogisticsStockOperationQueryService`.
- Reads/writes **Manufacture** inventory (sklad výroby) through `IInventoryReservationService`.
- **Catalog** reads box contents as its transport / reserve / quarantine stock pools
  (`ICatalogTransportSource`).
- **Expedition list** reads the gift setting; it also reaches the picking-list printer through
  `IExpeditionPickingSource` → `LogisticsExpeditionPickingAdapter` → `IPickingListSource`
  (a pass-through registered here; the picking list itself belongs to the expedition-list module).
- Domain/persistence types that sit under `Logistics` folders but belong to other modules:
  `CarrierCoolingSetting` (carrier cooling), `StockTakingResult` / `StockTakingRecords` (Catalog
  stock taking), `IWeatherForecastClient`, `IShippingMethodCatalog`.

## Known quirks
- Logistics never writes Flexi: assembling a gift package or receiving a box changes Shoptet
  stock only. ERP stock follows only through whatever Anela books in Flexi separately.
- A box whose stock-up failed stays in Error after a successful retry; staff move it to Stocked
  by hand (see `flow-transport-box`).
- Gift-package package-side numbers (stock, severity, disassembly limit) use `Stock.Available`,
  which includes goods in transport and sklad výroby; only the component check uses warehouse stock.
- New permissions are not granted to existing production groups automatically: the
  gift-package page disappeared for everyone after PR #4198 (reported 2026-09-29), and
  `warehouse.stock_override.read` had no holders — agent memory, 2026-09-30.
- `docs/features/gift-package-manufacture.md` and `docs/features/complete-received-boxes-job.md`
  are older specs; the process docs above reflect the current code.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — DI, side effects, tiles, refresh task
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs` — box state machine
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — gift packages
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/` — gift badge setting
- `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs`, `LogisticsController.cs`, `GiftSettingsController.cs` — API
- `frontend/src/components/pages/TransportBoxList.tsx`, `frontend/src/components/pages/GiftPackageManufacturing/`, `frontend/src/components/terminal/` — screens
