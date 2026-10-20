# Specification: Unit tests for computePercentage (ManufactureBatchCalculator)

## Summary
Issue #3962 asks for Vitest unit tests covering the exported pure helper `computePercentage` in `frontend/src/components/pages/ManufactureBatchCalculator.tsx`. Verification against the source shows the function and its guard exist as described, but the requested tests are already present in the repository (see Background). The remaining work is verification and, only if gaps are found, minimal additions.

## Background
Verified in source (`ManufactureBatchCalculator.tsx` lines 15-21):

```ts
export const computePercentage = (calculatedAmount: number, newBatchSize: number | null | undefined): string => {
  if (newBatchSize == null || !isFinite(newBatchSize) || newBatchSize <= 0) return 'N/A';
  return ((calculatedAmount / newBatchSize) * 100).toFixed(2) + '%';
};
```

Differences from the issue text: the issue describes "three guarded conditions" as separate checks; in reality it is a single combined guard (`== null`, `!isFinite`, `<= 0`). Behaviour is equivalent. The function is used at line 617 in the results table.

Existing coverage: `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` already imports `computePercentage` and has a `describe('computePercentage helper')` block with cases: normal value (184.5/1000 -> 18.45%), 100%, size 0, null, undefined, amount 0, 2-decimal rounding (1/3 -> 33.33%), NaN, negative size, negative amount (-5.00%), Infinity, -Infinity. This covers every branch named in the issue. The issue was filed from a CI run of 2026-08-24; the test file was last touched in 925de5ca (2026-10-08), so the gap was likely closed after filing (the file's history is shallow, so the exact commit that added them could not be determined).

## Functional Requirements

### FR-1: Guard-branch coverage
Each guard outcome returns the string 'N/A': null, undefined, 0, negative, NaN, Infinity (and -Infinity).
**Acceptance criteria:**
- A test exists for each of null, undefined, 0, negative, NaN, Infinity returning 'N/A'.

### FR-2: Happy-path coverage
**Acceptance criteria:**
- A test asserts a valid positive batch size yields `(amount/size*100).toFixed(2) + '%'`, including rounding.

### FR-3: Confirm tests pass and coverage is adequate
**Acceptance criteria:**
- `npm test -- ManufactureBatchCalculator` passes in `frontend/`.
- `npm run lint` passes for the touched file.
- If the existing tests fully satisfy FR-1/FR-2, no new test code is added; the implementer records this and the PR notes the issue is already satisfied.

## Non-Functional Requirements
### NFR-1: Scope
Test-only; no production code changes, no process-doc change (no sync/calculation/job behaviour changes).
### NFR-2: Security
Not applicable.

## Data Model
None.

## API / Interface Design
None. `computePercentage` is already exported.

## Dependencies
Vitest and Testing Library already configured in `frontend/`.

## Open Questions / Risks
- Issue appears already resolved; the implementer should confirm by running the suite (and optionally coverage) rather than duplicating tests. Line coverage of the whole file (29.3% reported) is driven by the untested component body, which is out of scope for this issue.
