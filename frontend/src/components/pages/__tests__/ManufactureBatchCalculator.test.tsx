import React from 'react';
import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { BrowserRouter } from 'react-router-dom';
import ManufactureBatchCalculator from '../ManufactureBatchCalculator';

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

// The exact helper logic that will be used in the component's render function.
// Defined here to test the pure logic before the component is modified.
const computePercentage = (
  calculatedAmount: number,
  newBatchSize: number | null | undefined,
): string => {
  if (!newBatchSize || newBatchSize <= 0) return 'N/A';
  return ((calculatedAmount / newBatchSize) * 100).toFixed(2) + '%';
};

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
    const percentageHeaders = screen
      .queryAllByRole('columnheader')
      .filter((el) => el.textContent?.includes('%'));
    expect(percentageHeaders).toHaveLength(0);
  });
});
