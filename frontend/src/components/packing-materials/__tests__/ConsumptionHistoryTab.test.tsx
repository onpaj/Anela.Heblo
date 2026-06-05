import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ConsumptionHistoryTab from '../ConsumptionHistoryTab';
import * as hooks from '../../../api/hooks/usePackingMaterials';

jest.mock('../../../api/hooks/usePackingMaterials', () => {
  const actual = jest.requireActual('../../../api/hooks/usePackingMaterials');
  return {
    ...actual,
    usePackingMaterials: jest.fn(),
    useConsumptionHistory: jest.fn(),
  };
});

const mockUsePackingMaterials = hooks.usePackingMaterials as jest.Mock;
const mockUseConsumptionHistory = hooks.useConsumptionHistory as jest.Mock;

const sampleResponse = {
  items: [
    {
      recordType: 1,
      recordTypeText: 'Spotřeba',
      packingMaterialId: 1,
      materialName: 'Tape',
      date: '2026-01-10',
      createdAt: '2026-01-10T08:00:00Z',
      consumptionType: 1,
      consumptionTypeText: 'PerOrder',
      invoiceId: 'INV-1',
      productCode: 'P1',
      amount: 5,
    },
    {
      recordType: 2,
      recordTypeText: 'Změna množství',
      packingMaterialId: 1,
      materialName: 'Tape',
      date: '2026-01-12',
      createdAt: '2026-01-12T08:00:00Z',
      oldQuantity: 100,
      newQuantity: 90,
      changeAmount: -10,
      logTypeText: 'Manual',
    },
  ],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
  totalPages: 1,
};

describe('ConsumptionHistoryTab', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUsePackingMaterials.mockReturnValue({ data: { materials: [{ id: 1, name: 'Tape' }] } });
    mockUseConsumptionHistory.mockReturnValue({ data: sampleResponse, isLoading: false, error: null, refetch: jest.fn() });
  });

  it('renders union rows for both record types', () => {
    render(<ConsumptionHistoryTab />);
    // 'Spotřeba' appears both as a column header and as a row's recordTypeText.
    expect(screen.getAllByText('Spotřeba').length).toBeGreaterThan(0);
    expect(screen.getByText('Změna množství')).toBeInTheDocument();
    expect(screen.getByText('INV-1')).toBeInTheDocument();
    expect(screen.getAllByText('Tape').length).toBeGreaterThan(0);
  });

  it('shows an empty state when there are no records', () => {
    mockUseConsumptionHistory.mockReturnValue({
      data: { items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    });
    render(<ConsumptionHistoryTab />);
    expect(screen.getByText('Žádné záznamy historie spotřeby.')).toBeInTheDocument();
  });

  it('applies filters and requests page 1', () => {
    render(<ConsumptionHistoryTab />);
    fireEvent.change(screen.getByPlaceholderText('ID faktury'), { target: { value: 'INV-1' } });
    fireEvent.click(screen.getByText('Použít filtry'));
    expect(mockUseConsumptionHistory).toHaveBeenLastCalledWith(
      expect.objectContaining({ invoiceId: 'INV-1', pageNumber: 1 }),
    );
  });

  it('toggles date sort direction', () => {
    render(<ConsumptionHistoryTab />);
    fireEvent.click(screen.getByText('Datum'));
    expect(mockUseConsumptionHistory).toHaveBeenLastCalledWith(
      expect.objectContaining({ sortDescending: false }),
    );
  });
});
