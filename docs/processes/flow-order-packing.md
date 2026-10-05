---
process: flow-order-packing
kind: workflow
module: packaging
summary: Packing-desk flow for e-shop orders — scan an order, create the carrier shipment in Shoptet, print its labels, record the packages in Heblo and move the Shoptet order to "Zabaleno"; plus re-creating a shipment and deleting a package.
owns:
  - backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
  - backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs
  - backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackedOrderStatusUpdater.cs
  - backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackingOrderShippingSource.cs
  - backend/src/Anela.Heblo.Application/Features/Packaging/Services/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/Validators/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/DeletePackage/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackageLabelPdf/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/**
  - backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs
  - backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs
  - backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs
  - backend/src/Anela.Heblo.Persistence/Features/Packaging/PackageConfiguration.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs
verified_at: "5e993f9e2"
related:
  - sync-tracking-numbers
  - calc-packing-statistics
---

# Order packing at the packing desk (Balení)

## Purpose
This is how an e-shop order physically leaves the warehouse. At the packing desk (Balení, page
`/baleni/baleni`, a landscape touch PC with a barcode scanner and a label printer) the packer
picks their name, scans the order barcode, sees the items (with images, set names, cooling flag
and customer / e-shop notes), chooses the number of boxes, and the desk prints one carrier label
per box. When every label has printed, Heblo moves the Shoptet order to **"Zabaleno"** (packed).

Heblo itself stores only an audit record per box (`public."Packages"`), which feeds the shipment
list (Zásilky, `/baleni/zasilky`) and the packing statistics (`calc-packing-statistics`).
The shipment, the carrier labels and the order state all live in **Shoptet**.

The Shoptet shipment / label API calls are described in detail by the shipment-labels module docs;
order state changes in general by the Shoptet-orders module docs. This doc covers what the
packing desk does with them.

## Trigger
User-driven, permission `warehouse.packaging.write` (`Feature.Warehouse_Packaging`, Write) for
every step that writes; reads need `warehouse.packaging.read`.

| Step | Who / where | Endpoint |
|---|---|---|
| Pick packer | Packer, chip on the Balení screen (list of active users with *Can pack*) | `GET /api/packaging/packing-users` |
| Scan order | Barcode scanner on `/baleni/baleni` | `POST /api/packaging/orders/{orderCode}/scan?numberOfPackages=N` (body `{packingUserId}`) |
| Print label | Automatic after scan, per box | `GET /api/packaging/orders/{orderCode}/packages/{packageNumber}/label.pdf` |
| Mark packed | Automatic once every label printed | `POST /api/packaging/orders/{orderCode}/packing/complete` |
| Re-create shipment | "Vytvořit novou zásilku" in the "shipment already exists" dialog | `POST /api/packaging/orders/{orderCode}/shipment/reset?numberOfPackages=N` |
| Delete package | Zásilky list, delete button | `DELETE /api/packaging/packages/{id}` |

Order state path in Shoptet: **Balí se** (`ShoptetOrders:PackingStateId`, default 26) →
**Zabaleno** (`ShoptetOrders:PackedStateId`, default 52). Only orders in *Balí se* are packable.

## Data flow
**Scan** (`ScanPackingOrderHandler`):
1. Validate `numberOfPackages` is 1–10, else `InvalidPackageCount`; `orderCode` must be non-empty.
2. Load the order from Shoptet (`ShoptetApiPackingOrderClient.GetPackingOrderAsync`):
   `GET /api/orders/{code}?include=stockLocation,notes` (404 → `ShoptetOrderNotFound`), enriched
   with product image, weight and cooling from the Heblo catalog (`IPackingProductSource`) and
   carrier cooling from the carrier-cooling settings. Delivery address falls back to billing address.
   Eligible = Shoptet `status.id == PackingStateId`.
3. Look for an existing active shipment: `GET /api/shipments?orderCode={code}` (shipments in status
   `canceled`, `cancel_requested`, `deleted`, `request_failed` are ignored).
4. Branch:
   - **Not eligible** → return order info (+ existing shipment for reprint review). No writes.
   - **Eligible, shipment already exists** → insert any missing `Packages` rows for it
     (`AddMissingAsync`, best effort) and **immediately set the order to Zabaleno**
     (`PATCH /api/orders/{code}/status`, failure only logged). The desk then asks
     "use existing" (reprint) or "create new" (Reset).
   - **Eligible, no shipment** → `ShipmentCreationService.CreateAndPersistAsync` (below), return
     the labels with `pendingCompletion = true`. The order is **not** marked packed yet.
5. The desk prints each label through the label proxy, polling while the carrier has not generated
   it yet (404 → backoff 1, 2, 3, 5, 10 s, up to 30 s; poll requests send header `X-Label-Poll`).
6. When every label is acknowledged printed and `pendingCompletion` is true, the desk calls
   **complete** → `IPackedOrderStatusUpdater.MarkAsPackedAsync` → Shoptet
   `PATCH /api/orders/{code}/status` with `PackedStateId`. Failure → `PackingCompletionFailed`
   (the desk resets its guard so it can retry).
7. The done screen shows tracking numbers from `sync-tracking-numbers`.

**Shipment creation** (`ShipmentCreationService`, shared by Scan and Reset):
1. Weight: Σ(item weight g × quantity); item weight from catalog, else
   `ShoptetApi:DefaultItemWeightGrams` (repo default 0). Total 0 → `ShipmentLabels:FallbackPackageWeightGrams`
   (1000 g). Per box = max(total ÷ N (integer division), `MinPackageWeightGrams` 100 g).
2. Carrier: first entry of `GET /api/shipments/order/{code}/shipping-options`; none →
   `ShipmentCarrierNotResolved`.
3. `POST /api/shipments` with carrier, N boxes, fixed box size 30 × 20 × 15 cm (config) and the
   per-box weight. Shoptet validation error → `ShipmentValidationFailed` (Shoptet message passed to
   the desk); any other error → `ShipmentCreationFailed`.
4. Read labels back (`GET /api/shipments?orderCode=`), keep only this shipment's GUID, pad to
   exactly N entries (labels are generated asynchronously; missing ones have no tracking number).
5. Packer: if `packingUserId` given, the user must exist, be active and have *Can pack*, else
   `PackingUserNotEligible`; `PackedBy` = display name. Without it, `PackedBy` = the logged-in
   user's e-mail and `PackedByUserId` = null.
6. `ReplacePackagesForOrderAsync`: delete every `Packages` row of the order and insert N new rows
   in one save. A failure here is only logged.

**Reset** (`ResetOrderShipmentHandler`): read active shipments for the order (none →
`NoShipmentToReset`) → `POST /api/shipments/{guid}/cancel-request` for each distinct GUID (any
failure → `ShipmentCancelFailed`, stop) → reload the order → shipment creation as above with
**no packer** → return labels; `pendingCompletion = N ≥ 2`.

**Delete package** (`DeletePackageHandler`): find the row (`PackageNotFound`) →
`POST /api/shipments/{shipmentGuid}/cancel-request` (failure only logged) → delete that one row.

**Label proxy** (`GetPackageLabelPdfHandler`): read the order's active labels from Shoptet, take
the `packageNumber`-th (1-based) → download its `labelUrl` (HTTP client `ShipmentLabelDownloader`,
30 s timeout) → stream to the browser with `Cache-Control: no-store`, file `{order}-{n}.pdf`.
Not ready → 404 `PackageLabelNotFound`; carrier download failure → 503 `PackageLabelDownloadFailed`.
When feature flag `is-gls-label-rotation-enabled` is on, the handler also looks up the order's
shipping-method GUID (`IPackingOrderShippingSource`, one extra `GET /api/orders/{code}`) and, if
`IShippingMethodCatalog` resolves it to **GLS**, adds 180° to every page's `/Rotate` (PDFsharp,
`LabelPdfRotator`) so the label comes out of the Zebra upside down. Fail-open: a failed carrier
lookup or an unparsable PDF is logged at Warning and the original label is served unrotated.

## Logic & formulas
- One `Packages` row = one box (carrier package) of one order. `PackageNumber` is the 1-based
  box index on the create path (carrier package names are not unique, e.g. "Vlastní balení").
  Unique index (`OrderCode`, `PackageNumber`).
- `PackedAt` / `CreatedAt` = UTC time the row was written, not when the box was physically packed
  or when Shoptet moved the order. Re-scan with a new shipment or Reset overwrites them.
- Max 10 boxes per order (hard-coded in the handlers and the service).
- Box dimensions are the same fixed defaults for every shipment; only the weight varies.
- Customer name stored is Shoptet's order customer name at scan time.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ShoptetOrders:PackingStateId` | 26 ("Balí se") | Shoptet state that makes an order packable |
| `ShoptetOrders:PackedStateId` | 52 ("Zabaleno") | State set when packing completes |
| `ShipmentLabels:DefaultPackageWidthCm` / `HeightCm` / `DepthCm` | 30 / 20 / 15 | Box size sent with every shipment |
| `ShipmentLabels:MinPackageWeightGrams` | 100 | Floor for the per-box weight |
| `ShipmentLabels:FallbackPackageWeightGrams` | 1000 | Order weight used when no item weight is known |
| `ShoptetApi:DefaultItemWeightGrams` | 0 | Weight for an item missing a catalog weight |

Feature flag `is-gls-label-rotation-enabled` (default off, `appsettings.json` + DB override via
`/admin/feature-flags`) — rotate GLS label PDFs 180° in the label proxy.

None of the keys above is overridden in `appsettings*.json` (class defaults apply). `ShipmentLabels` is
not present in any appsettings file.

## Runtime facts
- `public."Packages"` first `PackedAt` is 2026-06-09; 6 174 rows at check time — prod Heblo_V3
  query in the Metabase-readiness investigation — 2026-09-24.

## Known quirks
- **Packer check runs after the shipment is created.** `ShipmentCreationService` validates
  `packingUserId` only after `POST /api/shipments` succeeded, so an ineligible packer leaves a live
  Shoptet shipment with no `Packages` rows and the desk shows an error. A re-scan then finds the
  existing shipment and takes the "already exists" path.
- **Re-scanning an eligible order with an existing shipment marks it Zabaleno at once**, before the
  packer chooses reprint or new and before any label is printed.
- **Reset never sets the order state for one box** (`pendingCompletion` is only true for N ≥ 2).
  In practice Reset is reached from the "already exists" dialog, where the scan already set
  Zabaleno, so this is consistent — but Reset itself does not check eligibility.
- **Reset loses packer attribution**: it sends no packer, so the replaced rows get
  `PackedByUserId = null` and `PackedBy` = the kiosk login's e-mail; they drop out of the
  per-packer statistics.
- **Two package-number schemes.** Create path stores `1..N`; the "existing shipment" backfill
  stores the carrier package name. If the create path already wrote rows, a later re-scan of the
  same still-eligible order (e.g. complete failed) can add a second set of rows for the same boxes,
  inflating package counts. Duplicate carrier names inside one backfill are collapsed to one row.
- **Delete cancels the whole shipment but deletes one row.** `DELETE /packages/{id}` cancels the
  Shoptet shipment of that box (all its boxes) and removes only that row; sibling rows of the same
  shipment stay. The Shoptet order state is not changed.
- **Package rows are best effort.** A DB failure while writing rows is logged as a warning only;
  the shipment exists in Shoptet but Heblo statistics miss it.
- Label proxy indexes across **all active shipments** of the order, not just the latest one, so the
  N-th label can be from another shipment if an older one was not cancelled.
- `docs/features/packaging.md` describes an older API (`/label/pdf?shipmentGuid=…`, warning
  texts in the scan response); the code above is authoritative.
- Shoptet has no sandbox: every scan on staging against the real store creates real shipments.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs` — all endpoints and permissions
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — scan branches
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs` — weight, carrier, shipment, package rows
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs` — re-create shipment
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackageLabelPdf/GetPackageLabelPdfHandler.cs` — label proxy
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/LabelPdfRotator.cs` — 180° label rotation (GLS, flag-gated)
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — order read + eligibility
- `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` — `Packages` writes
- `frontend/src/components/baleni/PackingShipmentCreator.tsx`, `PackingLabelPrinter.tsx`, `printLabelPdf.ts` — desk-side print and completion
