---
process: flow-lots-and-material-containers
kind: workflow
module: catalog
summary: Pre-printed barcode labels for material containers that are later bound to a material and lot when goods are received, plus round lot/expiration labels for semi-products — ZPL sent to the Zebra printer via CUPS and tracked in Heblo tables.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/**
  - backend/src/Anela.Heblo.Persistence/Catalog/Inventory/**
  - backend/src/Anela.Heblo.API/Controllers/LotsController.cs
  - backend/src/Anela.Heblo.API/Controllers/MaterialContainersController.cs
  - backend/src/Anela.Heblo.Application/Shared/Printing/FeatureGatedLabelPrintingService.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsLabelPrintingService.cs
verified_at: "5e993f9e2"
related: [sync-catalog-master-data]
---

# Lots, material containers and label printing

## Purpose
Gives every physical container of raw material (sack, canister, bucket) a unique scannable code,
so production can tell which material and which supplier lot (šarže) is inside, and gives
semi-product batches round labels with their lot number and expiration. Staff use:
- **Šarže** page `/manufacturing/material-containers` — list, print new container
  labels, discard containers, lot-label calibration tab.
- **Terminal** `/terminal/po/…/material/:material` (receiving against a purchase order line) and
  `/terminal/freeform` — scan a pre-printed label and bind it to material + lot (+ amount).
- **Manufacture order detail** — "print lot labels" for the order's semi-product.
Permissions: Manufacture_MaterialContainers (write to change/print), Manufacture_LabelCalibration
for calibration.

## Trigger
User actions only; no job. States of a container (`MaterialContainerStatus`):
**Unassigned** (label printed, nothing inside) → **Assigned** (bound to material + lot) →
**Discarded**. Only Unassigned can be assigned.

## Data flow
1. **Print container labels** — `POST /api/material-containers/print-labels` with a count:
   1. Printer media check (below); may return `RequiresMediaChangeConfirmation`.
   2. Take `count` numbers from Postgres sequence `material_container_internal_seq` → codes
      `M00000001` style (`M` + 8 digits).
   3. Insert that many **Unassigned** rows into `public."MaterialContainers"` and save.
   4. Build ZPL: 50 × 20 mm gap-sensed label, Code128 barcode + code text, one label per code.
   5. Print (below) and record the media type in `public."PrinterMediaStates"`.
2. **Assign** — `POST /api/material-containers` with items (code, material code, lot code,
   optional amount + unit, optional purchase order line id): every code must exist and be
   Unassigned; a given PO line must exist; then all are assigned in one save. Errors:
   `UnknownMaterialContainerCode`, `MaterialContainerCodeExists` (duplicate or not Unassigned),
   `PurchaseOrderLineNotFound`. The terminal pre-fills the lot with the last lot used for that
   material (`GET /api/material-containers/last-used-lot`).
3. **Discard** — `POST /api/material-containers/{id}/discard`.
4. **Lots** (`public."Lots"`, unique per material code + lot code) — `api/lots` list, get,
   create, update (expiration, received date, notes), delete. Delete is refused
   (`LotHasEans`) while any container references the lot.
5. **Print lot labels** — `POST /api/lots/print-labels` with lot number, expiration, count:
   media check, read calibration from `public."LotLabelCalibrations"`, build ZPL for round
   10 mm labels (two centred text lines: lot number, expiration; no barcode) on **continuous**
   media, print, record media type. Nothing about the printed lot is stored.
6. **Calibration** — `GET/PUT /api/lots/label-calibration` (pitch 80–400 dots, default 148;
   drift 0–1000 dots per 100 labels, default 30), `POST …/label-calibration/nudge` (up/down,
   fast 0.30 / slow 0.15 dot per label), `POST /api/lots/print-calibration-label`,
   `POST /api/lots/feed-media` (feed N dots).
7. **Printing** — `ILabelPrintingService` = `FeatureGatedLabelPrintingService` →
   `CupsLabelPrintingService`: when feature flag `is-label-printing-enabled` is on, send the raw
   ZPL to CUPS queue `Cups:LabelPrinterName` on `Cups:ServerUrl`; when off, skip the physical
   print and log it — the rest of the operation still runs.

## Logic & formulas
- **Media-change guard**: `PrinterMediaState` remembers the last media type printed
  (MaterialContainer or LotRound). Printing the other type requires the user to confirm the roll
  was changed (`mediaChangeConfirmed`); Unknown never asks.
- **Lot label pitch**: each label advances `PitchDots` plus an evenly spread drift correction —
  cumulative extra after label *i* = round(i × drift / 100) — so long runs don't creep
  off the round die-cuts. Container labels switch the printer back to gap sensing (`^MNY`).
- The calibration and media state are single rows (id 1), created with defaults on first use.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `is-label-printing-enabled` (feature flag) | default true; off on Staging | Physical print on/off |
| `Cups:ServerUrl` | `http://100.86.160.29:631` (Tailscale dev; PROD/STG `https://10.0.0.4:631` per comment) | CUPS server |
| `Cups:LabelPrinterName` | `Zebra_ZD411` | Raw ZPL queue; empty → print throws |
| `Cups:Username`, `Cups:Password` | placeholders; secrets | CUPS auth |

## Runtime facts
- 2026-09-29: a slow Plaud polling run on the single Hangfire worker delayed manual print jobs by
  12–15 min; check job state latency and `lpstat` on vmHebloInfra when prints are late — agent
  memory `gotcha_hangfire_single_worker_starves_print` — 2026-09-29. (Applies to Hangfire-queued
  prints; the label prints here are sent synchronously from the request.)

## Known quirks
- **Containers are saved before printing.** If CUPS fails, the Unassigned rows exist without
  physical labels; the sequence numbers are consumed either way.
- **Printing off still creates containers** (flag off): codes exist in the DB with no label —
  intended for Staging, surprising elsewhere.
- **Heblo `Lots` ≠ Flexi lots.** This table is Heblo's own lot register for labels/containers;
  the catalog's lot stock and expirations come from Flexi (`sync-catalog-master-data`). Nothing
  syncs the two.
- `InventoryConstants.ContainerCodePrefix` ("INT-") is unused; real codes are `M########`.
- Lot labels have no barcode (a symbology does not fit a 10 mm round label).
- The ZPL can be previewed without the printer via Labelary (agent memory
  `pattern_zpl_labelary_preview`).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/PrintMaterialContainerLabels/PrintMaterialContainerLabelsHandler.cs` — create + print containers
- `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateMaterialContainers/CreateMaterialContainersHandler.cs` — assign rules
- `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Printing/LotLabelZplBuilder.cs` — round label, pitch/drift
- `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Printing/MaterialContainerLabelZplBuilder.cs` — container label
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/PrinterMediaState.cs` — media-change guard
- `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerCodeGenerator.cs` — sequence
- `backend/src/Anela.Heblo.Application/Shared/Printing/FeatureGatedLabelPrintingService.cs` — feature flag gate
- `frontend/src/components/terminal/lot-identification/ReceiveScreen.tsx` — terminal assign screen
