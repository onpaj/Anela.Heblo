# Baleni packing — shipping label print integration

**Date:** 2026-05-19
**Status:** Design approved

## Context

The Baleni packing kiosk (`BaleniPacking.tsx`) already scans an order code and shows
its detail (customer, items, cooling). A separate backend integration —
`POST /api/shipment-labels` — already retrieves Shoptet shipment labels for an order
(`ShipmentLabelDto[]`, each with `labelUrl` PDF and `labelZpl`). The two are not
connected: the generated client `shipmentLabels_GetLabels` is not consumed anywhere.

This change wires them together so that scanning an order also prints its shipping
label, hands-free, completing the packing workflow.

## Goal

After an order is scanned and its detail loads in `BaleniPacking`, **if the order is
in the packing state**, the kiosk automatically fetches and prints the shipping label
PDF — the first label automatically, every additional package label gated behind a
confirmation button, printed one at a time.

## Decisions

- **Print medium:** carrier PDF (`labelUrl`). The kiosk's Zebra printer is installed
  as a normal OS printer; printing goes through the browser print path.
- **Trigger:** automatic on successful order load (hands-free).
- **Multiple packages:** print the first label automatically; each subsequent label
  requires an explicit confirmation tap. One label per print job.
- **Packing state gate:** labels are fetched and printed only when
  `isInPackingState === true`. Otherwise the existing `PackingStateWarning` shows and
  nothing prints.
- **Errors:** purely informational inline banner; never blocks the packer.

## Architecture

### Backend — PDF proxy endpoint

`labelUrl` points to a cross-origin carrier/Shoptet-hosted PDF. Browsers block
`iframe.print()` on cross-origin documents, so the PDF must be served same-origin.

Add a streaming endpoint alongside the existing `POST /api/shipment-labels`:

```
GET /api/shipment-labels/pdf?orderCode={code}&shipmentGuid={guid}&packageName={name}
  → 200 application/pdf   (streamed bytes)
  → 404                    order / shipment / package / labelUrl not found
```

New use case `GetShipmentLabelPdf` under
`backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/`:

- Request: `orderCode`, `shipmentGuid`, `packageName`.
- Handler calls `IShipmentClient.GetLabelsByOrderCodeAsync(orderCode)` and resolves the
  matching package's `labelUrl` **server-side** — the frontend never passes a raw URL,
  avoiding SSRF.
- Downloads that PDF via an injected `HttpClient` and returns a `FileStreamResult`
  (`application/pdf`).
- Missing package or `labelUrl` (ZPL-only / not generated) → 404 with a clear error code.
- `IShipmentClient` / `HttpClient` failures → logged, returned as 404/500, never thrown
  to the kiosk.

Controller method added to
`backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs`.

The existing `POST /api/shipment-labels` is unchanged — the frontend uses it to learn
how many labels exist and their identifiers.

### Frontend

**`useShipmentLabels(orderCode, enabled)`** — `frontend/src/api/hooks/`
- Calls generated `shipmentLabels_GetLabels({ orderCode })`.
- `enabled` true only when the packing order loaded successfully **and**
  `isInPackingState === true`.
- Returns `labels[]`, `isLoading`, and a mapped error: `2902` → "zásilka zatím nebyla
  vytvořena", `2903` → "štítky zatím nebyly vygenerovány", other → generic message.
- `gcTime: 0`, no retry (mirrors `usePackingOrder`).

**`PackingLabelPrinter`** — `frontend/src/components/baleni/`
- Props: `orderCode` + the loaded packing order.
- Drives `useShipmentLabels` and a `printedCount` state.
- On labels loaded: auto-prints `labels[0]`.
- While `labels.length > printedCount`: renders a large touch button
  "Vytisknout štítek {n}/{total}"; a tap prints the next label. One at a time.
- On label error: renders the inline error banner; order detail stays visible.
- Resets fully when `orderCode` changes (new scan).
- Renders nothing visible in the happy single-label path.

**`printLabelPdf(orderCode, label)`** — print utility in `frontend/src/components/baleni/`
- Builds the same-origin absolute URL
  `${apiClient.baseUrl}/api/shipment-labels/pdf?orderCode=…&shipmentGuid=…&packageName=…`
  (absolute URL per the project's API-hook rule).
- Creates a hidden `<iframe>`, sets `src`, on `load` calls `contentWindow.print()`,
  removes the iframe afterward.

**`BaleniPacking.tsx` wiring**
- After the existing order render, mount `<PackingLabelPrinter>` when the order loaded
  and is in packing state. Existing scan/detail logic unchanged.

## Data flow

1. Packer scans order code → existing `GetPackingOrder` loads the detail.
2. If `isInPackingState === true`, `useShipmentLabels` fires.
3. Labels loaded → `PackingLabelPrinter` auto-prints `labels[0]` via hidden iframe
   pointing at the same-origin PDF proxy endpoint.
4. If more packages exist → confirmation button shown → tap prints the next label;
   repeat until all printed.
5. Any label/PDF error → inline banner; order detail and scanner stay ready.
6. New scan → `PackingLabelPrinter` resets and the cycle repeats.

## Error handling

| Situation | Behavior |
|-----------|----------|
| `2902` no shipment | Banner "Štítek nelze vytisknout — zásilka zatím nebyla vytvořena" |
| `2903` labels not generated | Banner "Štítky zatím nebyly vygenerovány" |
| PDF proxy 404 (no `labelUrl`) | Banner "Štítek se nepodařilo vytisknout" |
| Network / 500 | Generic banner |
| Order not in packing state | `PackingStateWarning` shown; labels query disabled; nothing prints |

Errors are always informational — they never block the packer or the scanner.

## Testing

- **Backend unit** — `GetShipmentLabelPdfHandler`: resolves the matching package PDF and
  streams it; 404 when order/shipment/package/`labelUrl` missing; `HttpClient` failure
  handled gracefully.
- **Frontend unit** — `useShipmentLabels` (enabled gating, error-code mapping);
  `PackingLabelPrinter` (auto-print first label, confirmation button gates each
  subsequent print one at a time, reset on new scan, error banner). Print utility mocked.
- **E2E** — extend `frontend/test/e2e/baleni/packing.spec.ts` to assert the confirmation
  button appears for a multi-package order. Actual printing is not E2E-testable.
- **Validation** (per `CLAUDE.md`): BE `dotnet build` + `dotnet format`;
  FE `npm run build` + `npm run lint`.

## Deployment note

Silent (no-dialog) printing requires the kiosk's Chrome to run with the
`--kiosk-printing` flag and the Zebra printer set as the default OS printer. This is a
kiosk configuration concern, not application code.
