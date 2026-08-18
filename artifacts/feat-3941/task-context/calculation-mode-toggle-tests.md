### task: calculation-mode-toggle-tests

**Goal of this task:** implement FR-4 — verify the `calculationMode` radio toggle (`frontend/src/components/pages/ManufactureBatchCalculator.tsx` lines 280–306, rendered only once `template` is truthy) defaults to batch-size mode, that switching to ingredient mode swaps the rendered input group (unmount, not `disabled`, per the ternary at lines 314–416), and that each mode's "Vypočítat" button invokes the correct calculation function and not the other one.

**Prerequisite:** `test-infrastructure-mocks` task must be complete — this task relies on `mockGetBatchTemplate`, `mockCalculateBySize`, `mockCalculateByIngredient`, and `triggerProductSelect` existing in the file exactly as that task defined them.

**Files:**
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` (modified)

Reference: `frontend/src/components/pages/ManufactureBatchCalculator.tsx`:
- Lines 280–306: the two radio inputs, each wrapped in a `<label>` with the visible text as a sibling `<span>` (no `htmlFor`/`id` — RTL's `getByLabelText` resolves this via the wrapping-label association).
- Lines 314–416: the mode ternary — `"batch-size"` renders the "Požadovaná velikost dávky (g)" number input; `"ingredient"` renders the ingredient `<select>` and the "Požadované množství (g)" number input.
- Lines 138–172: `handleCalculateBySize` calls `calculateBySize(selectedProduct.productCode, parseFloat(desiredBatchSize))`; `handleCalculateByIngredient` calls `calculateByIngredient(selectedProduct.productCode, selectedIngredientCode, parseFloat(desiredIngredientAmount))`.

#### Step 1 — Add the `calculation-mode toggle` describe block

In `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx`, find the end of the `URL parameter auto-selection` describe block added by the previous task:

```tsx
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

Replace it with:

```tsx
    // FR-3's lower-priority case ("if selectedProduct is already set, the effect does
    // not re-trigger auto-selection", guarded by `!selectedProduct` at
    // ManufactureBatchCalculator.tsx line 122): per spec.r1.md, this is documented as
    // not covered rather than tested. Exercising it deterministically would require
    // pausing the async handleProductSelect chain mid-flight to inject a manual
    // selection before the mount-time effect's own call resolves, which is racy with
    // React Testing Library's async utilities.
  });

  describe('calculation-mode toggle', () => {
    const templateWithIngredient = {
      success: true,
      productCode: 'SEMI001',
      productName: 'Test Semi Product',
      originalBatchSize: 200,
      newBatchSize: 100,
      scaleFactor: 0.5,
      ingredients: [
        {
          productCode: 'ING001',
          productName: 'Ingredient One',
          originalAmount: 50,
          calculatedAmount: 25,
          stockTotal: 100,
        },
      ],
    };

    const renderWithSelectedProduct = async () => {
      mockGetBatchTemplate.mockResolvedValue(templateWithIngredient);

      render(
        <BrowserRouter>
          <ManufactureBatchCalculator />
        </BrowserRouter>,
      );

      triggerProductSelect(testProduct);

      await screen.findByLabelText('Podle velikosti dávky');
    };

    it('defaults to batch-size mode once a template loads', async () => {
      await renderWithSelectedProduct();

      expect(screen.getByLabelText('Podle velikosti dávky')).toBeChecked();
      expect(screen.getByText('Požadovaná velikost dávky (g)')).toBeInTheDocument();
      expect(screen.queryByText('Požadované množství (g)')).not.toBeInTheDocument();
      expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    });

    it('switches to ingredient mode when "Podle ingredience" is clicked', async () => {
      await renderWithSelectedProduct();

      fireEvent.click(screen.getByLabelText('Podle ingredience'));

      expect(screen.queryByText('Požadovaná velikost dávky (g)')).not.toBeInTheDocument();
      expect(screen.getByText('Požadované množství (g)')).toBeInTheDocument();
      expect(screen.getByRole('combobox')).toBeInTheDocument();
    });

    it('invokes calculateBySize (not calculateByIngredient) when computing in batch-size mode', async () => {
      await renderWithSelectedProduct();
      mockCalculateBySize.mockClear();

      const batchSizeInput = screen.getByPlaceholderText('0.00');
      fireEvent.change(batchSizeInput, { target: { value: '150' } });
      fireEvent.click(screen.getByRole('button', { name: /Vypočítat/i }));

      await waitFor(() => {
        expect(mockCalculateBySize).toHaveBeenCalledWith('SEMI001', 150);
      });
      expect(mockCalculateByIngredient).not.toHaveBeenCalled();
    });

    it('invokes calculateByIngredient (not calculateBySize) when computing in ingredient mode', async () => {
      await renderWithSelectedProduct();
      mockCalculateBySize.mockClear();

      fireEvent.click(screen.getByLabelText('Podle ingredience'));
      fireEvent.change(screen.getByRole('combobox'), { target: { value: 'ING001' } });
      fireEvent.change(screen.getByPlaceholderText('0.00'), { target: { value: '30' } });
      fireEvent.click(screen.getByRole('button', { name: /Vypočítat/i }));

      await waitFor(() => {
        expect(mockCalculateByIngredient).toHaveBeenCalledWith('SEMI001', 'ING001', 30);
      });
      expect(mockCalculateBySize).not.toHaveBeenCalled();
    });
  });
});
```

(The final `});` closes the outer `ManufactureBatchCalculator` describe block, same as before.)

Notes:
- `mockCalculateBySize.mockClear()` in the third and fourth tests discards the call recorded by `renderWithSelectedProduct`'s own auto-calculate-on-select (since `templateWithIngredient.newBatchSize = 100 > 0`), isolating the assertion to the explicit "Vypočítat" click.
- The ingredient-mode "Vypočítat" button is `disabled` until both `selectedIngredientCode` and `desiredIngredientAmount` are non-empty (`ManufactureBatchCalculator.tsx` lines 397–401); a `disabled` button does not dispatch a `click` event in jsdom, so the `fireEvent.change` calls must happen before the `fireEvent.click`, in the order shown.
- A native `<select>` has an implicit ARIA role of `combobox`, which is why `getByRole('combobox')` locates the ingredient dropdown without needing a `data-testid`.

#### Step 2 — Run the full suite to confirm all four cases pass

```bash
cd frontend
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```

Expected output:

```
PASS src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
Test Suites: 1 passed, 1 total
Tests:       21 passed, 21 total
```

#### Step 3 — Verify formatting and lint are clean, then commit

```bash
cd frontend
npm run lint
```

Expect no new errors attributable to `ManufactureBatchCalculator.test.tsx`.

```bash
git add frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
git commit -m "test(manufacture-batch-calculator): cover calculation-mode toggle rendering and routing"
```

#### Step 4 — Confirm the full frontend build and lint still pass

```bash
cd frontend
npm run build
npm run lint
```

Both must complete without errors before this feature branch is considered done, per this repository's validation checklist (`CLAUDE.md` → Validation before completion).
