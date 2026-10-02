---
process: flow-shipment-label
kind: workflow
module: shipment-labels
summary: Creates carrier shipments in Shoptet for e-shop orders packed at the packing desk, reads back their labels (PDF URL / ZPL) and tracking numbers, cancels them on reset, and tells the order-completion job when a parcel was delivered.
owns:
  - backend/src/Anela.Heblo.Application/Features/ShipmentLabels/**
  - backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/**
verified_at: "5e993f9e2"
related: []
---

# Shipment creation & labels (Shoptet shipments)

## Purpose
When an order is packed at the packing desk (Balení), the parcel needs a carrier label
(PPL, Zásilkovna, … as chosen by the customer in the e-shop). Heblo does not talk to carriers
directly: it asks **Shoptet** to create a *shipment* (zásilka) for the order; Shoptet passes it to
Balikobot/the carrier, which generates the label PDF and the tracking number. Heblo then reads the
label back so the desk can print it, and later reads the tracking number and the delivery status.

This module is the single gateway to the Shoptet shipments API (`IShipmentClient`). The packing
screens, order-state changes and the `Packages` table belong to the **packaging** module (see its
`flow-order-packing` doc); automatic "vyřízena" on delivery belongs to the **shoptet-orders** module
(job `complete-delivered-orders`). This doc describes what each shipment call does, with which data,
and what can go wrong.

## Trigger
On demand only — no Hangfire job, no BackgroundRefresh task in this module. Calls come from:

| Caller (module) | When | Calls |
|---|---|---|
| Packing desk scan — `ScanPackingOrderHandler` + `ShipmentCreationService` (packaging) | Packer scans an order on `/baleni/baleni` | get labels (does a shipment already exist?), shipping options, **create shipment**, get labels |
| Reset shipment — `ResetOrderShipmentHandler` (packaging) | Packer re-does the parcel (different package count) | get labels, **cancel** every active shipment, then create a new one |
| Delete package — `DeletePackageHandler` (packaging) | Row deleted on the Zásilky page | **cancel** the package's shipment (failure only logged) |
| Label PDF proxy — `GetPackageLabelPdfHandler` (packaging), `GET api/packaging/orders/{code}/packages/{n}/label.pdf` | Desk prints / reprints a label | get labels, then downloads the PDF from the carrier CDN |
| Tracking numbers — `FillTrackingNumbersJob` (`fill-tracking-numbers`, `*/10 * * * *`), `GetOrderTrackingNumber(s)Handler` (packaging) | Every 10 min, and on the "done" screen | latest active tracking number / labels |
| Delivered check — `CompleteDeliveredOrdersJob` (`complete-delivered-orders`, `0 * * * *`, shoptet-orders) via `IShipmentDeliveryChecker` | Hourly | has any shipment status `delivered`? |
| Own API `POST api/shipment-labels`, `POST api/shipment-labels/create` | **Nobody** — no frontend or MCP caller (see Known quirks) | get labels / create shipment |

Shipment states in Shoptet (from `docs/integrations/shoptet-api.md` §11): `requested` (submitted,
no label yet) → `created` (label + tracking number ready) → … → `delivered`; dead states are
`cancel_requested`, `canceled`, `deleted`, `request_failed`.

## Data flow
All calls go to `https://api.myshoptet.com` (`Shoptet:BaseUrl`) with header
`Shoptet-Private-API-Token` (`Shoptet:ApiToken`), via typed HttpClient `ShoptetShipmentClient`.

1. **Read shipments of an order** — `GET /api/shipments?orderCode={code}`. Response
   `data.items[]` = shipments (oldest first, `guid` is time-ordered UUIDv7), each with `status` and
   `packages[]` (`name`, `labelUrl`, `labelZpl`, `trackingNumber`, `trackingUrl`). Used by:
   - `GetLabelsByOrderCodeAsync` → one `ShipmentLabel` per package of every **active** shipment
     (status null or not dead), flattened in response order.
   - `GetLatestActiveTrackingNumberAsync` → first non-empty `trackingNumber` of the **last** active
     shipment (deliberately not matched by package name — names change, see quirks).
   - `HasDeliveredShipmentAsync` → true if **any** shipment (dead ones included) has status
     `delivered` (case-insensitive).
2. **Resolve carrier** — `GET /api/shipments/order/{code}/shipping-options` → `data.shippingOptions[]`.
   Heblo always takes the **first** option: its `shippingId` (a per-order integer, passed on as
   `CarrierCode`) and `methodName` (fallback `carrierCode`) as the carrier name. No option →
   error `ShipmentCarrierNotResolved` (2906).
3. **Create shipment** — `POST /api/shipments` with
   `{"data":{"orderCode", "shippingId", "packages":[{width, height, depth, weight}, …]}}`:
   N identical packages (N = package count from the desk, 1–10; min 1), dimensions in **cm** from
   `ShipmentLabels:DefaultPackage*Cm`, weight as a string in **kg with 3 decimals**. Response
   `data.guid` = shipment GUID. No COD, note or bank account is sent — Shoptet takes those from the
   order itself.
4. **Read labels back** — right after creation the shipment is usually still `requested` with
   `labelUrl` / `trackingNumber` null; the desk then polls the label PDF proxy (packaging) until
   the carrier has generated it.
5. **Cancel** — `POST /api/shipments/{guid}/cancel-request` (no body). HTTP 404 is treated as
   "already gone" and ignored. The shipment moves to `cancel_requested` / `canceled` and is from then
   on ignored by step 1.

Target: nothing is stored by this module. The packaging module stores the result in
`public."Packages"` (one row per package, `ShipmentGuid`, `TrackingNumber`, carrier code/name).

## Logic & formulas
**Package weight** (same rule in `ShipmentCreationService` and in this module's unused
`CreateOrderShipmentHandler`):
- order weight = Σ over order items of `WeightGrams × Quantity` (item weights come from the Shoptet
  order via `IPackingOrderClient`, owned by shoptet-orders);
- if it is 0 (no item has a known weight) → `FallbackPackageWeightGrams` (1000 g), logged as a warning,
  because carriers reject a 0 kg parcel;
- desk flow: per-package weight = max(order weight ÷ N (integer division), `MinPackageWeightGrams`
  = 100 g); unused own endpoint: max(order weight, 100 g) for a single package;
- sent as `grams / 1000` formatted `"F3"` in invariant culture (e.g. `"1.250"`).

**Package size** — always the configured default box 30 × 20 × 15 cm; the real box is not measured.

**Errors from create** — a non-2xx body containing Shoptet error code `shipment-validation-failed`
becomes `ShoptetShipmentValidationException` (code, message, `instance` such as `data.orderCode`);
the desk maps it to `ShipmentValidationFailed` (2910) and shows Shoptet's message (typically an
incomplete recipient address/phone/e-mail, or COD above the carrier limit). Every other failure is
an `HttpRequestException` → `ShipmentCreationFailed` (2907). A 2xx response with `errors[]` is also
treated as a failure.

**Own endpoints** (`ShipmentLabelsController`, `[FeatureAuthorize(Warehouse_Expedition, Write)]`):
- `POST api/shipment-labels` `{orderCode}` → labels of active shipments; `ShipmentLabelsNoShipmentFound`
  (2902) if none, `ShipmentLabelsNotGenerated` (2903) if no package has a PDF URL or ZPL yet.
- `POST api/shipment-labels/create` `{orderCode, forceCreate}` → if active labels exist and
  `forceCreate` is false returns `ShipmentAlreadyExists` (2905) with the existing labels; otherwise
  loads the order (`ShipmentOrderWeightUnavailable` 2909 if not found / no items), creates one
  package, re-reads labels once and, if no PDF URL yet, waits 3 s and re-reads once more
  (`labelReady`). With `forceCreate` the old shipment is **not** cancelled.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ShipmentLabels:DefaultPackageWidthCm` | 30 (code default; no section in `appsettings.json`) | Box width sent to the carrier |
| `ShipmentLabels:DefaultPackageHeightCm` | 20 | Box height |
| `ShipmentLabels:DefaultPackageDepthCm` | 15 | Box depth |
| `ShipmentLabels:MinPackageWeightGrams` | 100 | Floor for each package's weight |
| `ShipmentLabels:FallbackPackageWeightGrams` | 1000 | Weight used when no order item has a known weight |
| `Shoptet:BaseUrl` | `https://api.myshoptet.com` | Shoptet REST host |
| `Shoptet:ApiToken` | (secret, Key Vault) | Private API token, also used by every other Shoptet REST client |
| Named HttpClient `ShipmentLabelDownloader` | 30 s timeout | Registered here, used by the packaging label-PDF proxy to stream carrier PDFs |

No feature flag gates shipment creation. There is no sandbox: every call hits the live store.

## Runtime facts
- Label generation after `POST /api/shipments` takes **minutes, not seconds** (order 126000035, PPL
  na výdejní místo) — `docs/integrations/shoptet-api.md` §11.5 — 2026-06-09.
- Shoptet returns shipments oldest-first; one test order had **12 shipments** accumulated by
  repeated pack/reset, all packages named `"Vlastní balení"` — `docs/integrations/shoptet-api.md`
  §11.5.1 — 2026-06-10.
- PPL "výdejní místo" (parcel box) silently creates only **1 package** when 2 are sent (HTTP 201,
  no error); the second label never gets a URL and the desk times out — `docs/integrations/shoptet-api.md` §11.9 — 2026-06-10.

## Known quirks
- **The module's own endpoints are dead.** `POST api/shipment-labels` and `/create` (and their
  handlers, validators, error codes 2902/2903/2905/2908/2909) have no caller in the frontend, MCP
  or backend since shipment orchestration moved into the packaging scan and the `useCreateShipment`/`useShipmentLabels` hooks were deleted (#1502). Only the generated
  API client and tests reference them. They also differ from the live path: no multi-package,
  validation errors become a generic 2907, and `forceCreate` creates a second shipment without
  cancelling the first.
- **ZPL is never printed.** `labelZpl` is read and passed to the frontend, but the desk prints only
  the PDF (via the packaging proxy and the browser print dialog). The "USB Zebra prints ZPL"
  design in `docs/integrations/shoptet-api.md` §11.7 was never built; that section is stale.
- **`ShipmentLabelNotReady` (2908) is never returned** by any handler; only its Czech translation exists.
- **`CreatedShipment.Status` is always null** — the adapter never reads the create response's status,
  so the unused `/create` endpoint always returns `status: null`.
- **Carrier = first shipping option.** If Shoptet ever offers more than one option for an order,
  Heblo silently picks the first one.
- **Package names are not identifiers.** They start as `"1"`, `"2"` and are renamed to the packaging
  type (`"Vlastní balení"`) once the label exists, and repeat across packages. Packaging therefore
  matches labels by position (`PackageNumber` = 1-based index) — and that index runs over the labels
  of **all** active shipments of the order, so a second active shipment (e.g. from the dead
  `forceCreate` path or a failed cancel) would shift label numbers.
- **`HasDeliveredShipmentAsync` counts dead shipments too** — a cancelled shipment that had reached
  `delivered` before would still let `complete-delivered-orders` close the order (unlikely in practice).
- **`docs/integrations/shoptet-api.md` §11.5.1 says each Heblo shipment has exactly one package** —
  stale since multi-package shipments (#2791); the desk now sends N packages in one shipment.
- Weight per package uses integer division (`total / N`), so a few grams are lost per parcel; harmless.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` — every Shoptet call, active-status filter, error mapping
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs` — the port other modules use
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsSettings.cs` — box size and weight defaults
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs` — DI, `ShipmentLabelDownloader` HttpClient, `IShipmentDeliveryChecker` adapter
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/` — the two unused endpoint handlers
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs` — the live create path (packaging module)
- `docs/integrations/shoptet-api.md` §11 — Shoptet shipments API findings
