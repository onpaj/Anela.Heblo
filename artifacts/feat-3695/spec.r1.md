# Specification: Fix hardcoded DesiredStateId in PrintExpeditionOrderHandler's non-printable state guard

## Summary
`PrintExpeditionOrderHandler` blocks re-printing of orders that are already in a terminal/in-progress Shoptet state, using a static `NonPrintableStates` dictionary that hardcodes the "desired state after printing" as `26`. The same handler separately reads this value from `IOptions<PrintPickingListOptions>.DesiredStateId` when building the actual print request. This fix makes the guard read `DesiredStateId` from configuration instead of a hardcoded literal, so the two checks can never drift apart.

## Background
When an expedition order is printed, the handler transitions the order to a "desired state" (`DesiredStateId`, configurable via `PrintPickingListOptions`, default `26`/"Balí se"). Before printing, the handler must refuse to re-print orders that are already in that desired state (or in other terminal states), to avoid double-printing. Today this guard is implemented as a static dictionary literal containing `26` alongside three genuinely stable, non-configurable lifecycle states (`-3`, `52`, `70`). Because `DesiredStateId` is configuration and the static `26` entry is not wired to it, changing `DesiredStateId` in configuration would silently desynchronize the guard from the actual desired state, both under-blocking (new desired state no longer guarded) and over-blocking (stale old state 26 still guarded when it's no longer meaningful). This is an internal architecture-review finding with a known, unambiguous fix; no product behavior or public contract changes.

## Functional Requirements

### FR-1: Non-printable state guard must derive the "desired state" check from configuration
`PrintExpeditionOrderHandler.Handle` must reject printing when the order's current Shoptet status equals `_options.Value.DesiredStateId`, rather than relying on a hardcoded `26` entry in the static `NonPrintableStates` dictionary. The three remaining stable states (`-3` "zrušeno/blokováno", `52` "Zabaleno", `70` "Předáno přepravci") continue to be checked via the static dictionary, unchanged.

**Acceptance criteria:**
- The static `NonPrintableStates` dictionary no longer contains the entry for key `26`.
- Before (or as part of) the `NonPrintableStates` lookup, the handler checks whether `currentStatusId == _options.Value.DesiredStateId`; if true, it returns a `PrintExpeditionOrderResponse` with `ErrorCodes.ExpeditionOrderInvalidState` and `Params["currentStatusName"]` set to `"Balí se"` (matching current behavior for state 26 under default configuration), and does not call `_expeditionListService.PrintPickingListAsync`.
- With `PrintPickingListOptions.DesiredStateId` at its default value (`26`), handler behavior is unchanged: orders with status `26` are still rejected with `ExpeditionOrderInvalidState` and `currentStatusName` = `"Balí se"`.
- With `PrintPickingListOptions.DesiredStateId` set to a non-default value (e.g. `99`) via configuration/options: orders with status `99` are rejected as invalid-state; orders with status `26` are no longer rejected by this specific check (they fall through to the `NonPrintableStates` dictionary lookup, which no longer contains `26`, so they proceed to print as normal, exactly as any other non-listed state does today).
- The three pre-existing static entries (`-3`, `52`, `70`) continue to produce `ExpeditionOrderInvalidState` with their respective `currentStatusName` values, unaffected by the value of `DesiredStateId`.
- No change to the `Params` dictionary shape, `ErrorCodes` used, or the ordering/short-circuiting of the Shoptet-not-found (`404`) check, which still runs first.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. The change replaces a dictionary lookup with an equality comparison plus (potentially) a smaller dictionary lookup — both O(1), executed once per request on the existing hot path.

### NFR-2: Security
No change. No new inputs, no new external calls, no change to authorization or data exposure. `_options.Value.DesiredStateId` is already trusted server-side configuration used elsewhere in the same method.

## Data Model
No data model changes. `PrintPickingListOptions.DesiredStateId` (`int`, default `26`) is the existing configuration field being consumed; no new fields are introduced.

## API / Interface Design
No public API surface changes. `PrintExpeditionOrderRequest` / `PrintExpeditionOrderResponse` contracts, HTTP endpoint, and `ErrorCodes` values are unchanged. This is an internal implementation fix within `PrintExpeditionOrderHandler.Handle`.

## Dependencies
- `Anela.Heblo.Application.Features.ExpeditionList.PrintPickingListOptions` (already injected into the handler via `IOptions<PrintPickingListOptions>` — no new dependency wiring required).
- Existing unit tests in `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`, notably `Handle_OrderInNonPrintableState_ReturnsInvalidStateError` (theory case for status `26`) and `Handle_ValidState_PrintsWithOrderCodeAndDesiredState26`, must continue to pass unmodified since they exercise default-configuration (`DesiredStateId = 26`) behavior. A new test case covering a non-default `DesiredStateId` is recommended to lock in the fix, but is an implementation/test detail rather than a functional requirement change.

## Out of Scope
- Changing the default value of `DesiredStateId` or any other `PrintPickingListOptions` field.
- Making the other three `NonPrintableStates` entries (`-3`, `52`, `70`) configurable — they are confirmed stable, non-volatile lifecycle states per the arch-review finding.
- Any change to the print request construction (`ExpeditionPickingRequest.DesiredStateId = _options.Value.DesiredStateId`), which is already correct.
- Any change to error codes, response contract, or the Shoptet-not-found handling path.

## Open Questions
None.

## Status: COMPLETE
