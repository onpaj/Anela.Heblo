import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import PackingShipmentCreator from '../PackingShipmentCreator';
import type { PackingOrder, ScanShipment } from '../../../api/hooks/useScanPackingOrder';

jest.mock('../PackingLabelPrinter', () => ({
  __esModule: true,
  default: ({ order, shipment }: { order: { code: string }; shipment: { packages: unknown[] } }) => (
    <div data-testid="label-printer">
      {order.code}:{shipment.packages.length} labels
    </div>
  ),
}));

jest.mock('../PackingLabelPrintModal', () => ({
  __esModule: true,
  default: ({ onComplete, onCancel }: { onComplete: () => void; onCancel: () => void }) => (
    <div data-testid="label-print-modal">
      <button onClick={onComplete}>complete</button>
      <button onClick={onCancel}>cancel</button>
    </div>
  ),
}));

const mockResetMutate = jest.fn();
let mockResetState = {
  mutate: mockResetMutate,
  isPending: false,
  isError: false,
  error: null as Error | null,
};

jest.mock('../../../api/hooks/useResetOrderShipment', () => ({
  useResetOrderShipment: () => mockResetState,
}));

jest.mock('../../../api/hooks/useOrderTrackingNumbers', () => ({
  useOrderTrackingNumbers: () => ({ data: null }),
}));

const someOrder: PackingOrder = {
  code: 'ORD001',
  customerName: 'X',
  shippingMethodName: 'Y',
  cooling: 'None',
  isCooled: false,
  customerNote: null,
  eshopNote: null,
  eligibility: { isEligible: true, warningTitle: null, warningBody: null },
  items: [],
  shippingAddress: null,
};

const newShipment: ScanShipment = {
  shipmentGuid: 'guid-new',
  packages: [{
    trackingNumber: null,
    labelUrl: 'https://carrier.example.com/new.pdf',
    labelZpl: null,
  }],
  alreadyExisted: false,
};

const existingShipment: ScanShipment = {
  shipmentGuid: 'guid-existing',
  packages: [{
    trackingNumber: null,
    labelUrl: 'https://carrier.example.com/existing.pdf',
    labelZpl: null,
  }],
  alreadyExisted: true,
};

const multiPackageShipment: ScanShipment = {
  shipmentGuid: 'guid-multi',
  packages: [
    { trackingNumber: null, labelUrl: null, labelZpl: null },
    { trackingNumber: null, labelUrl: null, labelZpl: null },
  ],
  alreadyExisted: false,
  pendingCompletion: true,
};

describe('PackingShipmentCreator', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockResetState = { mutate: mockResetMutate, isPending: false, isError: false, error: null };
  });

  it('renders nothing when scanShipment is null', () => {
    const { container } = render(
      <PackingShipmentCreator order={someOrder} scanShipment={null} />
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('shows PackingLabelPrinter immediately when shipment is new', () => {
    render(<PackingShipmentCreator order={someOrder} scanShipment={newShipment} />);
    expect(screen.getByTestId('label-printer')).toBeInTheDocument();
  });

  it('shows dialog when shipment already existed', () => {
    render(<PackingShipmentCreator order={someOrder} scanShipment={existingShipment} />);
    expect(screen.queryByTestId('label-printer')).not.toBeInTheDocument();
    expect(screen.getByText(/existující zásilku/i)).toBeInTheDocument();
    expect(screen.getByText(/novou zásilku/i)).toBeInTheDocument();
  });

  it('clicking reprint shows PackingLabelPrinter and hides dialog buttons', () => {
    render(<PackingShipmentCreator order={someOrder} scanShipment={existingShipment} />);
    const reprintBtn = screen.getByRole('button', { name: /Použít existující zásilku/i });
    fireEvent.click(reprintBtn);
    expect(screen.getByTestId('label-printer')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('clicking invalidate calls resetMutation.mutate with orderCode and default count', () => {
    render(<PackingShipmentCreator order={someOrder} scanShipment={existingShipment} />);
    const newBtn = screen.getByRole('button', { name: /Vytvořit novou zásilku/i });
    fireEvent.click(newBtn);
    expect(mockResetMutate).toHaveBeenCalledWith(
      { orderCode: 'ORD001', numberOfPackages: 1 },
      expect.any(Object),
    );
  });

  it('passes the chosen package count to resetMutation.mutate when recreating', () => {
    render(<PackingShipmentCreator order={someOrder} scanShipment={existingShipment} />);
    fireEvent.click(screen.getByTestId('recreate-package-increment'));
    fireEvent.click(screen.getByTestId('recreate-package-increment'));
    expect(screen.getByTestId('recreate-package-count')).toHaveTextContent('3');
    fireEvent.click(screen.getByRole('button', { name: /Vytvořit novou zásilku/i }));
    expect(mockResetMutate).toHaveBeenCalledWith(
      { orderCode: 'ORD001', numberOfPackages: 3 },
      expect.any(Object),
    );
  });

  it('shows spinner while resetMutation is pending', () => {
    mockResetState = { ...mockResetState, isPending: true };
    render(<PackingShipmentCreator order={someOrder} scanShipment={existingShipment} />);
    expect(screen.getByTestId('shipment-creating-spinner')).toBeInTheDocument();
  });

  it('shows error banner when resetMutation errors', () => {
    mockResetState = {
      ...mockResetState,
      isError: true,
      error: new Error('Shoptet nemohl vytvořit novou zásilku.'),
    };
    render(<PackingShipmentCreator order={someOrder} scanShipment={existingShipment} />);
    expect(screen.getByTestId('shipment-error-banner')).toBeInTheDocument();
    expect(screen.getByText(/Shoptet nemohl vytvořit novou zásilku/i)).toBeInTheDocument();
  });

  it('shows PackingLabelPrintModal for new multi-package shipment (pendingCompletion)', () => {
    render(<PackingShipmentCreator order={someOrder} scanShipment={multiPackageShipment} />);
    expect(screen.getByTestId('label-print-modal')).toBeInTheDocument();
    expect(screen.queryByTestId('label-printer')).not.toBeInTheDocument();
  });

  it('onComplete from print modal sets done state', () => {
    const onDoneStateChange = jest.fn();
    render(
      <PackingShipmentCreator
        order={someOrder}
        scanShipment={multiPackageShipment}
        onDoneStateChange={onDoneStateChange}
      />
    );
    fireEvent.click(screen.getByRole('button', { name: /complete/i }));
    expect(onDoneStateChange).toHaveBeenCalledWith(true);
  });

  it('onCancel from print modal hides modal', () => {
    render(<PackingShipmentCreator order={someOrder} scanShipment={multiPackageShipment} />);
    expect(screen.getByTestId('label-print-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(screen.queryByTestId('label-print-modal')).not.toBeInTheDocument();
  });
});
