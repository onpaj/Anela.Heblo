import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ManufactureInventoryList from '../ManufactureInventoryList';

jest.mock('../../../api/hooks/useManufactureInventory', () => ({
  useManufactureInventoryQuery: jest.fn(),
}));

jest.mock('../../../api/hooks/useCatalog', () => ({
  ProductType: {
    UNDEFINED: 'UNDEFINED',
    Goods: 'Goods',
    Material: 'Material',
    SemiProduct: 'SemiProduct',
    Product: 'Product',
    Set: 'Set',
  },
}));

jest.mock('../CatalogDetail', () => () => null);
jest.mock('../../inventory/ManufactureInventoryDetail', () => () => null);
jest.mock('../../../telemetry/useScreenView', () => ({
  useScreenView: () => undefined,
}));

const { useManufactureInventoryQuery } = require('../../../api/hooks/useManufactureInventory');
const mockQuery = useManufactureInventoryQuery as jest.Mock;

// Index of the hasExpiration argument in useManufactureInventoryQuery(...)
const HAS_EXPIRATION_ARG = 9;

const renderAt = (url: string) =>
  render(
    <MemoryRouter initialEntries={[url]}>
      <ManufactureInventoryList />
    </MemoryRouter>,
  );

const lastHasExpirationArg = () =>
  mockQuery.mock.calls[mockQuery.mock.calls.length - 1][HAS_EXPIRATION_ARG];

describe('ManufactureInventoryList material kind filter', () => {
  beforeEach(() => {
    mockQuery.mockReturnValue({
      data: { items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    });
  });

  it('applies hasExpiration=false from the URL (packaging and labels tile drill-down)', () => {
    renderAt('/manufacturing/inventory?type=Material&hasExpiration=false');

    expect(lastHasExpirationArg()).toBe(false);
    expect(screen.getByLabelText('Druh materiálu')).toHaveValue('false');
  });

  it('never queries unfiltered on a drill-down (no wasted full-list request)', () => {
    renderAt('/manufacturing/inventory?type=Material&hasExpiration=false');

    const hasExpirationArgs = mockQuery.mock.calls.map((call) => call[HAS_EXPIRATION_ARG]);
    expect(hasExpirationArgs).not.toContain(undefined);
  });

  it('applies hasExpiration=true from the URL (raw materials tile drill-down)', () => {
    renderAt('/manufacturing/inventory?type=Material&hasExpiration=true');

    expect(lastHasExpirationArg()).toBe(true);
    expect(screen.getByLabelText('Druh materiálu')).toHaveValue('true');
  });

  it('does not filter by expiration when the URL has no hasExpiration param', () => {
    renderAt('/manufacturing/inventory');

    expect(lastHasExpirationArg()).toBeUndefined();
    expect(screen.getByLabelText('Druh materiálu')).toHaveValue('');
  });

  it('clears the material kind filter on "Vymazat"', () => {
    renderAt('/manufacturing/inventory?hasExpiration=false');

    fireEvent.click(screen.getByRole('button', { name: 'Vymazat' }));

    expect(lastHasExpirationArg()).toBeUndefined();
    expect(screen.getByLabelText('Druh materiálu')).toHaveValue('');
  });
});
