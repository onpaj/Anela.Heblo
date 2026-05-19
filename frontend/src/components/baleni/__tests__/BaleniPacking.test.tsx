import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import BaleniPacking from '../BaleniPacking';
import { usePackingOrder, PackingOrderNotFoundError } from '../../../api/hooks/usePackingOrder';

jest.mock('../../../api/hooks/usePackingOrder', () => ({
  ...jest.requireActual('../../../api/hooks/usePackingOrder'),
  usePackingOrder: jest.fn(),
}));

const mockHook = usePackingOrder as jest.Mock;

const baseResult = {
  data: undefined,
  isLoading: false,
  isError: false,
  error: null,
};

beforeEach(() => {
  jest.useFakeTimers();
  mockHook.mockReset();
  mockHook.mockReturnValue(baseResult);
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

describe('BaleniPacking', () => {
  it('shows the empty state before any scan', () => {
    render(<BaleniPacking />);
    expect(screen.getByText('Naskenujte číslo objednávky')).toBeInTheDocument();
  });

  it('renders the order panel when data is loaded', () => {
    mockHook.mockReturnValue({
      ...baseResult,
      data: {
        code: '250001',
        customerName: 'Jan Novák',
        shippingMethodName: 'PPL (do ruky)',
        cooling: 'None',
        isCooled: false,
        items: [{ name: 'Krém', quantity: 2, imageUrl: null, setName: null }],
      },
    });

    render(<BaleniPacking />);
    expect(screen.getByText('Objednávka 250001')).toBeInTheDocument();
    expect(screen.getByText('Krém')).toBeInTheDocument();
  });

  it('shows a not-found message for an unknown order', () => {
    mockHook.mockReturnValue({
      ...baseResult,
      isError: true,
      error: new PackingOrderNotFoundError('999999'),
    });

    render(<BaleniPacking />);
    expect(screen.getByText('Objednávka nenalezena')).toBeInTheDocument();
  });

  it('shows a generic error message for other failures', () => {
    mockHook.mockReturnValue({
      ...baseResult,
      isError: true,
      error: new Error('network down'),
    });

    render(<BaleniPacking />);
    expect(screen.getByText('Nepodařilo se načíst objednávku')).toBeInTheDocument();
  });

  it('updates the scanned code when the scan input submits', () => {
    render(<BaleniPacking />);
    const input = screen.getByRole('textbox') as HTMLInputElement;
    fireEvent.change(input, { target: { value: '250001' } });
    fireEvent.submit(input.closest('form')!);
    expect(mockHook).toHaveBeenLastCalledWith('250001');
  });
});
