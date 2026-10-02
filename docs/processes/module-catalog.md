---
process: module-catalog
kind: module
module: catalog
summary: The product catalog — one in-memory view of every product, material, semi-product and goods code combining Flexi, Shoptet and Heblo data (stock, prices, history, margins) — plus stock taking, stock-up to Shoptet, product weights and material container/lot labels.
owns: []
verified_at: "5e993f9e2"
related: [calc-catalog-merge, sync-catalog-stock, sync-catalog-history, sync-catalog-prices, sync-catalog-master-data, calc-bundle-sales-expansion, calc-margins, feed-stock-up, feed-product-weight, job-product-export-download, flow-stock-taking, flow-lots-and-material-containers, flow-product-composition-order]
---

# Catalog

## Purpose
Answers "what is this product, how much of it do we have and where, what does it sell for and
cost, how has it sold, and how much does it earn?" for every code Anela trades or makes:
finished products (Výrobek), goods (Zboží), materials (Materiál), semi-products (Polotovar) and
gift packages (BAL/SET → type Set). Heblo has no product master table — Flexi (ERP) is the master
and the catalog is a merged, in-memory cache that also brings in Shoptet (e-shop) stock and
prices and Heblo's own data (transport boxes, purchase and manufacture orders, stock takings,
difficulty settings). Almost every other module reads products through it.

The module also owns a few write processes: e-shop stock taking, stock-up movements to Shoptet,
nightly product-weight write-back to Flexi, recipe ingredient order to Flexi, and the material
container / lot label workflow.

## Users & screens
| Route | Czech UI | What people do |
|---|---|---|
| `/catalog` | Katalog | Search/filter all codes; detail tabs: basic info, stock, sales, purchase, consumption, manufacture, composition (reorder recipe), margins, manufacture difficulty, usage in other recipes |
| `/products/statistics` | Statistiky | Sales/stock statistics per product over a period |
| `/products/margins` | Marže | Margin cascade M0–M3 (`calc-margins`) |
| `/logistics/inventory` | Zásoby produktů | Stock overview and e-shop stock taking with days since last count (`flow-stock-taking`) |
| `/logistics/warehouse-statistics` | (no menu item) | Warehouse statistics over Product + Goods |
| `/stock-up-operations` | Naskladnění | Monitor, retry or accept Shoptet stock-up operations (`feed-stock-up`) |
| `/manufacturing/material-containers` | Šarže | Container labels, discard, lot-label calibration (`flow-lots-and-material-containers`) |
| `/terminal/po/…`, `/terminal/freeform` | Terminál | Bind scanned containers to material + lot |

Dashboard tiles: "K přeskladnění (S/R/T)" (low stock), "Expirace surovin", "Materiály
inventarizované (30dní)", "Produkty inventarizované (30dní)", "Inventury surovin", "Inventury
obalů a etiket", "Produkty podle stáří inventury".

MCP tools (`CatalogMcpTools`): `GetCatalogList`, `GetCatalogDetail`, `GetProductComposition`,
`GetMaterialsForPurchase`, `GetAutocomplete`, `GetProductUsage`, `GetWarehouseStatistics`,
`GetProductMargins`.

API: `api/Catalog`, `api/ProductMargins`, `api/StockTaking`, `api/StockUpOperations`,
`api/lots`, `api/material-containers`. Permissions: Products_Catalog,
Manufacture_MaterialInventory, Manufacture_MaterialContainers, Manufacture_LabelCalibration.

## Processes
- `calc-catalog-merge` — assembles the product cache from all sources; debounced after each source load, 4 h validity.
- `sync-catalog-stock` — ERP/e-shop stock, transport/reserve/quarantine, ordered, planned, sklad výroby; BackgroundRefresh 5–10 min.
- `sync-catalog-history` — purchase (query 19), consumption (query 21), manufacture receipts; hourly.
- `calc-bundle-sales-expansion` — sales history (query 37) with gift packages expanded into components; hourly.
- `sync-catalog-prices` — Shoptet price list (incl. actions), Flexi price list (query 41), Heureka URL; 30 min / hourly.
- `sync-catalog-master-data` — Flexi attributes (query 38) and lots, Heblo stock takings and difficulty; 5 min / hourly.
- `calc-margins` — M0–M3 cost providers, cost pools, margin history; hourly / 2 h / 4 h.
- `feed-stock-up` — relative stock movements to Shoptet from boxes and gift packages; every minute.
- `feed-product-weight` — BoM weight → Flexi ceník; Hangfire `product-weight-recalculation` 02:00 + manual.
- `job-product-export-download` — nightly product export snapshot to Azure Blob; Hangfire `product-export-download` 02:00.
- `flow-stock-taking` — e-shop stock taking to Shoptet; ERP stock-taking cache patch.
- `flow-lots-and-material-containers` — container and lot labels via CUPS/Zebra.
- `flow-product-composition-order` — recipe ingredient order and phases → Flexi BoM.

Plain CRUD without a process doc: manufacture difficulty settings (Heblo table, refreshes one
product in the cache — described in `sync-catalog-master-data`), Heblo lots (create/update/delete),
container discard, read-only lists and statistics.

## Data owned
- In-memory cache (`IMemoryCache`): `CatalogData_Current` / `_Stale` and ~20 source keys
  (`Cached*Data`) — see `calc-catalog-merge`. Rebuilt on every start.
- `public."StockUpOperations"` — one Shoptet stock movement per document number.
- `public."StockTakingRecords"` — one row per successful stock taking (Eshop or Erp).
- `public."ManufactureDifficultySettings"` — difficulty value per product with validity period.
- `public."Lots"` — Heblo lot register (material code + lot code, expiration, received date).
- `public."MaterialContainers"` — labelled containers (`M########`), status, material, lot,
  amount, optional purchase order line; sequence `material_container_internal_seq`.
- `public."LotLabelCalibrations"`, `public."PrinterMediaStates"` — single-row printer settings.
- Azure Blob: `products_*.csv` snapshots (container from `ProductExportOptions:ContainerName`).

## External systems
| System | Read | Write |
|---|---|---|
| Flexi (ERP) | stock-to-date (warehouses 4/5/20), user queries 19, 21, 37, 38, 41, stock-movement lines, lots, BoM (kusovník) and set parts | ceník weight (`feed-product-weight`), BoM line order/phase (`flow-product-composition-order`); ERP stock taking via the Manufacture module |
| Shoptet | product CSV export (stock), `GET /api/pricelists/{id}`, `GET /api/stocks/{id}/supplies` | `PATCH /api/stocks/{id}/movements` — deltas (`feed-stock-up`), absolute real stock (`flow-stock-taking`) |
| anela.cz Heureka XML feed | product URLs | — |
| CUPS → Zebra ZD411 | — | raw ZPL labels |
| Azure Blob Storage | — | product export snapshots |

## Dependencies
Reads from: Logistics (transport boxes), Purchase (purchase orders), Manufacture (manufacture
orders, sklad výroby inventory), Shared CostPools / Flexi ledger (margins), FileStorage (blob
download), FeatureFlags.

Read by (through adapters in `Features/Catalog/Infrastructure`): Purchase
(`PurchaseMaterialCatalogAdapter`, purchase-price sync/recalculation), Manufacture
(`CatalogManufactureCatalogSourceAdapter`, ERP stock-taking sync), Logistics (gift packages,
stock operations), ShoptetOrders packing (`CatalogPackingProductSourceAdapter`), Analytics
(`CatalogAnalyticsSourceAdapter`), FinancialOverview stock value
(`FinancialOverviewStockValueAdapter`), DataQuality checks (ERP/e-shop stock, stock takings,
lot stock, stock operations), Pricing (margins and prices).

## Known quirks
- **New Flexi products appear only after an app restart**: the merge seeds its product list
  from ERP stock only once (`calc-catalog-merge`).
- **A product with an empty Flexi product group (`skupZboz`) is invisible** in Heblo, with its
  sales and margins (`calc-catalog-merge`).
- **`Stock.Available` includes transport and sklad výroby**; use `WarehouseStock` for anything
  picked from the warehouse (`sync-catalog-stock`).
- **Stock-up deltas have no duplicate protection in Shoptet**; retrying a submitted operation can
  double the movement (`feed-stock-up`).
- Staging/Test load only 100 days of history, so YoY views look empty there.
- Two unrelated "lot" concepts: Flexi lots (stock) vs. Heblo `Lots` (labels).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — registrations and refresh tasks
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` — how a product is assembled
- `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs` — the product model
- `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs` — main API
- `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs` — MCP tools
- `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/InventoryModule.cs` — lots/containers sub-module
