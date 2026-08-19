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

describe('computePercentage helper', () => {
  it('returns formatted percentage for normal values', () => {
    expect(computePercentage(184.5, 1000)).toBe('18.45%');
  });

  it('returns "100.00%" when calculatedAmount equals newBatchSize', () => {
    expect(computePercentage(500, 500)).toBe('100.00%');
  });

  it('returns "N/A" when newBatchSize is 0', () => {
    expect(computePercentage(100, 0)).toBe('N/A');
  });

  it('returns "N/A" when newBatchSize is null', () => {
    expect(computePercentage(100, null)).toBe('N/A');
  });

  it('returns "N/A" when newBatchSize is undefined', () => {
    expect(computePercentage(100, undefined)).toBe('N/A');
  });

  it('returns "0.00%" when calculatedAmount is 0', () => {
    expect(computePercentage(0, 1000)).toBe('0.00%');
  });

  it('rounds to exactly 2 decimal places', () => {
    // 1/3 * 100 = 33.333... → "33.33%"
    expect(computePercentage(1, 3)).toBe('33.33%');
  });

  it('returns N/A when newBatchSize is NaN', () => {
    expect(computePercentage(100, NaN)).toBe('N/A');
  });

  it('returns N/A when newBatchSize is negative', () => {
    expect(computePercentage(100, -500)).toBe('N/A');
  });

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

describe('ManufactureBatchCalculator', () => {
  it('renders without crashing and shows no percentage column header in empty state', () => {
    render(
      <BrowserRouter>
        <ManufactureBatchCalculator />
      </BrowserRouter>,
    );

    // The component renders the page title
    expect(screen.getByText('Kalkulačka dávek pro výrobu')).toBeInTheDocument();

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
