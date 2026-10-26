## Module / File
`frontend/src/components/pages/PurchaseOrderForm.tsx`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
`normalizeSupplierName` chains five regex replacements to normalise Czech company suffixes (s.r.o., a.s., spol. s.r.o.) — edge cases like `"s. r. o."` with spaces are not covered, and a wrong normalisation means planning items are never shown for matching suppliers. `filteredPlanningItems` uses exact-then-partial logic, so a supplier whose normalised name is a substring of another would produce false positives. `handleAddFromPlanningList` has an API-success path (exact match, fallback to plain object) and a catch path — the fallback object uses price 0, which could silently produce zero-cost order lines.

## Why it matters
If supplier name normalisation fails, planning-list filtering always shows zero items for the selected supplier, making the planning-list feature effectively invisible. Zero-price fallback lines submitted to create orders would require manual correction in the supplier's system.

## Suggested approach
Unit tests for `normalizeSupplierName` with representative Czech company names including spaced variants; unit tests for `filteredPlanningItems` memo; mock `fetch` to test success and failure paths of `handleAddFromPlanningList`. ~S effort.

---
_Filed by weekly coverage-gap routine on 2026-10-26. Based on CI run #39780303564 (cad5f9218f4b3982c6605f346e1549562e175d67)._
