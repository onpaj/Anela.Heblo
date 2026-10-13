---
process: flow-purchase-order
kind: workflow
module: purchase
summary: Buyers record purchase orders to suppliers in Heblo (Draft → In transit → Completed, plus an invoice-received flag); open order lines become the "Ordered" stock that purchase and manufacturing planning count on.
owns:
  - backend/src/Anela.Heblo.Domain/Features/Purchase/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/SearchSuppliers/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs
  - backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs
  - backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs
  - backend/src/Anela.Heblo.API/Controllers/SuppliersController.cs
  - frontend/src/components/pages/PurchaseOrder*.tsx
  - frontend/src/components/purchase-orders/**
  - frontend/src/api/hooks/usePurchaseOrders.ts
verified_at: "5e993f9e2"
related:
  - calc-purchase-stock-analysis
---

# Purchase orders (Nákupní objednávky)

## Purpose
This is where Anela records what it has ordered from suppliers: materials, packaging, labels and goods.
Each order has a supplier, order date, expected delivery, how the order was placed (e-mail, phone, WhatsApp, in person,
e-shop, other), and lines with the material, quantity and unit price. The order moves through its
states, and staff tick when the supplier's **invoice has arrived** (Faktura přijata).

These orders exist **only in Heblo**. They are not sent to the supplier and not written to Flexi or Shoptet; the actual
goods receipt and invoice are booked in Flexi separately. Their effect in Heblo is that open order lines
become the catalog's **Ordered (Objednáno)** quantity. Ordered feeds `EffectiveStock` in purchase planning (`calc-purchase-stock-analysis`) and other
catalog-based stock views.

Pages: **Nákup → Nákupní objednávky** (`/purchase/orders`: list, detail, create/edit form). Dashboard tile
**"Suma nákupních objednávek"** (`purchaseordersintransit`, manual show): count and total value of In-transit
orders. Permission: `Purchase_PurchaseOrders` (read; Write for every change).

## Trigger
User-driven. The states (`PurchaseOrderStatus`) and allowed transitions:

| From | To | How |
|---|---|---|
| (new) | Draft (Návrh) | "create order" form, `POST /api/purchase-orders` |
| Draft | InTransit (V přepravě) | detail page status button, `PUT /api/purchase-orders/{id}/status` |
| InTransit | Completed (Dokončeno) | detail page status button |
| InTransit | Received | API only; no UI label or button |
| Received | Completed | API only |

Any other transition returns `StatusTransitionNotAllowed`. The **invoice-acquired** flag is a separate toggle
(`PUT /api/purchase-orders/{id}/invoice-acquired`) that works in any state. A Completed order is read-only
(lines, header, order number); only the invoice flag can still change. There is no delete.

## Data flow
1. **Supplier lookup**: `GET /api/suppliers/search?searchTerm=…` (`FlexiSupplierRepository`) reads the Flexi address book
   (`IContactListClient`, relation types Supplier and SupplierAndCustomer) once and caches the whole list in memory under
   `all_suppliers` for 2 h (1 h sliding). Filtering by name or code happens in memory.
2. **Create**: the handler resolves the supplier by Flexi contact id (`SupplierNotFound` if it is not in the cached
   list). It generates an order number if none is given, sets the status to Draft and adds the lines. Each line's material name is
   taken from the catalog (`IMaterialCatalogService.GetByIdAsync`), falling back to the name sent by the
   client, then to "Unknown Material". It saves to `public."PurchaseOrders"`, `public."PurchaseOrderLines"` and a
   history row "Order created" in `public."PurchaseOrderHistory"`.
3. **Edit** (`PUT /api/purchase-orders/{id}`): changes the order number (logged in history), supplier, expected
   delivery, contact channel and notes. Lines: lines missing from the request are removed, lines with an `id` are
   updated, lines without one are added. Names are re-resolved from the catalog in one batch.
4. **Status / invoice**: the change is applied and a history row is written (old → new, user, UTC time).
5. **Ordered quantities → catalog**: the Catalog module's BackgroundRefresh task
   `ICatalogRepository:RefreshOrderedData` (every 5 min, tier 1) calls `ICatalogPurchaseSource` →
   `PurchaseOrderRepository.GetOrderedQuantitiesAsync`. That sums `Quantity` per `MaterialId` over all lines of
   qualifying orders and stores the result as `Stock.Ordered` in the catalog cache.
6. **Material containers** (Catalog inventory) can point at a purchase order line (`MaterialContainers.PurchaseOrderLineId`,
   FK with `Restrict`), so a container can be traced to the order line it came from.

## Logic & formulas
- **Which lines count as Ordered:** lines of orders where `Status = Draft` **or** `Status = InTransit` **or**
  `InvoiceAcquired = false`. Quantities are summed in the line's unit as entered (no unit conversion) and keyed by
  `MaterialId` (the catalog product code).
- **Order number**: if the user leaves it empty, it is `PO{orderDate:yyyyMMdd}-{now UTC HHmmssfff}`; when that
  number exists, `-2` … `-5` is appended. After 5 attempts the request fails with
  `PurchaseOrderNumberGenerationFailed`. `OrderNumber` has a unique index.
- **Totals**: line total = quantity × unit price, order total = sum of line totals. The prices are whatever the buyer
  typed, with no currency or VAT field. The in-transit tile shows the total in thousands ("12.5k").
- Dates: `OrderDate` and `ExpectedDeliveryDate` are stored as UTC; the client sends the dates as strings on create.
- `CreatedBy`/`UpdatedBy`/`ChangedBy` = the signed-in user's name, or "System".
- Enforced by the server: quantity > 0 and unit price ≥ 0 (domain checks, and `[Range]` on the request); text
  lengths (`[StringLength]`); valid status transitions; no edits on Completed orders.
- List defaults: the API sorts by order date, newest first; the page starts with "active only" (`activeOrdersOnly`,
  hides Completed).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `FlexiBeeSettings:*` | secrets (Key Vault) | Flexi connection for the supplier list |
| Supplier cache (code constants) | 2 h absolute, 1 h sliding, key `all_suppliers` | Supplier list freshness |
| `BackgroundRefresh:ICatalogRepository:RefreshOrderedData` | every 00:05:00, tier 1 | How quickly order changes reach the catalog's Ordered |

## Runtime facts
None.

## Known quirks
- **Ordered also counts finished orders whose invoice flag is off.** Because of the `|| !InvoiceAcquired` condition, a Completed (or
  Received) order keeps counting as Ordered until someone ticks "invoice acquired". By then the goods are usually
  already in stock in Flexi, so `EffectiveStock` counts them twice. Draft orders, which may never be sent,
  count too.
- **The FluentValidation rules never run.** `CreatePurchaseOrderRequestValidator` and
  `UpdatePurchaseOrderRequestValidator` are registered in `PurchaseModule`, but no `ValidationBehavior` is wired
  for these requests. So these rules are **not enforced**: supplier required, order date at most 30 days ahead, expected delivery on or
  after the order date, at most 100 lines, and "at least one line" on edit. Only the DataAnnotations and the domain checks apply.
- **The history is partial.** It records creation, order-number changes, status changes and invoice-flag changes. Line,
  supplier, delivery-date and notes edits only update `UpdatedBy/UpdatedAt`, with no history row.
- **The supplier list is a cache.** A supplier newly added in Flexi can take up to 2 h to appear. If the Flexi read
  returns nothing, it is not cached and the next search retries; meanwhile create/edit fails with `SupplierNotFound`.
- **Removing a line that a material container points to** is blocked by the `Restrict` foreign key. The edit handler
  catches only `InvalidOperationException`, so this surfaces as a server error rather than a friendly message.
  (Read from code, not observed.)
- **The `Received` state is unused by the UI.** The frontend has no label for it and never sets it.
- **The order total has no currency.** Line prices are free numbers, so a total mixing EUR and CZK suppliers is
  meaningless (this matters for the in-transit tile).

## Code entry points
- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs` — states, transitions, editability, history rows
- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrderNumberGenerator.cs` — order number format
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs` — create, supplier and material lookup
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs` — line diffing
- `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs` — `GetOrderedQuantitiesAsync` (Ordered rule), list filters
- `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderConfiguration.cs` — tables, unique order number
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs` — supplier source and cache
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs` — `RefreshOrderedData` into the catalog
- `frontend/src/components/pages/PurchaseOrderDetail.tsx` — status buttons, invoice toggle
