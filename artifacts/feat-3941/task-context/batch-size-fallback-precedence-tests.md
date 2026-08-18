### task: batch-size-fallback-precedence-tests

**Goal of this task:** implement FR-2 — verify the `templateData.newBatchSize || templateData.originalBatchSize || 0` precedence in `handleProductSelect` (`frontend/src/components/pages/ManufactureBatchCalculator.tsx` lines 89–92) across all three cases: MMQ present (wins), MMQ falsy with BOM present (fallback), and both falsy (empty, no auto-calculate call).

**Prerequisite:** `test-infrastructure-mocks` task must be complete — this task relies on `mockGetBatchTemplate`, `mockCalculateBySize`, and `triggerProductSelect` existing in the file exactly as that task defined them.

**Files:**
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` (modified)

Reference: `frontend/src/components/pages/ManufactureBatchCalculator.tsx` lines 71–108 — `handleProductSelect` awaits `getBatchTemplate`, and only if `templateData.success` is true does it compute `defaultBatchSize` and (if `batchSizeToUse > 0`) call `calculateBySize(product.productCode, batchSizeToUse)`.

#### Step 1 — Add the `batch-size fallback precedence` describe block

In `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx`, find the end of the `ManufactureBatchCalculator` describe block (the existing smoke test's closing):

```tsx
    // No percentage column header should appear before a calculation is run
    expect(screen.queryByRole('columnheader', { name: '%' })).not.toBeInTheDocument();
  });
});
```

Replace it with:

```tsx
    // No percentage column header should appear before a calculation is run
    expect(screen.queryByRole('columnheader', { name: '%' })).not.toBeInTheDocument();
  });

  describe('batch-size fallback precedence', () => {
    it('prefills batch size with newBatchSize (MMQ) when both newBatchSize and originalBatchSize are present', async () => {
      mockGetBatchTemplate.mockResolvedValue({
        success: true,
        productCode: 'SEMI001',
        productName: 'Test Semi Product',
        originalBatchSize: 200,
        newBatchSize: 100,
        scaleFactor: 0.5,
        ingredients: [],
      });

      render(
        <BrowserRouter>
          <ManufactureBatchCalculator />
        </BrowserRouter>,
      );

      triggerProductSelect(testProduct);

      await waitFor(() => {
        expect(mockCalculateBySize).toHaveBeenCalledWith('SEMI001', 100);
      });
      expect(screen.getByDisplayValue('100')).toBeInTheDocument();
    });

    it('falls back to originalBatchSize (BOM) when newBatchSize is falsy', async () => {
      mockGetBatchTemplate.mockResolvedValue({
        success: true,
        productCode: 'SEMI001',
        productName: 'Test Semi Product',
        originalBatchSize: 200,
        newBatchSize: 0,
        scaleFactor: 1,
        ingredients: [],
      });

      render(
        <BrowserRouter>
          <ManufactureBatchCalculator />
        </BrowserRouter>,
      );

      triggerProductSelect(testProduct);

      await waitFor(() => {
        expect(mockCalculateBySize).toHaveBeenCalledWith('SEMI001', 200);
      });
      expect(screen.getByDisplayValue('200')).toBeInTheDocument();
    });

    it('leaves batch size empty and does not auto-calculate when both newBatchSize and originalBatchSize are falsy', async () => {
      mockGetBatchTemplate.mockResolvedValue({
        success: true,
        productCode: 'SEMI001',
        productName: 'Test Semi Product',
        originalBatchSize: 0,
        newBatchSize: 0,
        scaleFactor: 0,
        ingredients: [],
      });

      render(
        <BrowserRouter>
          <ManufactureBatchCalculator />
        </BrowserRouter>,
      );

      triggerProductSelect(testProduct);

      await waitFor(() => {
        expect(mockGetBatchTemplate).toHaveBeenCalledWith('SEMI001');
      });

      const batchSizeInput = await screen.findByPlaceholderText('0.00');
      await waitFor(() => {
        expect(batchSizeInput).toHaveValue(null);
      });
      expect(mockCalculateBySize).not.toHaveBeenCalled();
    });
  });
});
```

(The final `});` closes the outer `ManufactureBatchCalculator` describe block, same as before.)

Note: `toHaveValue(null)` is `jest-dom`'s documented way to assert a `<input type="number">` has no value (displayed as an empty string, represented as `null` by the matcher) — the batch-size `<input>` at `ManufactureBatchCalculator.tsx` line 320 is `type="number"`.

#### Step 2 — Run the suite to confirm all three cases pass

```bash
cd frontend
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```

Expected output:

```
PASS src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
Test Suites: 1 passed, 1 total
Tests:       16 passed, 16 total
```

#### Step 3 — Commit

```bash
git add frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
git commit -m "test(manufacture-batch-calculator): cover batch-size fallback precedence (MMQ/BOM/zero)"
```

---

