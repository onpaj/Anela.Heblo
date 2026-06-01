# "Vyrobeno" column in product stock analysis — design

## Context

The "Řízení zásob výrobků" page (Manufacturing Stock Analysis) shows per-product stock,
sales, and an overstock indicator (NS %). Its stock figure currently covers ERP stock
plus in-transit ("Transport") stock.

The company also tracks freshly manufactured product that sits in an internal
**Sklad výroby** warehouse (`ManufacturedProductInventoryItem` table) before it is
consumed into a transport box and ultimately booked into the ERP warehouse. That stock
is real, sellable product, but it is invisible to the stock analysis — so overstock %
understates how much product the company actually holds, and planning decisions are
made on incomplete numbers.

This change adds a **"Vyrobeno"** column showing the Sklad výroby quantity per product,
and makes the overstock calculation count it — exactly the way Transport is already
counted. There is no double-counting: an item leaves Sklad výroby
(`InventoryChangeType.ConsumedByTransportBox`) before it appears as Transport, which in
turn leaves before it is booked into ERP. Each unit is in exactly one stage at a time.

## Approach

`Vyrobeno` becomes a new stock dimension on `StockData`, sourced from Sklad výroby,
folded into `Available` so it propagates into every derived figure (`Total`,
`StockDaysAvailable`, overstock %) with no changes to the analysis handler or severity
calculator. The frontend gets a new column and an extended SKLAD breakdown.

## Backend changes

### 1. `StockData` — new dimension
`backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs`

- Add `public decimal Manufactured { get; set; }`.
- Change `Available` to:
  `Available => (PrimaryStockSource == StockSource.Erp ? Erp : Eshop) + Transport + Manufactured`.

`Total` (`Available + Reserve`) and `EffectiveStock` inherit the change automatically.
Because `StockData` is an in-memory domain record (not an EF entity), **no migration** is needed.

### 2. Repository aggregation method
`backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/IManufacturedProductInventoryRepository.cs`
and impl `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs`

- Add `Task<IReadOnlyDictionary<string, decimal>> GetTotalAmountByProductCodeAsync(CancellationToken cancellationToken = default)`.
- Implementation: `GroupBy(x => x.ProductCode).Select(g => new { g.Key, Total = g.Sum(x => x.Amount) })`,
  materialized into a dictionary.

### 3. `CatalogRepository` — mirror the Transport pattern
`backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`

- Inject `IManufacturedProductInventoryRepository`.
- Add a `CachedManufacturedData` cache property mirroring `CachedInTransportData`
  (lines ~585–592): `_cache` get/set, `InvalidateSourceData`, `SetLoadDateInCache`.
- Add `RefreshManufacturedData(CancellationToken ct)` mirroring `RefreshTransportData`
  (lines ~117–121): calls `GetTotalAmountByProductCodeAsync`, assigns the cache.
- In `Merge()` (near line 399, alongside `Stock.Transport`), assign
  `product.Stock.Manufactured = CachedManufacturedData.TryGetValue(product.ProductCode, out var m) ? m : 0`.
- Add a `ManufacturedLoadDate` property mirroring `TransportLoadDate` (line ~770) and
  include it in the load-date status object near line 806.

### 4. Wiring
- `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs` — add
  `Task RefreshManufacturedData(CancellationToken ct);`.
- `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` and
  `backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs` — add the
  no-op `RefreshManufacturedData` implementation.
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — register the
  refresh task next to `RefreshTransportData` (line ~125):
  `services.RegisterRefreshTask<ICatalogRepository>(nameof(ICatalogRepository.RefreshManufacturedData), (r, ct) => r.RefreshManufacturedData(ct));`.
- `backend/src/Anela.Heblo.API/appsettings.json` — add a `RefreshManufacturedData` block
  under `BackgroundRefresh:ICatalogRepository` (copy `RefreshTransportData`:
  `InitialDelay 00:00:00`, `RefreshInterval 00:05:00`, `Enabled true`, `HydrationTier 1`,
  description "Refreshes manufactured (Sklad výroby) stock data").

### 5. Analysis DTO + mapper
- `GetManufacturingStockAnalysisResponse.cs` — add `public double ManufacturedStock { get; set; }`
  to `ManufacturingStockItemDto`.
- `ManufactureAnalysisMapper.cs` — map `ManufacturedStock = (double)catalogItem.Stock.Manufactured`.

No change to `GetManufacturingStockAnalysisHandler` or `ManufactureSeverityCalculator` —
they read `Stock.Total`, which now includes `Manufactured`.

## Frontend changes

### 6. SKLAD breakdown
`frontend/src/api/hooks/useManufacturingStockAnalysis.ts` — `formatWarehouseStock`:

- Build the breakdown from non-zero parts: primary stock, then `transportStock`, then
  `manufacturedStock`.
- If both `transportStock` and `manufacturedStock` are 0, show just the total.
- Otherwise show `total (a+b+c)` with only the non-zero secondary parts, e.g.
  `15 (5+7+3)`, or `8 (5+3)` when transport is 0.

### 7. New column
`frontend/src/components/pages/ManufacturingStockAnalysis.tsx` — add a `vyrobeno` column
to the `columns` array **immediately after** the `currentStock` ("Sklad") column.

- `id: 'vyrobeno'`, `header: 'Vyrobeno'`, `align: 'right'`, widths/`cellClassName`
  matching the `planned` column.
- `renderCell`: show `formatNumber(item.manufacturedStock, 0)` in bold when `> 0`,
  otherwise the gray `—` dash (same as `planned`).
- Non-sortable — omit `sortBy` (no new `ManufacturingStockSortBy` value; keeps scope minimal).

The TypeScript `ManufacturingStockItemDto` gains `manufacturedStock` automatically when
the OpenAPI client regenerates on build.

## Testing

**Backend (xUnit)**
- `StockData`: `Available` and `Total` include `Manufactured`; `EffectiveStock` too.
- `ManufacturedProductInventoryRepository.GetTotalAmountByProductCodeAsync`: sums
  multiple lots/rows per product code; empty table returns empty dictionary.
- `ManufactureAnalysisMapper`: `ManufacturedStock` mapped from `Stock.Manufactured`.
- `CatalogRepository`: `Merge` assigns `Stock.Manufactured` from cache; missing code → 0.

**Frontend (Jest/RTL)**
- `formatWarehouseStock`: total-only when transport+manufactured 0; `(p+t+m)`,
  `(p+m)`, `(p+t)` variants.
- `ManufacturingStockAnalysis`: Vyrobeno column renders the value when > 0 and `—` when 0.

**End-to-end verification**
1. Backend: `dotnet build` + `dotnet format`; `dotnet test` for touched projects.
2. Frontend: `npm run build` + `npm run lint`; `npm test` for touched files.
3. Run the app, open "Řízení zásob výrobků". Confirm:
   - the new "Vyrobeno" column sits right after "Sklad" and shows Sklad výroby quantities;
   - the "Sklad" cell breakdown includes the manufactured part for products that have one;
   - NS and NS % increase for products with Sklad výroby stock (compared to before),
     consistent with how Transport already affects them.
