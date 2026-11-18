# Material container label reprint — design

**Status:** approved 2026-11-18 · **Size:** small change

## Intent
Container labels (Code128 + human-readable code, 50×20 mm, `MaterialContainerLabelZplBuilder`) get damaged or lost. Today the only print path (`PrintMaterialContainerLabels`) always generates *new* unassigned codes. Operators need to print another copy of an *existing* code.

## Goals
1. **Material containers list** (`MaterialContainerList.tsx`): per-row "Dotisk" button that prints that exact code once.
2. **Handheld terminal**: new home tile "Dotisk štítku" → scan screen; every scanned code is immediately reprinted.

## Non-goals
- Lot labels (round, text-only, no barcode) — untouched.
- Printing multiple copies per reprint; changing label layout; changing container state.

## Approaches
- **Chosen — dedicated reprint endpoint** taking one existing code, reusing the existing ZPL builder, printer service and media-state check. Creates no rows.
- *Rejected — extend `PrintMaterialContainerLabels` with an optional code list:* mixes "generate new" and "reprint" semantics in one handler and validator.
- *Rejected — frontend-only (build ZPL client-side):* printing goes through the backend `ILabelPrintingService`; no client path exists.

## Design
- **Backend:** `ReprintMaterialContainerLabel` use case (`POST /api/material-containers/{code}/reprint`, body `{ mediaChangeConfirmed }`), `Feature.Manufacture_MaterialContainers` Write.
  - Unknown code → `UnknownMaterialContainerCode` (existing error code).
  - Discarded container → refused with a new error code (my call: a discarded container shouldn't get a fresh label). New ErrorCode needs the ErrorHandlingTests range bucket + Czech i18n entry.
  - Media-type check identical to the existing print: returns `RequiresMediaChangeConfirmation = true` when the printer last printed a different media type and the request isn't confirmed; records the print on success.
- **List page:** row button → reprint mutation; reuses the existing `PrinterMediaChangeDialog` flow.
- **Terminal:** route `/terminal/reprint-label`, scan via `useScanScreen`; success flashes ok + code, error flashes err with message. On media-change response, a bottom sheet asks to change the roll; on confirm the pending code is printed and confirmation is remembered for the rest of the screen session.

## Left to the builder
Icon choice, exact Czech copy, test layout (BE handler unit tests, FE component tests for list button and terminal screen). Process doc for label printing under `docs/processes/` updated if one owns this code.
