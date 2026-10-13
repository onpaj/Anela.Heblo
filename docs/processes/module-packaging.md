---
process: module-packaging
kind: module
module: packaging
summary: The packing desk (Balení) — scan an e-shop order, create its carrier shipment and print labels in Shoptet, mark it packed, and keep a per-box record for the shipment list and packing statistics.
owns: []
verified_at: "5e993f9e2"
related: []
---

# Packaging (Balení)

## Purpose
Gets paid e-shop orders out of the warehouse. A packer at the packing desk scans an order that
Shoptet has in state *Balí se*, checks the items on screen, and the desk creates the carrier
shipment in Shoptet, prints one label per box and moves the order to *Zabaleno*. Heblo keeps one
record per box so staff can look up shipments later and so the warehouse can see throughput and
who packed what. Shoptet remains the system of record for orders, shipments and labels.

## Users & screens
Packers and the warehouse lead, on a landscape touch PC at the packing desk (PWA scope `/baleni`,
no sidebar). Menu path `/baleni` needs `warehouse.packaging.read`; scanning, re-creating shipments,
completing and deleting need `warehouse.packaging.write`.

| Route | Screen |
|---|---|
| `/baleni` | Home: tiles to the three pages + today's counters |
| `/baleni/baleni` | Packing desk: pick packer, scan order, number of boxes, print labels, done screen |
| `/baleni/zasilky` | Shipments list (Zásilky): filter by order, customer, box number, carrier, date; delete a box |
| `/baleni/statistiky` | Packing statistics (default last 30 days) |

Main dashboard tile **"Stav balení"** (`packingstats`). No MCP tools.

## Processes
- `flow-order-packing` — scan → shipment → labels → *Zabaleno*; re-create shipment; delete a box. User-driven.
- `sync-tracking-numbers` — fills missing tracking numbers; Hangfire `fill-tracking-numbers`, every 10 min, plus on the done screen.
- `calc-packing-statistics` — today's counters, dashboard tile and the statistics page. On demand.

Plain reads without a process doc: shipments list (`GET /api/packaging/packages`, paginated, sorted
by `PackedAt` desc by default; carrier filter maps the `Carriers` enum to shipping codes via the
logistics shipping-method catalog) and packer list (`GET /api/packaging/packing-users`, served by
the Authorization module).

## Data owned
- `public."Packages"` — one row per box of a packed order: order code, customer name, box number,
  tracking number, carrier code/name, Shoptet shipment GUID, `PackedAt`, packer (`PackedBy` name or
  e-mail, `PackedByUserId`), `CreatedAt`. Unique (`OrderCode`, `PackageNumber`); indexes on
  `OrderCode`, `PackedAt`, `PackedByUserId`.

No caches or blob storage.

## External systems
**Shoptet REST API** (no sandbox, every call hits the live store):

| Direction | Endpoint | Used for |
|---|---|---|
| read | `GET /api/orders/{code}?include=stockLocation,notes` | Order for the desk + eligibility |
| read | `GET /api/orders?statusId=&page=1&itemsPerPage=50` | Counts of orders being packed / processed |
| read | `GET /api/shipments?orderCode=` | Existing shipments, labels, tracking numbers |
| read | `GET /api/shipments/order/{code}/shipping-options` | Carrier for a new shipment |
| write | `POST /api/shipments` | Create the carrier shipment |
| write | `POST /api/shipments/{guid}/cancel-request` | Cancel a shipment (Reset, delete box) |
| write | `PATCH /api/orders/{code}/status` | Set *Zabaleno* (`PackedStateId`, default 52) |
| read | carrier `labelUrl` (CDN) | Label PDF, proxied through Heblo for silent printing |

## Dependencies
- **ShoptetOrders** — `IPackingOrderClient` / `PackingOrder` contract, `ShoptetOrdersSettings`
  (state ids), and the implementation of Packaging's `IPackedOrderStatusUpdater`.
- **ShipmentLabels** — `IShipmentClient` (Shoptet shipment calls) and `ShipmentLabelsSettings`
  (box size and weights).
- **Catalog** — product image, weight and cooling for the scanned items.
- **CarrierCooling** — carrier × delivery-handling cooling matrix shown on the desk.
- **Authorization** — users with *Can pack* (packer picker and packer validation).
- **Logistics** — shipping-method catalog for the carrier filter / display name.
- Read by: main dashboard (tile); Metabase can read `Packages` through the legacy `metabase_ro`
  grant (no curated view).

## Known quirks
- An ineligible packer is detected only after the Shoptet shipment is created, leaving an orphan
  shipment (see `flow-order-packing`).
- Re-scanning an eligible order that already has a shipment sets *Zabaleno* immediately.
- Reset drops packer attribution; deleting one box cancels the whole Shoptet shipment.
- Multi-box orders get box 1's tracking number on every stored row (`sync-tracking-numbers`).
- Statistics count Heblo box rows, not Shoptet state changes; history starts 2026-06-09.
- `docs/features/packaging.md` is partly outdated (old label route and response fields).

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs` — every endpoint
- `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs` — DI, validators, tile
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs` — shipment creation core
- `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` — `Packages` table and statistics
- `frontend/src/components/baleni/` — desk UI (touch-first, landscape)
