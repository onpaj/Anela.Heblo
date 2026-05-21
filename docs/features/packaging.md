# Packaging (Balení) Feature Spec

## 1. Purpose

The Balení screen is a kiosk-style terminal where warehouse staff scan order barcodes, verify packing eligibility, and print shipping labels. It is designed for a single dedicated device with a barcode scanner and a label printer. Staff do not navigate — they scan, confirm visually, and move to the next order.

## 2. Actors & Devices

| Actor | Device | Notes |
|---|---|---|
| Packer | Kiosk terminal (touchscreen or keyboard) | Runs a browser in kiosk mode (auto-confirms print dialogs) |
| Barcode scanner | USB or Bluetooth HID | Emits order code as keystrokes, triggers scan event |
| Label printer | Network-connected label printer | Driven by the browser's default printer via `iframe.contentWindow.print()` |

## 3. End-to-End Workflow

```
[idle] ──scan──▶ POST /api/packaging/orders/{code}/scan
         │
         ├── order not found                  → error banner
         ├── order ineligible                 → PackingStateWarning; stop
         ├── eligible & shipment is NEW       → fetch label PDF → iframe.print()
         └── eligible & shipment EXISTED      → dialog
                  ├── "Použít existující"     → fetch label PDF → iframe.print()
                  └── "Vytvořit novou"        → POST /api/packaging/orders/{code}/shipment/reset
                                              → fetch label PDF → iframe.print()
```

**Scan** always triggers a POST `/scan`. There is no separate "create shipment" button.

**Label fetch** is always `GET /api/packaging/orders/{code}/label/pdf?shipmentGuid=...&packageName=...`, proxied by the BE from Balíkobot/carrier.

## 4. States & Guards

### Eligibility

An order is eligible for packing iff `order.statusId == ShoptetOrdersSettings.PackingStateId` (default: 26, "Balí se"). Ineligible orders render a Czech warning (`warningTitle`, `warningBody` from the scan response) and block all Shoptet writes.

### Already-Shipped Signal

"Shipment already exists" = `GET /api/shipments?orderCode={code}` returns at least one item. Derived at scan time by `ScanPackingOrderHandler`. The FE receives `shipment.alreadyExisted: true`.

## 5. API Contract

### POST /api/packaging/orders/{orderCode}/scan

No request body required.

**Success response (order found):**
```json
{
  "success": true,
  "order": {
    "code": "ORD001",
    "customerName": "Jana Nováková",
    "shippingMethodName": "PPL",
    "cooling": "None",
    "isCooled": false,
    "customerNote": null,
    "eshopNote": null,
    "eligibility": {
      "isEligible": true,
      "warningTitle": null,
      "warningBody": null
    },
    "items": [{ "name": "...", "quantity": 1, "imageUrl": null, "setName": null }]
  },
  "shipment": {
    "shipmentGuid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "packages": [{ "name": "PKG-1" }],
    "alreadyExisted": false
  }
}
```

`shipment` is `null` when `eligibility.isEligible` is `false`.

**Error response:**
```json
{ "success": false, "errorCode": "ShoptetOrderNotFound" }
```

Error codes: `ShoptetOrderNotFound`, `ShipmentOrderWeightUnavailable`, `ShipmentCarrierNotResolved`, `ShipmentCreationFailed`.

---

### POST /api/packaging/orders/{orderCode}/shipment/reset

No request body required. Deletes the existing Shoptet shipment, then creates a new one.

**Success response:**
```json
{
  "success": true,
  "shipment": {
    "shipmentGuid": "new-guid",
    "packages": [{ "name": "PKG-1" }]
  }
}
```

Error codes: `NoShipmentToReset` (HTTP 409), `ShipmentDeleteFailed` (HTTP 503), `ShipmentCreationFailed`, `ShipmentCarrierNotResolved`, `ShipmentOrderWeightUnavailable`, `ShoptetOrderNotFound`.

---

### GET /api/packaging/orders/{orderCode}/label/pdf?shipmentGuid=...&packageName=...

Unchanged PDF proxy. Returns `application/pdf`. Used by `printLabelPdf.ts` via iframe print.

## 6. Shoptet Integration

See `docs/integrations/shoptet-api.md` §11 for shipment endpoints. Constraints:
- **No sandbox** — every API call hits the live store.
- Endpoints used: `GET /api/shipments?orderCode=...`, `GET /api/shipments/order/{code}/shipping-options`, `POST /api/shipments`, `DELETE /api/shipments/{shipmentGuid}`.
- Carrier code is resolved via shipping-options (`shippingId` cast to string).
- Label URL latency: Balíkobot may take a few seconds. The scan endpoint does NOT wait for label URL readiness; `printLabelPdf` fetches on demand.

## 7. Printing Model

- FE creates an invisible `<iframe>` with a Blob URL of the PDF.
- On iframe load: `iframe.contentWindow.print()` opens the browser print dialog.
- In kiosk mode the print dialog is auto-confirmed by the OS.
- Failure modes: PDF proxy returns 404 (label not yet ready, carrier latency) or fetch error (network). `printLabelPdf.ts` currently silently swallows errors; a future improvement would surface them.

## 8. Failure Modes & Recovery

| Failure | BE returns | FE displays |
|---|---|---|
| Shoptet order not found | `ShoptetOrderNotFound` (404) | Error banner |
| Order in wrong state | `success: true`, `eligibility.isEligible: false` | PackingStateWarning |
| Weight data missing | `ShipmentOrderWeightUnavailable` (422) | Error banner |
| Carrier not resolved | `ShipmentCarrierNotResolved` (422) | Error banner |
| Shipment creation fails | `ShipmentCreationFailed` (503) | Error banner |
| Shipment delete fails | `ShipmentDeleteFailed` (503) | Error banner (reset path) |
| No shipment to reset | `NoShipmentToReset` (409) | Error banner (reset path) |
| Label PDF not ready | HTTP 404 from label/pdf proxy | printLabelPdf silently fails (kiosk retry) |

## 9. Configuration

| Key | Location | Default | Purpose |
|---|---|---|---|
| `ShoptetOrdersSettings:PackingStateId` | `appsettings.json` | 26 | Shoptet status ID for "Balí se" |
| `ShipmentLabels:MinPackageWeightGrams` | `appsettings.json` | 100 | Floor for package weight sent to carrier |
| `ShipmentLabels:DefaultPackage*Mm` | `appsettings.json` | 300×200×150 | Default package dimensions |
| Kiosk mode (print auto-confirm) | Browser/OS setting | — | Set at device level; not app-configurable |

## 10. Future Improvements (out of scope here)

- DB-backed print history for audit log
- Per-package "printed" toggle in UI
- Multi-package workflow (currently creates one package per shipment)
- Server-side printing via CUPS print sink (`docs/superpowers/plans/2026-03-25-cups-print-sink.md`)
- Label-reprint audit log
- Surface `printLabelPdf` errors in the UI instead of silent failure

## 11. Glossary

| Term | Meaning |
|---|---|
| Balení / Balí se | Czech for "packing" / "is being packed" (the order status that gates packing) |
| Zásilka | Shipment (Shoptet entity created via Balíkobot) |
| Štítek | Label (the PDF output printed on the label printer) |
| Expedice | Dispatch / expedition (a related but separate workflow) |
