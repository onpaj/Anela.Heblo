## Module / File
`frontend/src/components/common/SupplierAutocomplete.tsx`

## Coverage
Line coverage: 0% (0/65 lines). Filter threshold: 60%

## What's not tested
`handleKeyDown`'s ArrowDown → focus-first-option DOM query is untested. `handleOptionKeyDown`'s ArrowUp at index 0 returning focus to input is an invariant with no test. The `handleInputChange` branch (`!newValue && value` → `onSelect(null)`) is dark. Click-outside close via `document.addEventListener` is also uncovered.

## Why it matters
Supplier selection is used in purchase order forms; if keyboard navigation regresses, keyboard-only users cannot select suppliers and have no workaround. The `onSelect(null)` clear path is critical for form reset flows — a silent regression here would leave stale supplier values in forms.

## Suggested approach
React Testing Library tests: (a) keyboard navigation sequence through options; (b) ArrowUp on first option restores input focus; (c) clearing input calls `onSelect(null)`; (d) click outside closes the dropdown. Estimated effort: ~2 hours.

---
_Filed by weekly coverage-gap routine on 2026-11-09. Based on CI nightly run #42456500800 (16643224c30a67e613312ac689cd872155b43bd1)._
