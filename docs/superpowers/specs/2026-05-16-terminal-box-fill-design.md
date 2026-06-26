# Terminal — Adding Items to Box ("Plnění boxu")

**Date:** 2026-05-16
**Status:** Approved — ready for implementation planning

## Purpose

Give warehouse workers a mobile terminal workflow for filling a transport box
with manufactured products. The worker scans an empty box, picks items from the
manufactured-product inventory ("manufacture warehouse"), and finishes by
scanning the box again to move it to `InTransit` ("v přepravě"). Adding an item
consumes it from the manufactured-product inventory; removing an item restores
it. This is the mobile counterpart of the existing desktop transport-box detail
flow.

## Background

The backend already supports the full add-items-to-box flow:

- `TransportBox` state machine: `New → Opened` (requires a `B###` code) →
  `InTransit`. Items can only be added in `Opened` state; `ToTransit` requires
  at least one item.
- `AddItemToBox` use case (`POST /api/transport-boxes/{id}/items`) adds an item
  and consumes from `ManufacturedProductInventoryItem` via `Consume`, with
  overdraft / negative-stock support (`allowNegativeStock`).
- `RemoveItemFromBox` use case (`DELETE /api/transport-boxes/{id}/items/{itemId}`)
  removes an item and restores the consumed stock via `Restore`.
- `ChangeTransportBoxState` use case (`PUT /api/transport-boxes/{id}/state`)
  performs `New → Opened` and `Opened → InTransit` transitions.
- `GetTransportBoxByCode` (`GET /api/transport-boxes/by-code/{boxCode}`) looks
  up a box by code.

The terminal is a mobile PWA (`/terminal`) with a tile-based home
(`TerminalHome`). This feature adds a fourth, fully functional tile; the
existing three tiles remain "coming soon".

The desktop already "remembers the last amount per product" through a
`lastManufacturedItems` lookup in the transport-box detail UI.

## Scope decisions

These were confirmed during brainstorming:

1. **Box scan** — "Open new or resume existing": if an `Opened` box with the
   scanned code exists, resume it; otherwise create a new box and open it. A box
   whose code is busy in another active state produces an error.
2. **Item picking** — searchable, tappable list of manufactured-inventory items
   with stock. No product-barcode scanning.
3. **Corrections** — the terminal lists items already in the box; tapping one
   removes it and restores the consumed stock to the warehouse.
4. **Overdraft** — mirror the desktop dialog: offer "add with negative stock"
   or "add only the remaining amount".
5. **Architecture** — one new thin backend use case for the atomic
   open-or-resume step; every other step reuses existing endpoints.

## Architecture

### Flow

A single terminal route, `/terminal/box-fill`, with three sequential steps plus
a done screen:

1. **Scan box** — identify / open the box.
2. **Add items** — pick manufactured products, enter amounts, remove mistakes.
3. **Confirm transit** — scan the box again to move it to `InTransit`.
4. **Done** — success summary, with a "Další box" button to loop back to step 1.

### Backend — new use case `OpenOrResumeBoxByCode`

- Endpoint: `POST /api/transport-boxes/open-by-code`
- Request: `{ boxCode: string }`
- Response: `{ success, transportBox: TransportBoxDto, resumed: bool,
  errorCode?, params? }`

Handler logic (atomic, single handler / transaction):

1. Normalize the code to uppercase and validate the format (`B` + 3 digits).
   On invalid format, return an error response.
2. `GetByCodeAsync(code)`:
   - Box in `Opened` state → return it, `resumed: true`.
   - Box in `New` state → call `box.Open(code, ...)`, return it,
     `resumed: false`.
   - Box in `Closed` state, or no box found → create a new `TransportBox`,
     call `Open(code, ...)`; close any `Stocked` box sharing the code
     (mirrors the existing `HandleNewToOpened` callback).
   - Box in any other active state (`InTransit`, `Received`, `Reserve`,
     `Quarantine`, `Stocked`, `InSwap`, `Error`) → return error
     `TransportBoxDuplicateActiveBoxFound` with the code and current state in
     `params`.
3. Persist and return the mapped `TransportBoxDto`.

No orphan `New` boxes can be left behind, and concurrent scans of the same code
cannot both succeed, because creation and opening happen in one handler.

The controller endpoint is added to `TransportBoxController`. The generated
TypeScript/C# API clients pick up the new endpoint on build.

### Backend — reused endpoints (unchanged)

- `POST /api/transport-boxes/{id}/items` — add item, consume inventory.
- `DELETE /api/transport-boxes/{id}/items/{itemId}` — remove item, restore
  inventory.
- `PUT /api/transport-boxes/{id}/state` with `newState = InTransit` — move the
  box to transit (`ToTransit`, which already requires at least one item).

### Frontend — components

New folder `frontend/src/components/terminal/box-fill/`:

- **`BoxFillWorkflow.tsx`** — orchestrator. Holds the current step, the box
  DTO, and the amount-memory map. Rendered by the `/terminal/box-fill` route.
- **`ScanBoxStep.tsx`** — large, autofocused scan input. Submits on Enter
  (hardware scanners emit Enter). Calls `open-by-code`. When the response has
  `resumed: true` and the box already has items, shows a banner so the worker
  knows they are continuing an existing box.
- **`AddItemsStep.tsx`** — header showing the box code and item count; a
  searchable list of manufactured inventory with stock
  (`useManufacturedProductInventoryQuery({ onlyWithStock: true })`); tapping an
  item opens an amount entry prefilled from amount-memory; below the list, the
  box's current items, each tappable to remove (confirm prompt →
  `RemoveItemFromBox`). A primary button "Odeslat do přepravy", disabled until
  the box has at least one item, advances to step 3.
- **`OverdraftSheet.tsx`** — bottom sheet mirroring the desktop dialog:
  "Přidat záporný stav", "Přidat pouze zbývající", "Zrušit".
- **`ConfirmTransitStep.tsx`** — prompts "Naskenujte box B### pro potvrzení";
  scan input; the client compares the scanned code to the box code. A match
  calls `PUT state = InTransit`; a mismatch shows an inline error and stays on
  the step.
- **`BoxFillDoneStep.tsx`** — success summary (box code, item count) plus a
  "Další box" button that resets to step 1.
- **`TerminalScanInput.tsx`** — reusable large scan field shared by the scan
  and confirm steps; usable by future terminal workflows.

`TerminalHome` gains a fourth tile (active — no "Brzy k dispozici" label),
title "Plnění boxu", description along the lines of "Naskenujte box, přidejte
vyrobené produkty a odešlete do přepravy". `App.tsx` routes
`/terminal/box-fill` to `BoxFillWorkflow` instead of `ComingSoonPage`.

API access reuses the existing `useManufacturedProductInventory` hook and the
generated transport-box client methods; a new hook wraps the `open-by-code`
endpoint.

### Amount memory

An in-memory `Map<productCode, lastAmount>` held in `BoxFillWorkflow` state.
Updated on every successful add; used to prefill the amount field when an item
is selected. It survives across boxes within the terminal session and is
cleared on a full page reload. Keyed by product code (not lot) — amount memory
is per product, matching the worker's mental model.

## Data flow

1. **Scan box** → `POST /open-by-code { boxCode }` → `TransportBoxDto` stored
   in workflow state.
2. **Add item** → `POST /{id}/items { productCode, productName, amount,
   sourceInventoryId, lotNumber, expirationDate, allowNegativeStock }` →
   updated box; inventory consumed. Amount memory updated.
3. **Remove item** → `DELETE /{id}/items/{itemId}` → updated box; inventory
   restored.
4. **Confirm transit** → client checks scanned code == box code →
   `PUT /{id}/state { newState: InTransit }` → box in `InTransit`.

## Error handling & edge cases

- Invalid box-code format → inline error, no network call.
- Code busy in another active state → message
  "Box B### se už používá (stav: …)".
- Empty box at the transit step → send button disabled; backend `ToTransit`
  also guards as a backstop.
- Scan mismatch at the confirm step → inline error, no state change, stay on
  the step.
- Overdraft (amount > available stock) → `OverdraftSheet` with negative-stock
  or remaining-only options.
- Network / API errors → toast with retry. No partial state, because
  `open-by-code` is atomic.
- Resuming a box that already has items → informational banner on entry to the
  add-items step.

## Testing

- **Backend** — unit tests for `OpenOrResumeBoxByCodeHandler`: create+open a
  new box, resume an `Opened` box, open an existing `New` box, `Closed`→new
  box, busy-code error, invalid-format error.
- **Frontend** — component tests per step plus a workflow test covering the
  happy path (scan → add → remove → transit), overdraft handling, and the
  scan-mismatch case. Follows the existing `TerminalHome.test.tsx` pattern.
- **E2E** — one Playwright spec under `frontend/test/e2e/` in the
  transport/logistics module. Optional; the E2E suite runs nightly.
- **Validation gate** — `dotnet build` + `dotnet format`; `npm run build` +
  `npm run lint`; all touched tests pass.

## Out of scope

- Product-barcode/EAN scanning for item selection (list-based picking only).
- Editing item amounts in place (remove and re-add instead).
- Any change to the desktop transport-box flow.
- Reviving the other three "coming soon" terminal tiles.
