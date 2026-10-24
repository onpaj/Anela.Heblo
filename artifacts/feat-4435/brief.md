## Module / File
`frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx`

## Coverage
Line coverage: 0% (0/78 lines, filter threshold: 60%)

## What's not tested
`handleApplyFilters` constructs the `GetManufactureOrdersRequest` object from raw input state. Several conversions are untested: (1) empty string fields are mapped to `null` using `|| null` — a falsy check that would also coerce `"0"` to null if such a value appeared; (2) `state` uses a stricter `=== ""` check instead of `|| null`, making the two patterns inconsistent; (3) date strings are parsed with `new Date(fromDateInput)` without validation — an invalid date string produces an `Invalid Date` object that is passed silently to the API. The 3-state `manualActionRequired` quick-toggle cycles null → true → false → null and uses `setTimeout(50ms)` to auto-apply — the filter applied is the *previous* state because `setState` is async, and the apply fires before React re-renders.

## Why it matters
A filter bug would send the wrong query parameters to the server, causing the manufacture order list to show incorrect results silently. The `Invalid Date` case would produce a backend validation error that surfaces as a generic network failure. The 3-state toggle race means clicking it once applies the wrong value (e.g. clicking to enable "requires manual action" applies the previous null/off state first).

## Suggested approach
React Testing Library tests for the filter component: (1) filling fields and clicking "Použít filtry" calls `onFiltersChange` with expected null/value mapping; (2) clearing the product code sends `productCode: null`; (3) toggling `manualActionRequired` through all three states produces the correct filter values; (4) clicking reset calls with all-null request. ~3h.

---
_Filed by weekly coverage-gap routine on 2026-10-05. Based on CI run #36876655065 (923c24940cf65d6ce38a8b56d75709bba333ca91)._
