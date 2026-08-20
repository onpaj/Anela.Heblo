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

