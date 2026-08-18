## Module / File
`frontend/src/components/pages/ManufactureBatchCalculator.tsx`

## Coverage
Line coverage: 29.3% (filter threshold: 60%)

## What's not tested
1. **`computePercentage` edge cases** — the exported pure function returns `'N/A'` when `newBatchSize` is null, zero, or non-finite. No unit test exercises these boundary conditions. A regression that changes the guard would produce NaN% or a division-by-zero output visible to the operator.
2. **Batch size fallback order** — when a template loads, the initial batch size uses `templateData.newBatchSize || templateData.originalBatchSize || 0` (MMQ first, BOM fallback, then zero). No test verifies this precedence, so swapping the two attributes would silently use the wrong default.
3. **URL parameter auto-selection** — when `?productCode=X&batchSize=Y` are present in the URL, the component auto-selects the product and pre-fills the batch size. No test verifies this flow, including that `handleProductSelect` receives the auto-constructed `CatalogItemDto` and that the URL batch size overrides the template default.
4. **Calculation mode toggle** — switching from batch-size mode to ingredient mode changes which calculation function is called. No test checks that the mode switch disables the other mode's inputs.

## Why it matters
`computePercentage` drives the % column that operators use to verify ingredient ratios during manufacturing. A wrong value or 'N/A' where a number is expected (or vice versa) leads to manual verification errors. The URL parameter flow is the entry point from `ManufactureOrder` — a broken auto-selection means operators arrive at the calculator without the right product pre-loaded.

## Suggested approach
- Unit test `computePercentage` directly: null batch size → 'N/A', zero → 'N/A', non-finite → 'N/A', valid inputs → correct percentage string
- Component test (React Testing Library) with a mocked `useManufactureBatch`:
  - URL `?productCode=X&batchSize=500` → product auto-selected, batch size set to 500 overriding template default
  - Template with `newBatchSize=100`, `originalBatchSize=200` → initial batch size is 100 (MMQ wins)
~2 h effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
