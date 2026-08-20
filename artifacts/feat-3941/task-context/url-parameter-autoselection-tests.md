### task: url-parameter-autoselection-tests

**Goal of this task:** implement FR-3 — verify the mount-time `useEffect` (`frontend/src/components/pages/ManufactureBatchCalculator.tsx` lines 117–136) that auto-selects a product from `?productCode=X&batchSize=Y` in the URL, and confirm the URL's `batchSize` overrides the template's `newBatchSize` default.

**Prerequisite:** `test-infrastructure-mocks` task must be complete — this task relies on `mockGetBatchTemplate` and `mockCalculateBySize` existing in the file exactly as that task defined them. It does not use `triggerProductSelect` (the URL effect drives selection itself, not the mocked `CatalogAutocomplete` button).

**Files:**
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` (modified)

Reference: `frontend/src/components/pages/ManufactureBatchCalculator.tsx` lines 116–136 — on mount, if `productCode` and `batchSize` are both present in `location.search` and `selectedProduct` is not yet set, the effect constructs `new CatalogItemDto({ productCode, productName: productCode, type: ProductType.SemiProduct })` and passes it through `handleProductSelect`, which (per lines 78–92) prefers the URL's `batchSize` over the template's `newBatchSize`/`originalBatchSize` whenever `parseFloat(urlBatchSize) > 0`.

#### Step 1 — Add the `URL parameter auto-selection` describe block

In `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx`, find the end of the `batch-size fallback precedence` describe block added by the previous task:

```tsx
      const batchSizeInput = await screen.findByPlaceholderText('0.00');
      await waitFor(() => {
        expect(batchSizeInput).toHaveValue(null);
      });
      expect(mockCalculateBySize).not.toHaveBeenCalled();
    });
  });
});
```

Replace it with:

```tsx
      const batchSizeInput = await screen.findByPlaceholderText('0.00');
      await waitFor(() => {
        expect(batchSizeInput).toHaveValue(null);
      });
      expect(mockCalculateBySize).not.toHaveBeenCalled();
    });
  });

  describe('URL parameter auto-selection', () => {
    it('auto-selects the product from ?productCode&batchSize and overrides the template default batch size', async () => {
      mockGetBatchTemplate.mockResolvedValue({
        success: true,
        productCode: 'URLPROD',
        productName: 'URL Product Template',
        originalBatchSize: 800,
        newBatchSize: 1000,
        scaleFactor: 0.5,
        ingredients: [],
      });

      render(
        <MemoryRouter initialEntries={['/manufacturing/batch-calculator?productCode=URLPROD&batchSize=500']}>
          <Routes>
            <Route path="/manufacturing/batch-calculator" element={<ManufactureBatchCalculator />} />
          </Routes>
        </MemoryRouter>,
      );

      await waitFor(() => {
        expect(mockGetBatchTemplate).toHaveBeenCalledWith('URLPROD');
      });
      await waitFor(() => {
        expect(mockCalculateBySize).toHaveBeenCalledWith('URLPROD', 500);
      });

      // URL batchSize (500) wins over the template's newBatchSize (1000)
      expect(screen.getByDisplayValue('500')).toBeInTheDocument();
      expect(screen.queryByDisplayValue('1000')).not.toBeInTheDocument();

      // selectedProduct.productName is seeded from the URL's productCode and is not
      // overwritten once the template resolves — existing, unchanged behavior per
      // spec.r1.md FR-3's fourth acceptance criterion.
      expect(screen.getByTestId('catalog-autocomplete-value')).toHaveTextContent('URLPROD');
    });

    // FR-3's lower-priority case ("if selectedProduct is already set, the effect does
    // not re-trigger auto-selection", guarded by `!selectedProduct` at
    // ManufactureBatchCalculator.tsx line 122): per spec.r1.md, this is documented as
    // not covered rather than tested. Exercising it deterministically would require
    // pausing the async handleProductSelect chain mid-flight to inject a manual
    // selection before the mount-time effect's own call resolves, which is racy with
    // React Testing Library's async utilities.
  });
});
```

(The final `});` closes the outer `ManufactureBatchCalculator` describe block, same as before.)

#### Step 2 — Run the suite to confirm the new test passes

```bash
cd frontend
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```

Expected output:

```
PASS src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
Test Suites: 1 passed, 1 total
Tests:       17 passed, 17 total
```

#### Step 3 — Commit

```bash
git add frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
git commit -m "test(manufacture-batch-calculator): cover URL parameter auto-selection and override"
```

---

