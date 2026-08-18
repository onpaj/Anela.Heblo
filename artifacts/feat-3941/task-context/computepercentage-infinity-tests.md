### task: computepercentage-infinity-tests

**Goal of this task:** implement FR-1 — cover the `Infinity`/`-Infinity` branch of `computePercentage`'s `!isFinite(newBatchSize)` guard (`frontend/src/components/pages/ManufactureBatchCalculator.tsx` line 19), which the existing `NaN` test does not exercise.

**Prerequisite:** `test-infrastructure-mocks` task must be complete (this task only adds two `it` blocks; it does not depend on the mock restructuring's behavior, but is sequenced after it per this plan's task order).

**Files:**
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` (modified)

#### Step 1 — Add the two edge-case tests

In `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx`, find the end of the `computePercentage helper` describe block:

```tsx
  it('returns negative percentage when calculatedAmount is negative', () => {
    expect(computePercentage(-50, 1000)).toBe('-5.00%');
  });
});
```

Replace it with:

```tsx
  it('returns negative percentage when calculatedAmount is negative', () => {
    expect(computePercentage(-50, 1000)).toBe('-5.00%');
  });

  it('returns "N/A" when newBatchSize is Infinity', () => {
    expect(computePercentage(100, Infinity)).toBe('N/A');
  });

  it('returns "N/A" when newBatchSize is -Infinity', () => {
    expect(computePercentage(100, -Infinity)).toBe('N/A');
  });
});
```

(This closes the `computePercentage helper` describe block, same as before — only the two new `it`s are inserted before its closing `});`.)

#### Step 2 — Run the suite to confirm both new cases pass

```bash
cd frontend
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```

Expected output:

```
PASS src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
Test Suites: 1 passed, 1 total
Tests:       13 passed, 13 total
```

#### Step 3 — Commit

```bash
git add frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
git commit -m "test(manufacture-batch-calculator): cover computePercentage Infinity/-Infinity edge cases"
```

---

