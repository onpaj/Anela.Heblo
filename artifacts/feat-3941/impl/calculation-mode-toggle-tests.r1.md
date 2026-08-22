# Implementation: calculation-mode-toggle-tests

## What was implemented
Added a new `describe('calculation-mode toggle', ...)` block to the existing
`ManufactureBatchCalculator.test.tsx` covering FR-4: verifying that the
`calculationMode` radio toggle defaults to batch-size mode once a template
loads, that clicking "Podle ingredience" swaps the rendered input group
(unmounting the batch-size input/showing the ingredient select + amount
input, per the ternary at lines ~314-416), and that each mode's "Vypočítat"
button invokes only its own calculation function (`calculateBySize` vs.
`calculateByIngredient`) and never the other one.

Four new test cases were added, reusing the existing `mockGetBatchTemplate`,
`mockCalculateBySize`, `mockCalculateByIngredient`, and `triggerProductSelect`
helpers already defined earlier in the file (from the
`test-infrastructure-mocks` prerequisite task) without redefining them.

## Files created/modified
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` — added the `calculation-mode toggle` describe block (local `templateWithIngredient` fixture, `renderWithSelectedProduct` helper, and 4 `it(...)` cases) right after the `URL parameter auto-selection` describe block, before the outer `ManufactureBatchCalculator` describe's closing brace.

## Tests
`frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx`:
- `defaults to batch-size mode once a template loads` — asserts the "Podle velikosti dávky" radio is checked, the batch-size label/input is present, and the ingredient amount label / `combobox` are absent.
- `switches to ingredient mode when "Podle ingredience" is clicked` — asserts clicking the second radio unmounts the batch-size input group and mounts the ingredient select + amount input.
- `invokes calculateBySize (not calculateByIngredient) when computing in batch-size mode` — fills the batch-size input, clicks "Vypočítat", asserts `calculateBySize('SEMI001', 150)` was called and `calculateByIngredient` was not.
- `invokes calculateByIngredient (not calculateBySize) when computing in ingredient mode` — switches mode, selects an ingredient, fills the amount, clicks "Vypočítat", asserts `calculateByIngredient('SEMI001', 'ING001', 30)` was called and `calculateBySize` was not.

## How to verify
```bash
cd frontend
npm install --legacy-peer-deps   # first time only; node_modules was not preinstalled in this checkout
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
npm run lint
npm run build
```

## Notes
- `node_modules` was not present in this worktree; `npm ci` failed on an
  `@types/node` peer-dependency conflict between the root project and `knip`,
  so `npm install --legacy-peer-deps` was used instead (same resolution
  approach implied by the existing lockfile conflict, not a change to any
  dependency version).
- The reference file structure (mocks, `triggerProductSelect`, the
  `URL parameter auto-selection` describe block ending in the FR-3
  lower-priority-case comment) matched the task description exactly, so no
  adaptation of the provided test code was needed — it was inserted verbatim.
- `npm run lint` reports 236 pre-existing errors across many unrelated test
  files in the repo (`no-node-access`, `no-wait-for-multiple-assertions`,
  etc.); none are attributable to `ManufactureBatchCalculator.test.tsx` —
  confirmed via `npm run lint | grep -A20 ManufactureBatchCalculator.test.tsx`
  returning no output.
- Only the target test file was modified, per the hard constraints; the
  component file `ManufactureBatchCalculator.tsx` was read but not changed.

## PR Summary
Adds FR-4 test coverage for the `calculationMode` radio toggle in `ManufactureBatchCalculator`: a new `calculation-mode toggle` describe block verifies the toggle defaults to batch-size mode once a template loads, that switching to ingredient mode unmounts the batch-size input and mounts the ingredient select/amount input, and that each mode's "Vypočítat" button calls only its own calculation function (`calculateBySize` or `calculateByIngredient`), never the other. This brings the test file from 17 to 21 passing tests; build and lint remain clean for this file.

### Changes
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` — added `calculation-mode toggle` describe block with 4 new tests

## Status
DONE
