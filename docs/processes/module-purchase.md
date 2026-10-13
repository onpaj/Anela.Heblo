---
process: module-purchase
kind: module
module: purchase
summary: Buying side of Anela - purchase orders to suppliers, material stock coverage for deciding what to order, and the nightly upkeep of Flexi purchase prices.
owns: []
verified_at: "5e993f9e2"
related:
  - calc-margins
  - calc-bundle-sales-expansion
---

# Purchase (Nákup)

## Purpose
Supports the people who buy raw materials (suroviny), labels (etikety), packaging (obaly) and bought-in goods
(zboží). It answers three questions:
- **What should I order, and how urgently?** Stock coverage per material, with a severity and a suggested
  quantity.
- **What have we ordered and not received yet?** Purchase orders kept in Heblo, whose open lines count as
  "Ordered" stock in the catalog and in purchase planning.
- **What does an item really cost us to buy or make?** A nightly job that keeps the Flexi purchase price (`nakupCena`)
  of every material, semi-product and product in line with the actual stock value.

## Users & screens
Sidebar section **Nákup**:
- **Nákupní objednávky** — `/purchase/orders`: list (default: active orders, newest first), detail with status buttons
  and the "Faktura přijata" toggle, and a create/edit form with a supplier search (Flexi address book) and a material
  picker (catalog materials and goods, Catalog's `GetMaterialsForPurchase`). Permission `Purchase_PurchaseOrders`.
- **Zásoby materiálu** — `/purchase/stock-analysis`: stock coverage table with filters (category Suroviny / Etikety
  / Obaly / Vše, severity, only configured), search, export, and a browser-only purchase planning list (max 20
  items) that can be carried into a new order. Permission `Purchase_PurchaseStock`.
- **Klasifikace faktur** (`/purchase/invoice-classification`) appears in this section, but it belongs to the
  InvoiceClassification module, not to Purchase.

Dashboard tiles (category Purchase): **"Materiál NS < 20%"** (`lowstockefficiency`, auto-shown) and **"Suma
nákupních objednávek"** (`purchaseordersintransit`, count and value of in-transit orders, manual show).

The module has no MCP tools of its own. The MCP tool `GetMaterialsForPurchase` lives in Catalog.

## Processes
- `feed-purchase-price-recalculation` — Hangfire `purchase-price-recalculation`, daily 02:00 Europe/Prague (also
  manual). Writes stock prices of materials and goods to Flexi `nakupCena`, then triggers Flexi BoM roll-ups for
  semi-products, then for products and sets.
- `calc-purchase-stock-analysis` — on demand (page, export, dashboard tile). Consumption rate, days to stock-out,
  NS%, severity and recommended order quantity for every material and goods item.
- `flow-purchase-order` — user-driven. Draft → In transit → Completed (plus the invoice flag). Stored only in
  Heblo; open lines feed the catalog's Ordered quantity every 5 min.

Plain reads with no process doc: supplier search (`GET /api/suppliers/search`), purchase order list, detail and
history (`GET /api/purchase-orders…`).

## Data owned
- `public."PurchaseOrders"` — one purchase order: number (unique), Flexi supplier id and name, dates, contact
  channel, status (stored as text), invoice-acquired flag, notes, audit columns.
- `public."PurchaseOrderLines"` — one ordered material: `MaterialId` (catalog product code), name, quantity, unit
  price, notes. Material containers can point to a line (`MaterialContainers.PurchaseOrderLineId`).
- `public."PurchaseOrderHistory"` — audit rows: creation, order-number, status and invoice-flag changes.
- In-memory cache `all_suppliers` (Flexi supplier list, 2 h).
- In Flexi, not in Heblo: the ceník `nakupCena` values written by the nightly job.

## External systems
| System | What | Direction |
|---|---|---|
| Flexi (ABRA FlexiBee) | Address book contacts of type Supplier / SupplierAndCustomer (`IContactListClient`) | read |
| Flexi | Price list, user query 41 (`uzivatelsky-dotaz/41`) — `idcenik`, `nakupCena`, BoM id | read |
| Flexi | `stav-skladu-k-datu`, warehouses 5 (MATERIAL) and 4 (ZBOZI) — average stock price | read |
| Flexi | `PUT cenik/{id}.json` (`nakupCena`) and kusovník `prepocti-nakupni-cenu` | **write** (nightly job) |

All other inputs (consumption, sales, purchase history, min/optimal stock settings, stock levels) come from Flexi
indirectly, through the Catalog module's caches.

## Dependencies
- **Reads from Catalog** (through contracts that Catalog implements: `IMaterialCatalogService`,
  `IPurchasePriceSyncSource`, `IPurchasePriceRecalculationService`): merged catalog items, stock, consumption,
  sales, purchase history, attributes, BoM ids.
- **Read by Catalog**: `ICatalogPurchaseSource` (implemented in Purchase by `PurchaseCatalogSourceAdapter`) supplies
  `Stock.Ordered`, which the purchase stock analysis and any catalog view of Ordered or EffectiveStock depend on.
  (Manufacturing batch planning has its own EffectiveStock = current stock + planned and does not use it.)
- **Read by Catalog inventory**: material containers validate their `PurchaseOrderLineId` against Purchase.
- **Feeds margins** via Flexi: `calc-margins` falls back to ceník `nakupCena` for the M0 material cost of items
  without manufacture history.

## Known quirks
- **Ordered over-counts.** Lines stay "Ordered" until the invoice flag is ticked, even on Completed orders, and Draft
  orders count too (see `flow-purchase-order`).
- **The purchase-order FluentValidation validators never run.** They are registered without a `ValidationBehavior`.
  Only DataAnnotations and domain checks protect the data.
- **The recommended order quantity ignores open orders.** It uses Available, not EffectiveStock (see
  `calc-purchase-stock-analysis`).
- **A cold catalog at 02:00 makes the nightly price job a silent no-op**, reported as Success.
- `POST /api/purchase-orders/recalculate-purchase-price` and the frontend hook `useRecalculatePurchasePrice`
  exist, but no page calls them.
- The `Received` order state exists in the backend but not in the UI.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — DI registrations, tiles, the `ICatalogPurchaseSource` adapter
- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs` — order aggregate and state machine
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/` — one folder per use case
- `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/Jobs/PurchasePriceRecalculationJob.cs` — the only recurring job
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs` — Catalog-side implementation of the Purchase contracts
- `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`, `PurchaseStockAnalysisController.cs`, `SuppliersController.cs` — HTTP surface
- `frontend/src/components/pages/PurchaseOrderList.tsx`, `PurchaseStockAnalysis.tsx` — the two pages
