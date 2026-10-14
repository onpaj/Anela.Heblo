---
process: sync-catalog-stock
kind: sync
module: catalog
summary: Loads every stock position of a product into the catalog cache — Flexi warehouse stock, Shoptet e-shop stock, goods in transport/reserve/quarantine boxes, open purchase orders, planned and finished-but-not-stocked manufacture.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClientOptions.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/ErpStock.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/EshopStock.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IErpStockClient.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IEshopStockClient.cs
  - backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs
verified_at: "5e993f9e2"
related: [calc-catalog-merge, feed-stock-up, flow-stock-taking]
---

# Catalog stock positions

## Purpose
Answers "how much of product X do we have, and where?". The catalog shows stock split into the
e-shop figure (Shoptet, what customers see), the ERP figure (Flexi warehouses), goods on the way
in transport boxes (Transport), boxes put aside (Rezerva) or held back (Karanténa), finished
manufacture not yet stocked in (sklad výroby), quantities ordered from suppliers (Objednáno) and
quantities planned in manufacture orders (Plánováno). These feed the catalog list/detail, stock
analysis, purchase and manufacture planning, low-stock tiles and the MCP catalog tools.

## Trigger
BackgroundRefresh tasks, all tier 1, first run at start-up:

| Task id | Every | Cache key |
|---|---|---|
| `ICatalogRepository.RefreshErpStockData` | 10 min | `CachedErpStockData` |
| `ICatalogRepository.RefreshEshopStockData` | 5 min | `CachedEshopStockData` |
| `ICatalogRepository.RefreshTransportData` | 5 min | `CachedInTransportData` |
| `ICatalogRepository.RefreshReserveData` | 5 min | `CachedInReserveData` + `CachedInQuarantineData` |
| `ICatalogRepository.RefreshOrderedData` | 5 min | `CachedOrderedData` |
| `ICatalogRepository.RefreshPlannedData` | 5 min | `CachedPlannedData` |
| `ICatalogRepository.RefreshManufacturedData` | 5 min | `CachedManufacturedData` |

On demand: changing a manufacture order (`UpdateManufactureOrderHandler`,
`UpdateManufactureOrderStatusHandler`) refreshes planned data immediately via
`IManufactureCatalogSource.RefreshPlannedDataAsync`. An ERP stock taking patches one product's
ERP stock and lots directly in the caches (see `flow-stock-taking`).

## Data flow
1. **ERP stock** (`FlexiStockClient.ListAsync`): three parallel Flexi `stav-skladu-k-datu`
   (stock to date, today) calls — warehouse **5** (material) keeping type Material, warehouse
   **20** (semi-products) keeping SemiProduct, warehouse **4** (ZBOZI) keeping Product and
   Goods. Union, drop names containing "archiv". Each row carries code, name, suffix, type id,
   stock, average stock price, MOQ, lots/expiration flags, volume, weight, note, supplier.
   Called through `CatalogResilienceService`.
2. **E-shop stock** (`ShoptetStockClient.ListAsync`): `GET StockClient:Url` — the Shoptet
   product CSV export (`https://www.anela.cz/export/products.csv-…`), windows-1250, `;`
   separated. Columns by index: 0 code, 1 pair code, 2 name, 3 default image, 4 image,
   15 name suffix, **25 stock**, 26 location, 27 weight, 28 height, 29 depth, 30 width,
   31 atypical shipping. Called through `CatalogResilienceService`.
3. **Transport / Reserve / Quarantine** (`LogisticsCatalogTransportSourceAdapter`): Heblo
   `TransportBoxes` with items, summed per product code (item amount cast to int):
   Transport = boxes in state InTransit, Received or Opened; Reserve = Reserve; Quarantine =
   Quarantine.
4. **Ordered** (`PurchaseOrderRepository.GetOrderedQuantitiesAsync`): sum of
   `PurchaseOrderLines.Quantity` per material code over purchase orders that are Draft or
   InTransit **or** have `InvoiceAcquired = false`.
5. **Planned** (`ManufactureOrderRepository.GetPlannedQuantitiesAsync`): sum of
   `ManufactureOrderProducts.PlannedQuantity` per product code over orders not in Draft,
   Completed or Cancelled.
6. **Manufactured** (`ManufacturedProductInventoryRepository.GetTotalAmountByProductCodeAsync`):
   sum of positive `Amount` per product code of the sklad výroby inventory (goods written there
   when a manufacture order completes).
7. Each result replaces its cache key and schedules a catalog merge (`calc-catalog-merge`).

## Logic & formulas
- Units: pieces for products/goods, the Flexi unit (often g or kg) for materials and
  semi-products. No VAT involved.
- `WarehouseStock` = e-shop stock if the code is in the Shoptet CSV, else ERP stock.
  `Available = WarehouseStock + Transport + Manufactured`; `Total = Available + Reserve`;
  Quarantine is shown but counted nowhere; `EffectiveStock = Available + Ordered` (purchase
  planning). Planned is never part of Total.
- `Stock.StockPrice` = ERP average stock price, only when ERP stock > 0.
- Products in no box / order get 0 for those figures.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `StockClient:Url` | `https://www.anela.cz/export/products.csv-xxxxxxxxxx` (placeholder; real token in Key Vault) | Shoptet product CSV export |
| `StockClient:TimeoutSeconds` | 8 | Per-attempt timeout of the Shoptet stock HttpClient |
| `BackgroundRefresh:ICatalogRepository:Refresh{ErpStock,EshopStock,Transport,Reserve,Ordered,Planned,Manufactured}Data` | see Trigger, tier 1 | Intervals |
| Flexi warehouse ids | 5 / 20 / 4 (constants in `FlexiStockClient`) | Material / semi-product / product warehouses |

## Runtime facts
- A gift-package run on 2026-09-15 checked `Available` (warehouse + transport + sklad výroby:
  349 pcs) while the warehouse held 184, and drove stock negative — agent memory
  `gotcha_stock_available_includes_manufacture_warehouse` — 2026-09-15.

## Known quirks
- **`Available` is not "can be picked now"**: it includes transport and sklad výroby. Anything
  that deducts warehouse stock must use `WarehouseStock`. The API `StockDto` exposes no
  `manufactured` field, so the list's total cannot be reconciled from its own columns.
- **Ordered includes received orders without an invoice.** The `!InvoiceAcquired` branch keeps a
  Received/Completed order's lines in Ordered until the invoice is ticked, while the goods may
  already be in ERP stock — `EffectiveStock` can then count them twice. (Read from code.)
- **Planned can mix units**: `CreateManufactureOrderHandler` writes a "direct output" row keyed by
  the semi-product code in grams, and the planned query does not exclude it — agent memory
  `gotcha_stock_planned_disjoint_from_total`.
- **Transport amounts are truncated to int** per box item before summing.
- **ERP stock decides which codes exist at all** (only the first merge seeds the list — see
  `calc-catalog-merge`). A product missing from the e-shop CSV falls back to ERP stock only on
  restart; until then it keeps its last e-shop figure.
- The Shoptet CSV is read by fixed column index; a column added or reordered in the Shoptet
  export template silently shifts every field.
- `RefreshPlannedData` is the only stock source refreshed on demand; transport box and purchase
  order changes wait for the next 5-minute tick.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs` — the seven refresh methods
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockClient.cs` — warehouses, type filter, "archiv" filter
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` — CSV download and column map (`StockDataMap`)
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs` — box states per figure
- `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs` — `GetOrderedQuantitiesAsync`
- `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs` — `GetPlannedQuantitiesAsync`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs` — `IsIn*Predicate`
