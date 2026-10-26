# Spec r1 - Unit tests for isFormEditable (issue 4587)

## Goal
Add regression tests for the editability rules of `isFormEditable` in `frontend/src/components/pages/TransportBoxDetail.tsx` (lines ~108-122).

## Current behaviour (source of truth)
- `notes` -> always true, any state.
- state `New` -> only `boxNumber` true.
- state `Opened` -> only `items` true.
- any other state (e.g. InTransport, Received, Stocked, Closed, Error, undefined) -> false for items and boxNumber.

## Matrix (3 states x 3 field types = 9 required cases)
| state  | items | notes | boxNumber |
|--------|-------|-------|-----------|
| New    | false | true  | true      |
| Opened | true  | true  | false     |
| other  | false | true  | false     |

## Constraint
`isFormEditable` is a closure inside the `TransportBoxDetail` component (not exported, reads `boxData.transportBox.state`). Tests must observe it either through rendered UI or through a minimal, behaviour-preserving extraction.

## Acceptance criteria
1. All 9 combinations asserted; "other" covers at least one concrete non-New/Opened state plus undefined state.
2. Tests fail if any case in the function is changed.
3. No behaviour change in production code; `npm test`, `npm run build`, `npm run lint` pass.

## Out of scope
Other coverage gaps in TransportBoxDetail.tsx; backend changes; process docs (no process change).
