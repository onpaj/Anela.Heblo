---
process: module-shipment-labels
kind: module
module: shipment-labels
summary: Heblo's gateway to Shoptet shipments — creates carrier shipments for packed orders, reads labels and tracking numbers, cancels shipments and reports deliveries; used by the packing desk and the order-completion job.
owns: []
verified_at: "5e993f9e2"
related:
  - flow-shipment-label
---

# Shipment labels

## Purpose
Every e-shop parcel needs a carrier label. Anela does not integrate carriers (PPL, Zásilkovna, …)
directly: Shoptet does, through Balikobot. This module is the thin layer that lets the rest of
Heblo ask Shoptet to **create a shipment (zásilka)** for an order, **read back its label**
(PDF URL, also a ZPL string) and **tracking number**, **cancel** it, and find out whether it has
been **delivered**. It has no screen, no table and no scheduled job of its own — it is a service
used by the packing desk (packaging module) and by the order-completion job (shoptet-orders module).

## Users & screens
No own page or MCP tool. Its effects are visible on:
- **Packing desk** (Balení, `/baleni/baleni`, packaging module) — scanning an order creates the
  shipment and auto-prints label 1, then "Vytisknout štítek 2/N"…; "Zkusit štítek znovu" when the
  carrier has not generated the label yet.
- **Shipments list** (Zásilky, `/baleni/zasilky`, packaging) — tracking numbers filled from Shoptet;
  deleting a row cancels its shipment in Shoptet.

Its own API (`POST api/shipment-labels`, `POST api/shipment-labels/create`, permission
`Warehouse_Expedition` write) is no longer called by anyone — see Known quirks.

## Processes
- `flow-shipment-label` — every Shoptet shipments call (create, labels, tracking, cancel, delivered
  check), how package weight/size are computed, error mapping. On demand, triggered by the packing
  desk and by the hourly `complete-delivered-orders` job.

No other processes. Using processes in other modules (documented there, by name):
packaging's order-packing flow and its `fill-tracking-numbers` job (`*/10 * * * *`);
shoptet-orders' `complete-delivered-orders` job (`0 * * * *`).

## Data owned
None. Nothing is persisted by this module; shipment rows live in Shoptet, and the packaging module
stores packed parcels in `public."Packages"`.

## External systems
**Shoptet REST API** (`https://api.myshoptet.com`, header `Shoptet-Private-API-Token`), via
`ShoptetShipmentClient`:

| Call | Direction | Purpose |
|---|---|---|
| `GET /api/shipments?orderCode={code}` | read | shipments of an order: status, packages, label URL/ZPL, tracking |
| `GET /api/shipments/order/{code}/shipping-options` | read | carrier `shippingId` for the order (first option wins) |
| `POST /api/shipments` | **write** | create a shipment with N packages (cm, kg) |
| `POST /api/shipments/{guid}/cancel-request` | **write** | cancel a shipment |

**Carrier label CDN** — the `ShipmentLabelDownloader` HttpClient (30 s timeout) registered here is
used by packaging to stream the label PDF through Heblo's own origin (no CORS) for silent printing.

## Dependencies
- Reads from: shoptet-orders (`IPackingOrderClient` — order items and weights, only in the unused
  `/create` endpoint); Shoptet API settings (`Shoptet:*`).
- Read by: **packaging** (`IShipmentClient`, `ShipmentLabelsSettings` — scan, reset, delete package,
  label PDF proxy, tracking numbers, `fill-tracking-numbers`), **shoptet-orders**
  (`IShipmentDeliveryChecker`, implemented here by `ShipmentLabelsShipmentDeliveryCheckerAdapter`,
  used by `complete-delivered-orders`).

## Known quirks
- Own endpoints, their handlers and error codes 2902/2903/2905/2909 are dead code since #1502 moved
  orchestration into the packaging scan; 2908 `ShipmentLabelNotReady` is never returned at all.
- ZPL labels are fetched but never printed; printing is PDF-only through the browser. The Zebra/ZPL
  plan in `docs/integrations/shoptet-api.md` §11.7 is stale.
- Package size is always the default 30 × 20 × 15 cm box; the carrier gets no real dimensions.
- Labels appear minutes after shipment creation (2026-06-09), so the first print often waits/retries.
- Details and more in `flow-shipment-label`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs` — DI registration and cross-module adapter
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs` — the contract other modules use
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` — Shoptet implementation
- `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs` — unused own endpoints
