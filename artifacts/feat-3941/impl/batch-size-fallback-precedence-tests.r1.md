# Implementation: batch-size-fallback-precedence-tests

## What was implemented
Added a `batch-size fallback precedence` describe block to the `ManufactureBatchCalculator` test suite, covering all three branches of the `templateData.newBatchSize || templateData.originalBatchSize || 0` precedence used in `handleProductSelect` (`ManufactureBatchCalculator.tsx` lines 89-92):
1. `newBatchSize` (MMQ) present and non-zero → wins, used for both the prefilled input and the auto `calculateBySize` call.
2. `newBatchSize` falsy (0) but `originalBatchSize` (BOM) present → BOM value is used as fallback.
3. Both `newBatchSize` and `originalBatchSize` falsy (0) → batch-size input stays empty and `calculateBySize` is never called (guarded by the `batchSizeToUse > 0` check).

## Files created/modified
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` — added the `batch-size fallback precedence` describe block with three new `it` blocks, inserted before the closing of the `ManufactureBatchCalculator` describe block, exactly as specified in the task.

## Tests
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx`
  - `prefills batch size with newBatchSize (MMQ) when both newBatchSize and originalBatchSize are present`
  - `falls back to originalBatchSize (BOM) when newBatchSize is falsy`
  - `leaves batch size empty and does not auto-calculate when both newBatchSize and originalBatchSize are falsy`

## How to verify
```bash
cd frontend
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```
Result: `Test Suites: 1 passed, 1 total` / `Tests: 16 passed, 16 total` (matches the expected output in the task spec exactly).

## Notes
- Verified the prerequisite `test-infrastructure-mocks` task was already complete: `mockGetBatchTemplate`, `mockCalculateBySize`, and `triggerProductSelect` all existed in the test file exactly as expected before this change.
- Read `ManufactureBatchCalculator.tsx` lines 65-109 and confirmed the `handleProductSelect` logic matches the task's reference description precisely (including the `batchSizeToUse > 0` guard before calling `calculateBySize`), and confirmed the batch-size `<input type="number">` (line 320-333) is the only element with `placeholder="0.00"` rendered when `calculationMode === "batch-size"` (the default), since the ingredient-mode input with the same placeholder only renders in the other, mutually exclusive branch. No discrepancies found — the test code was applied exactly as specified in the task, no adjustments were needed.
- The sandbox worktree had no `frontend/node_modules` installed. Since `frontend/package-lock.json` in this worktree is byte-identical to the one in the main checkout (`/home/user/Anela.Heblo/frontend/package-lock.json`), I temporarily symlinked `frontend/node_modules` to the main checkout's installed modules to run the test suite, then removed the symlink afterward (node_modules is gitignored and not part of the commit).

## PR Summary

Added test coverage for the batch-size fallback precedence logic (`newBatchSize` → `originalBatchSize` → `0`) in `ManufactureBatchCalculator`'s product-select handler, closing a gap where the MMQ/BOM/zero precedence chain and the "don't auto-calculate when both are zero" guard were previously untested.

### Changes
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` — added a `batch-size fallback precedence` describe block with three tests covering the MMQ-wins, BOM-fallback, and both-falsy/no-auto-calculate cases.

## Status
DONE
