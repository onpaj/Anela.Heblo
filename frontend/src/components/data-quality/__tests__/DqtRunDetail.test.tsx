import React from 'react';
import { render, screen } from '@testing-library/react';
import DqtRunDetail from '../DqtRunDetail';
import { useDqtRunDetail } from '../../../api/hooks/useDataQuality';

jest.mock('../../../api/hooks/useDataQuality', () => ({
  useDqtRunDetail: jest.fn(),
}));

const mockUseDqtRunDetail = useDqtRunDetail as jest.Mock;

const priceComparisonRun = {
  id: 'run-1',
  testType: 'PriceComparison',
  status: 'Completed',
};

test('renders drift rows for a PriceComparison run instead of the no-mismatches message', () => {
  // Arrange
  mockUseDqtRunDetail.mockReturnValue({
    data: {
      run: priceComparisonRun,
      results: [],
      driftResults: [
        {
          entityKey: 'SKU-001',
          mismatchCode: 1,
          testType: 'PriceComparison',
          hebloValue: '99.00',
          shoptetValue: '109.00',
          details: 'FlexiDiffers',
        },
      ],
    },
    isLoading: false,
    error: null,
  });

  // Act
  render(<DqtRunDetail runId="run-1" />);

  // Assert — the drift table is shown, not the "no mismatches" placeholder
  expect(screen.queryByText('Žádné neshody nalezeny pro tento test.')).not.toBeInTheDocument();
  expect(screen.getByText('SKU-001')).toBeInTheDocument();
  expect(screen.getByText('Rozdílná cena')).toBeInTheDocument();
  expect(screen.getByText('109.00')).toBeInTheDocument();
  expect(screen.getByText('99.00')).toBeInTheDocument();
});

test('renders Shoptet and Flexi column headers in that order for a PriceComparison run', () => {
  // Arrange
  mockUseDqtRunDetail.mockReturnValue({
    data: {
      run: priceComparisonRun,
      results: [],
      driftResults: [
        {
          entityKey: 'SKU-002',
          mismatchCode: 2,
          testType: 'PriceComparison',
          hebloValue: undefined,
          shoptetValue: '50.00',
          details: 'MissingInFlexi',
        },
      ],
    },
    isLoading: false,
    error: null,
  });

  // Act
  render(<DqtRunDetail runId="run-1" />);
  const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);

  // Assert — the generic hebloValue/shoptetValue columns are correctly labelled Flexi/Shoptet
  // for this check (not "Heblo"), each header lining up with the column it actually holds:
  // hebloValue (Flexi price) comes before shoptetValue (Shoptet price) in column order.
  expect(headers).not.toContain('Heblo');
  const shoptetIndex = headers.indexOf('Shoptet');
  const flexiIndex = headers.indexOf('Flexi');
  expect(shoptetIndex).toBeGreaterThan(-1);
  expect(flexiIndex).toBeGreaterThan(-1);
  expect(flexiIndex).toBeLessThan(shoptetIndex);
});

test('shows a mismatch-specific label for each PriceComparisonMismatch value', () => {
  // Arrange
  mockUseDqtRunDetail.mockReturnValue({
    data: {
      run: priceComparisonRun,
      results: [],
      driftResults: [
        { entityKey: 'SKU-A', mismatchCode: 1, testType: 'PriceComparison', hebloValue: '1', shoptetValue: '2', details: 'FlexiDiffers' },
        { entityKey: 'SKU-B', mismatchCode: 2, testType: 'PriceComparison', hebloValue: undefined, shoptetValue: '2', details: 'MissingInFlexi' },
        { entityKey: 'SKU-C', mismatchCode: 3, testType: 'PriceComparison', hebloValue: '1', shoptetValue: '2', details: 'FlexiPriceTypeUnknown' },
      ],
    },
    isLoading: false,
    error: null,
  });

  // Act
  render(<DqtRunDetail runId="run-1" />);

  // Assert — mismatchCode is a plain enum value here (not a bitmask), so code 3 must not
  // be decoded as the union of codes 1 and 2.
  expect(screen.getByText('Rozdílná cena')).toBeInTheDocument();
  expect(screen.getByText('Chybí ve Flexi')).toBeInTheDocument();
  expect(screen.getByText('Neznámý typ ceny ve Flexi')).toBeInTheDocument();
});

test('still renders the no-mismatches message for a PriceComparison run with no drift results', () => {
  // Arrange
  mockUseDqtRunDetail.mockReturnValue({
    data: { run: priceComparisonRun, results: [], driftResults: [] },
    isLoading: false,
    error: null,
  });

  // Act
  render(<DqtRunDetail runId="run-1" />);

  // Assert
  expect(screen.getByText('Žádné neshody nalezeny pro tento test.')).toBeInTheDocument();
});
