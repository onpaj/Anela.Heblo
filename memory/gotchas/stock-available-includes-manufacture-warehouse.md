# `Stock.Available` counts the manufacture warehouse — never use it as "can I pick this?"

## Symptom
"Zásoby produktů" shows a CELKEM that the visible columns cannot explain: SKLADEM 184,
TRANSPORT/REZERVA/KARANTÉNA all "-", CELKEM 349. The gift-package modal then showed 349 as
SKLADEM for that component, reported "Všechny komponenty jsou dostupné", and manufacturing the
package drove the warehouse negative.

## Root cause
`StockData.Available` (`backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs`) is a
**three-way sum**, and two of its three terms are not in the warehouse:

```
Available = WarehouseStock (Erp or Eshop, per PrimaryStockSource) + Transport + Manufactured
Total     = Available + Reserve
```

`Manufactured` is the "sklad výroby" figure: the sum of `ManufacturedProductInventoryItems.Amount`,
written when a manufacture order moves to **Completed**
(`UpdateManufactureOrderStatusHandler` → `ManufactureInventoryWriteDownService`). Those goods sit at
production until someone packs them into a transport box — they cannot be picked from the warehouse.

`StockDto` carries no `manufactured` field, so the stock table has no column for it. That is why
CELKEM looks impossible: the missing 165 pcs were the manufacture warehouse.

## How to apply
- **Deducting from the warehouse? Validate against `StockData.WarehouseStock`, never `Available`.**
  Any flow that books a stock-down against ERP/e-shop but checks `Available` is checking one pool and
  deducting from another. That is exactly how gift-package manufacture went negative.
- `WarehouseStock` follows `PrimaryStockSource`: the e-shop figure for products in the e-shop feed,
  ERP otherwise. **Do not hardcode `Stock.Eshop`** — anything not sold online (packaging, e.g.
  `DAR0010`) has `Eshop == 0` and its real figure only in `Erp`, so an e-shop-only check reads 0 and
  blocks everything.
- `Available`/`Total` remain correct for *planning* views (purchase planning, manufacturing stock
  analysis, dashboards), which legitimately want everything on hand anywhere.
- A frontend `disabled={...}` is not a stock guard. Before this fix the only thing preventing an
  over-consuming gift package was a greyed-out button; the handler accepted any POST, and
  `allowStockOverride` was stored on the log but never read.

## Still open
`LogisticsGiftPackageItem.AvailableStock` (the package's own "Aktuální sklad", its severity
/"Doporučeno" figures, and the **disassembly** quantity check at
`GiftPackageManufactureService.DisassembleGiftPackageAsync`) still uses `Stock.Available`, so
disassembly can be authorised against packages sitting in the manufacture warehouse.

## Related: the gates disagreed too
Until 2026-09-16 the screen's three gates used different features — menu `warehouse.packaging.read`,
page `warehouse.logistics.read`, button `warehouse.logistics.write` — so only Správce could use it
end to end. Now all three are `Warehouse_GiftPackages`, and `allowStockOverride` needs the separate
`Warehouse_StockOverride` capability. **Seed groups do not update deployed environments**: grant the
new roles at `/admin/access` or nobody but `super_user` can open the screen.

## References
- Fix: `WarehouseStock` on `StockData`, `LogisticsCatalogItem.WarehouseStock`,
  `EnsureIngredientsAreInStock` in `GiftPackageManufactureService`
- `docs/features/gift-package-manufacture.md` → "Stock Validation"
