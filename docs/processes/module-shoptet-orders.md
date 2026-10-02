---
process: module-shoptet-orders
kind: module
module: shoptet-orders
summary: Heblo's gateway to live Shoptet orders — reads an order for the packing desk, changes order states (packed, blocked, completed after delivery) and appends internal remarks.
owns: []
verified_at: "5e993f9e2"
related:
  - job-complete-delivered-orders
  - flow-block-order
---

# Shoptet orders

## Purpose
Anela's e-shop orders live in Shoptet; Heblo does not keep its own copy of them for day-to-day
work. This module is the single place that talks to the Shoptet order API on behalf of the
warehouse: it reads one order (items, customer, delivery address, carrier, cooling, notes) for
the packing desk (Balení), moves orders between Shoptet states (Balí se → Zabaleno, hold an
order, close a delivered order as Vyřízena) and writes the internal staff remark
(Interní poznámka). Other modules (Packaging, Expedition list, Shipment labels) reach Shoptet
orders through contracts this module provides.

Not this module: the nightly `shoptet_raw` order mirror for reporting
(`ShoptetOrdersSyncJob`, `Anela.Heblo.Persistence.ShoptetOrders`) belongs to the analytics
module (its doc is `sync-shoptet-orders`), despite the shared name.

## Users & screens
- No page of its own. Warehouse staff see its data on the packing desk `/baleni`, which uses the
  Packaging API (`/api/packaging/...`), not this module's endpoints.
- API `ShoptetOrdersController` (`api/shoptet-orders`, permission `Warehouse_Expedition`):
  - `GET {code}/packing` — packing view of one order (read-only). No frontend caller.
  - `PATCH {code}/block` — see `flow-block-order`. No frontend caller.
- No MCP tools.

## Processes
- `job-complete-delivered-orders` — hourly Hangfire job: orders in 70/82 whose shipment is
  delivered → Vyřízena (-3) + remark; dry run unless flag `is-delivered-order-completion-enabled`.
- `flow-block-order` — `PATCH /api/shoptet-orders/{code}/block`: allowed state → blocked state 35 + remark.
- Mark as packed (Zabaleno, 52) — executed by this module's `IEshopOrderClient.MarkAsPackedAsync`
  but triggered by the packing desk scan / complete-packing; documented with the packaging
  module's packing workflow.
- Read-only order lookups (no doc needed): packing view (`IPackingOrderClient`), packing
  dashboard counts of orders in Balí se (26) and Vyřizuje se (-2) (`IPackingOrderCountSource`),
  order status for single-order expedition print (`IOrderStatusReader`).

### Order states this module knows (anela.cz)
| Id | Shoptet name | Used by |
|---|---|---|
| -2 | Vyřizuje se | dashboard count (`ProcessingStateId`); allowed block source |
| 26 | Balí se | packing eligibility (`PackingStateId`); dashboard count |
| 52 | Zabaleno | target of mark-as-packed (`PackedStateId`) |
| 35 | (name not in repo) | block target (`BlockedStatusId`), also allowed block source |
| -1, 3, 32 | (names not in repo) | allowed block sources |
| 70 | Předáno přepravci | auto-complete source |
| 82 | SMS chlazené-Předáno dopravci | auto-complete source |
| 73 | Oprava-robot | auto-complete test source |
| -3 | Vyřízena | auto-complete target (`CompletedStatusId`) |

## Data owned
None. The module writes no Heblo table, cache or blob; all state lives in Shoptet.
(`shoptet_raw.*` tables belong to analytics, see Purpose.)

## External systems
Shoptet REST API (`ShoptetApi:BaseUrl`, header `Shoptet-Private-API-Token`), via
`ShoptetOrderClient` and `ShoptetApiPackingOrderClient`:
| Call | Direction | Used for |
|---|---|---|
| `GET /api/orders/{code}` | read | status id (block, expedition print) |
| `GET /api/orders/{code}?include=stockLocation,notes` | read | packing view |
| `GET /api/orders/{code}?include=notes` | read | current `eshopRemark` before appending |
| `GET /api/orders?statusId={id}&page={n}&itemsPerPage=50` | read | lists by state; dashboard counts use `paginator.totalCount` of page 1 |
| `PATCH /api/orders/{code}/status` | write | packed, blocked, completed |
| `PATCH /api/orders/{code}/notes` | write | `eshopRemark` (overwrite; append is read-modify-write) |
| `GET /api/shipments?orderCode={code}` | read | delivered check (client owned by ShipmentLabels) |

`ShoptetOrderClient` also implements `IShoptetOrderTestClient` (`POST`/`DELETE /api/orders`,
list by external-code prefix) — used only by integration tests that create real orders in the
live store; no production code path calls it. `SetAdditionalFieldAsync` (`PATCH .../notes`
additionalFields) is used by the expedition list module.

## Dependencies
- Reads from: Catalog (`IPackingProductSource` — product cooling, weight, image), CarrierCooling
  (`IPackingCarrierCoolingSource` — carrier × delivery-handling cooling matrix), ShipmentLabels
  (`IShipmentDeliveryChecker`), FeatureFlags, BackgroundJobs.
- Read by: Packaging (`IPackingOrderClient`, `IPackedOrderStatusUpdater`, `IPackingOrderCountSource`),
  ShipmentLabels (`IPackingOrderClient` when creating a shipment), ExpeditionList (`IOrderStatusReader`).

## Known quirks
- Both API endpoints of this module have no caller in the frontend or MCP — the packing desk
  goes through Packaging, and block is unused (see `flow-block-order`).
- Packing eligibility is strictly `status.id == PackingStateId (26)`; any other state shows the
  "not in Balí se" warning on the packing desk.
- Items without a catalogue weight fall back to `ShoptetApi:DefaultItemWeightGrams` (logged as
  Warning) — affects shipment weight, not the packing view shown to staff.
- Delivery address on the packing view falls back to the billing address when the order has none.
- The order/shipment/customer Shoptet HTTP clients have no retry or resilience handler; only the
  stock CSV client does.
- Remark appends are read-modify-write without concurrency control; a parallel staff edit in
  Shoptet admin can be overwritten.
- `ShoptetOrdersSettings` state lists are config arrays with the binder merge/append trap
  (memory note `gotcha_config_binder_appends_arrays`); details in the process docs.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` — DI, cross-module adapters
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs` — all state ids
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — order operations contract
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs` — packing view endpoint
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — Shoptet order HTTP calls
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — packing view assembly, dashboard counts
- `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs` — endpoints
- `docs/integrations/shoptet-api.md` — verified Shoptet order API behaviour
