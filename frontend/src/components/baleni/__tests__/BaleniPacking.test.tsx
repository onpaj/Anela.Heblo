import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import BaleniPacking from '../BaleniPacking';
import { useScanPackingOrder } from '../../../api/hooks/useScanPackingOrder';

jest.mock('../../../api/hooks/useScanPackingOrder', () => ({
  ...jest.requireActual('../../../api/hooks/useScanPackingOrder'),
  useScanPackingOrder: jest.fn(),
}));

jest.mock('../PackingShipmentCreator', () => ({
  __esModule: true,
  default: ({ orderCode }: { orderCode: string }) => (
    <div data-testid="packing-shipment-creator" data-order-code={orderCode} />
  ),
}));

const mockHook = useScanPackingOrder as jest.Mock;

const idleMutation = {
  mutate: jest.fn(),
  isPending: false,
  isSuccess: false,
  isError: false,
  data: undefined,
  error: null,
};

beforeEach(() => {
  jest.useFakeTimers();
  mockHook.mockReset();
  mockHook.mockReturnValue({ ...idleMutation });
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
      ...idleMutation,
      isSuccess: true,
      data: {
        order: {
          code: '250001',
          customerName: 'Jan Novák',
          shippingMethodName: 'PPL (do ruky)',
          cooling: 'None',
          isCooled: false,
          customerNote: null,
          eshopNote: null,
          eligibility: { isEligible: true, warningTitle: null, warningBody: null },
          items: [{ name: 'Krém', quantity: 2, imageUrl: null, setName: null }],
        },
        shipment: null,
      },
    });

    render(<BaleniPacking />);
    expect(screen.getByText('Objednávka 250001')).toBeInTheDocument();
    expect(screen.getByText('Krém')).toBeInTheDocument();
  });

  it('renders customer and internal notes when present', () => {
    mockHook.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        order: {
          code: '250001',
          customerName: 'Jan Novák',
          shippingMethodName: 'PPL (do ruky)',
          cooling: 'L2',
          isCooled: true,
          customerNote: 'Zabalit jako dárek',
          eshopNote: 'Stálý zákazník',
          eligibility: { isEligible: true, warningTitle: null, warningBody: null },
          items: [{ name: 'Krém', quantity: 2, imageUrl: null, setName: null }],
        },
        shipment: null,
      },
    });

    render(<BaleniPacking />);
    expect(screen.getByText('Zabalit jako dárek')).toBeInTheDocument();
    expect(screen.getByText('Stálý zákazník')).toBeInTheDocument();
    expect(screen.getByText('Chlazení L2')).toBeInTheDocument();
  });

  it('shows a danger warning when the order is not eligible', () => {
    mockHook.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        order: {
          code: '250001',
          customerName: 'Jan Novák',
          shippingMethodName: 'PPL (do ruky)',
          cooling: 'None',
          isCooled: false,
          customerNote: null,
          eshopNote: null,
          eligibility: {
            isEligible: false,
            warningTitle: 'Nelze balit',
            warningBody: 'Objednávka není ve stavu balení.',
          },
          items: [{ name: 'Krém', quantity: 2, imageUrl: null, setName: null }],
        },
        shipment: null,
      },
    });

    render(<BaleniPacking />);
    expect(screen.getByTestId('packing-state-warning')).toBeInTheDocument();
  });

  it('does not show the danger warning when the order is eligible', () => {
    mockHook.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        order: {
          code: '250001',
          customerName: 'Jan Novák',
          shippingMethodName: 'PPL (do ruky)',
          cooling: 'None',
          isCooled: false,
          customerNote: null,
          eshopNote: null,
          eligibility: { isEligible: true, warningTitle: null, warningBody: null },
          items: [{ name: 'Krém', quantity: 2, imageUrl: null, setName: null }],
        },
        shipment: null,
      },
    });

    render(<BaleniPacking />);
    expect(screen.queryByTestId('packing-state-warning')).not.toBeInTheDocument();
  });

  it('shows a not-found message for an unknown order', () => {
    mockHook.mockReturnValue({
      ...idleMutation,
      isError: true,
      error: new Error('Objednávka nebyla nalezena.'),
    });

    render(<BaleniPacking />);
    expect(screen.getByText('Objednávka nenalezena')).toBeInTheDocument();
  });

  it('shows a generic error message for other failures', () => {
    mockHook.mockReturnValue({
      ...idleMutation,
      isError: true,
      error: new Error('network down'),
    });

    render(<BaleniPacking />);
    expect(screen.getByText('Nepodařilo se načíst objednávku')).toBeInTheDocument();
  });

  it('calls mutate when the scan input submits', () => {
    const mutate = jest.fn();
    mockHook.mockReturnValue({ ...idleMutation, mutate });

    render(<BaleniPacking />);
    const input = screen.getByRole('textbox') as HTMLInputElement;
    fireEvent.change(input, { target: { value: '250001' } });
    fireEvent.submit(input.closest('form')!);
    expect(mutate).toHaveBeenLastCalledWith('250001');
  });

  it('calls mutate again when the same code is scanned twice', () => {
    const mutate = jest.fn();
    mockHook.mockReturnValue({ ...idleMutation, mutate });

    render(<BaleniPacking />);
    const input = screen.getByRole('textbox') as HTMLInputElement;

    fireEvent.change(input, { target: { value: '250001' } });
    fireEvent.submit(input.closest('form')!);
    fireEvent.change(input, { target: { value: '250001' } });
    fireEvent.submit(input.closest('form')!);

    expect(mutate).toHaveBeenCalledTimes(2);
  });

  it('mounts PackingShipmentCreator when order is eligible', () => {
    mockHook.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        order: {
          code: '250001',
          customerName: 'Jan Novák',
          shippingMethodName: 'PPL',
          cooling: 'None',
          isCooled: false,
          customerNote: null,
          eshopNote: null,
          eligibility: { isEligible: true, warningTitle: null, warningBody: null },
          items: [],
        },
        shipment: null,
      },
    });

    render(<BaleniPacking />);
    expect(screen.getByTestId('packing-shipment-creator')).toBeInTheDocument();
    expect(screen.getByTestId('packing-shipment-creator')).toHaveAttribute(
      'data-order-code',
      '250001'
    );
  });

  it('does not mount PackingShipmentCreator when order is not eligible', () => {
    mockHook.mockReturnValue({
      ...idleMutation,
      isSuccess: true,
      data: {
        order: {
          code: '250001',
          customerName: 'Jan Novák',
          shippingMethodName: 'PPL',
          cooling: 'None',
          isCooled: false,
          customerNote: null,
          eshopNote: null,
          eligibility: {
            isEligible: false,
            warningTitle: 'Nelze balit',
            warningBody: null,
          },
          items: [],
        },
        shipment: null,
      },
    });

    render(<BaleniPacking />);
    expect(screen.queryByTestId('packing-shipment-creator')).not.toBeInTheDocument();
  });
});
