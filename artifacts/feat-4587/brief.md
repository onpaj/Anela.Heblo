## Module / File
`frontend/src/components/pages/TransportBoxDetail.tsx`

## Coverage
Line coverage: 42% (filter threshold: 60%)

## What's not tested
`isFormEditable` returns different values based on box state (New/Opened/other) and field type (items/notes/boxNumber): New → boxNumber only editable; Opened → items only editable; notes always editable. No test covers this — a wrong case in the switch could allow editing items on a New box or editing box numbers on an Opened box. The function is called per-render for multiple field types; a regression affects which form controls render as interactive.

## Why it matters
Allowing item edits on a New box (before it has a box number) or blocking boxNumber edits on a New box would break the warehouse box-preparation workflow silently — no validation error, just wrong UI state.

## Suggested approach
Unit tests for `isFormEditable` with all combinations of state × fieldType (3 states × 3 field types = 9 cases). ~XS effort (pure function).

---
_Filed by weekly coverage-gap routine on 2026-10-26. Based on CI run #39780303564 (cad5f9218f4b3982c6605f346e1549562e175d67)._
