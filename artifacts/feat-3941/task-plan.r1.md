# Implementation Plan: ManufactureBatchCalculator test coverage for computePercentage edge cases and batch-size fallback

## Goal

Close the test-coverage gap on `frontend/src/components/pages/ManufactureBatchCalculator.tsx` (currently 29.3% line coverage against a 60% threshold) by adding the four groups of missing tests identified in `spec.r1.md`:

- FR-1: `computePercentage` non-finite (`Infinity` / `-Infinity`) edge cases.
- FR-2: the `templateData.newBatchSize || templateData.originalBatchSize || 0` batch-size fallback precedence.
- FR-3: the `?productCode=X&batchSize=Y` URL-parameter auto-selection flow, including the URL-value-overrides-template-default behavior.
- FR-4: the batch-size ↔ ingredient calculation-mode toggle and which calculation function each mode invokes.

This is a **test-only** change. No production code in `ManufactureBatchCalculator.tsx` or `useManufactureBatch.ts` is modified. The one production-adjacent change is extending the test-file-local Jest mock of `CatalogAutocomplete` (not the real component) so tests can trigger `onSelect` — this is explicitly in scope per spec.r1.md's NFR-3 and Out of Scope section.

## Architecture

All work lands in one file, extended in place:

```
frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
```

No new files are created. The file's existing two `jest.mock` factories for `useManufactureBatch` and `CatalogAutocomplete` are restructured (Task 1) to be per-test configurable; every subsequent task only adds new `it`/`describe` blocks using that restructured infrastructure. The `InventoryStatusCell`, `ManufactureInventoryDetail`, and `CatalogDetail` mocks are untouched throughout.

Data flow exercised by the new tests mirrors production exactly: the mocked `CatalogAutocomplete`'s `onSelect` (or the component's own mount-time `useEffect` reading `location.search`) invokes the real, non-exported `handleProductSelect` callback inside `ManufactureBatchCalculator`, which calls the mocked `getBatchTemplate` and (conditionally) the mocked `calculateBySize`/`calculateByIngredient`, updating component state that is then asserted against the rendered DOM.

## Tech Stack

- Jest + React Testing Library (`@testing-library/react`, `@testing-library/jest-dom`) — already configured via `react-scripts test`.
- `react-router-dom` v6.30.4 — `BrowserRouter` (already used by the existing smoke test) and `MemoryRouter`/`Routes`/`Route` (new for FR-3, precedent in `frontend/src/components/terminal/lot-identification/__tests__/PoLinePickStep.test.tsx`).
- No new npm packages.
- **One-time setup note:** if `frontend/node_modules` is not yet installed in your environment, run `npm install` inside `frontend/` first (see `docs/development/setup.md`) — none of the steps below will run otherwise.

All test-run commands below are run from the `frontend/` directory and use:

```bash
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```

`CI=true` makes `react-scripts test` (Jest under the hood) run once and exit instead of entering watch mode; the trailing filename argument restricts the run to this file via Jest's `testPathPattern`.

## Spec coverage map

| Spec requirement | Task |
|---|---|
| FR-1 (`Infinity`/`-Infinity` → `'N/A'`) | `computepercentage-infinity-tests` |
| FR-2 (MMQ → BOM → zero fallback, 3 cases) | `batch-size-fallback-precedence-tests` |
| FR-3 (URL param auto-selection + override) | `url-parameter-autoselection-tests` |
| FR-4 (calculation-mode toggle, 4 cases) | `calculation-mode-toggle-tests` |
| NFR-3 (mock restructuring, `CatalogAutocomplete` `onSelect` mock) | `test-infrastructure-mocks` (prerequisite for all of the above) |

---

### task: test-infrastructure-mocks

**Goal of this task:** restructure the two existing `jest.mock` factories in `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` so later tasks can configure `getBatchTemplate` / `calculateBySize` / `calculateByIngredient` per test, and so tests can trigger a manual product selection through `CatalogAutocomplete`. No new test cases are added in this task — the existing smoke test must still pass unchanged afterward, proving the restructuring is behavior-preserving.

**Files:**
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` (modified)

Read-only references used to write this task (no changes to these):
- `frontend/src/components/pages/ManufactureBatchCalculator.tsx` — the component under test; `handleProductSelect` (lines 65–114) is the callback both `CatalogAutocomplete.onSelect` and the URL `useEffect` (lines 117–136) funnel into.
- `frontend/src/api/hooks/useManufactureBatch.ts` — confirms the mocked hook's shape: `{ getBatchTemplate, calculateBySize, calculateByIngredient, isLoading, error }`. The component never destructures `error`, so the mock may omit it (matching current test-file behavior).
- `frontend/src/components/common/CatalogAutocomplete.tsx` — confirms the real `onSelect: (item: T | null) => void` and `value?: T | null` prop names the mock must accept.

#### Step 1 — Restructure the mocks and add shared test fixtures

Open `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx`. Find this block (the current file header, lines 1–43):

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { BrowserRouter } from 'react-router-dom';
import ManufactureBatchCalculator, { computePercentage } from '../ManufactureBatchCalculator';

// Mock the API hook
jest.mock('../../../api/hooks/useManufactureBatch', () => ({
  useManufactureBatch: () => ({
    getBatchTemplate: jest.fn().mockResolvedValue({ success: false }),
    calculateBySize: jest.fn().mockResolvedValue({ success: false }),
    calculateByIngredient: jest.fn().mockResolvedValue({ success: false }),
    isLoading: false,
  }),
}));

// Mock CatalogAutocomplete component
jest.mock('../../common/CatalogAutocomplete', () => {
  return function MockCatalogAutocomplete() {
    return <div data-testid="catalog-autocomplete" />;
  };
});

// Mock InventoryStatusCell component
jest.mock('../../inventory/InventoryStatusCell', () => {
  return function MockInventoryStatusCell() {
    return <div />;
  };
});

// Mock ManufactureInventoryDetail component
jest.mock('../../inventory/ManufactureInventoryDetail', () => {
  return function MockManufactureInventoryDetail() {
    return null;
  };
});

// Mock CatalogDetail component
jest.mock('../CatalogDetail', () => {
  return function MockCatalogDetail() {
    return null;
  };
});
```

Replace it with:

```tsx
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { BrowserRouter, MemoryRouter, Routes, Route } from 'react-router-dom';
import ManufactureBatchCalculator, { computePercentage } from '../ManufactureBatchCalculator';
import { CatalogItemDto, ProductType } from '../../../api/generated/api-client';

// Mock the API hook — module-scoped jest.fn() refs so each test can configure
// resolved values independently, without leaking state between tests.
const mockGetBatchTemplate = jest.fn();
const mockCalculateBySize = jest.fn();
const mockCalculateByIngredient = jest.fn();

jest.mock('../../../api/hooks/useManufactureBatch', () => ({
  useManufactureBatch: () => ({
    getBatchTemplate: mockGetBatchTemplate,
    calculateBySize: mockCalculateBySize,
    calculateByIngredient: mockCalculateByIngredient,
    isLoading: false,
  }),
}));

// Mock CatalogAutocomplete component — renders the seeded `value.productName` (so
// tests can assert what the calculator passes as `value`) and exposes a button that
// invokes the real `onSelect` prop with a test-configured product, so tests can
// simulate a manual product selection without reproducing the real search UI.
let mockAutocompleteProduct: CatalogItemDto | null = null;
jest.mock('../../common/CatalogAutocomplete', () => {
  return function MockCatalogAutocomplete(props: {
    value?: { productName?: string } | null;
    onSelect: (item: any) => void;
  }) {
    return (
      <div data-testid="catalog-autocomplete">
        {props.value?.productName && (
          <span data-testid="catalog-autocomplete-value">{props.value.productName}</span>
        )}
        <button
          data-testid="catalog-autocomplete-select"
          onClick={() => props.onSelect(mockAutocompleteProduct)}
        >
          select
        </button>
      </div>
    );
  };
});

// Mock InventoryStatusCell component
jest.mock('../../inventory/InventoryStatusCell', () => {
  return function MockInventoryStatusCell() {
    return <div />;
  };
});

// Mock ManufactureInventoryDetail component
jest.mock('../../inventory/ManufactureInventoryDetail', () => {
  return function MockManufactureInventoryDetail() {
    return null;
  };
});

// Mock CatalogDetail component
jest.mock('../CatalogDetail', () => {
  return function MockCatalogDetail() {
    return null;
  };
});

beforeEach(() => {
  mockGetBatchTemplate.mockReset().mockResolvedValue({ success: false });
  mockCalculateBySize.mockReset().mockResolvedValue({ success: false });
  mockCalculateByIngredient.mockReset().mockResolvedValue({ success: false });
  mockAutocompleteProduct = null;
});

// Selects a product through the mocked CatalogAutocomplete's onSelect callback,
// exactly as a manual pick through the real component would.
const triggerProductSelect = (product: CatalogItemDto) => {
  mockAutocompleteProduct = product;
  fireEvent.click(screen.getByTestId('catalog-autocomplete-select'));
};

const testProduct = new CatalogItemDto({
  productCode: 'SEMI001',
  productName: 'Test Semi Product',
  type: ProductType.SemiProduct,
});
```

Notes on this exact ordering (imports, then `const mockX = jest.fn()`, then `jest.mock(...)` referencing them): this is the established pattern already used throughout this codebase (e.g. `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` lines 1–16) — Jest's `babel-plugin-jest-hoist` only allows a `jest.mock()` factory to reference an out-of-scope variable when its name is prefixed with `mock`, which is why `mockGetBatchTemplate`, `mockCalculateBySize`, `mockCalculateByIngredient`, and `mockAutocompleteProduct` all keep that prefix — do not rename them.

Leave the rest of the file (`describe('computePercentage helper', ...)` and `describe('ManufactureBatchCalculator', ...)` and their contents) untouched in this task.

#### Step 2 — Run the suite to confirm the restructuring is behavior-preserving

```bash
cd frontend
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```

Expected output (unchanged from before this task — same 10 `computePercentage` cases plus the 1 smoke test, all still passing):

```
PASS src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
Test Suites: 1 passed, 1 total
Tests:       11 passed, 11 total
```

If instead you see `ReferenceError: Cannot access 'mockGetBatchTemplate' before initialization`, the `const mockGetBatchTemplate = jest.fn();` declarations were placed after the `jest.mock(...)` call that references them — reorder so all three `const mock... = jest.fn();` lines appear before their `jest.mock(...)` call, exactly as shown in Step 1.

#### Step 3 — Commit

```bash
git add frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx
git commit -m "test(manufacture-batch-calculator): restructure mocks for per-test configuration"
```

---

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
