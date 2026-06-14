import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import PackingLabelPrintModal from '../PackingLabelPrintModal';
import { printLabelWithReadiness } from '../printLabelPdf';
import { useCompletePackingOrder } from '../../../api/hooks/useCompletePackingOrder';
import type { PackingOrder, ScanShipment } from '../../../api/hooks/useScanPackingOrder';

jest.mock('../printLabelPdf', () => ({
  printLabelWithReadiness: jest.fn(),
}));

jest.mock('../../../api/hooks/useCompletePackingOrder', () => ({
  useCompletePackingOrder: jest.fn(),
}));

const mockPrintWithReadiness = printLabelWithReadiness as jest.Mock;
const mockCompleteMutate = jest.fn();

const order: PackingOrder = {
  code: 'ORD-001',
  customerName: 'Test Customer',
  shippingMethodName: 'DPD',
  shippingAddress: null,
  cooling: 'None',
  isCooled: false,
  customerNote: null,
  eshopNote: null,
  eligibility: { isEligible: true, warningTitle: null, warningBody: null },
  items: [],
};

const shipment: ScanShipment = {
  shipmentGuid: 'guid-1',
  packages: [
    { trackingNumber: null, labelUrl: null, labelZpl: null },
    { trackingNumber: null, labelUrl: null, labelZpl: null },
    { trackingNumber: null, labelUrl: null, labelZpl: null },
  ],
  alreadyExisted: false,
  pendingCompletion: true,
};

const noop = () => {};

beforeEach(() => {
  jest.clearAllMocks();
  (useCompletePackingOrder as jest.Mock).mockReturnValue({
    mutate: mockCompleteMutate,
    isPending: false,
  });
});

describe('PackingLabelPrintModal', () => {
  it('auto-prints label 1 on mount', async () => {
    mockPrintWithReadiness.mockReturnValue(new Promise(() => {}));

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);
    });

    expect(mockPrintWithReadiness).toHaveBeenCalledWith(
      'ORD-001',
      1,
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('shows printing spinner while label is being fetched', async () => {
    mockPrintWithReadiness.mockReturnValue(new Promise(() => {}));

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);
    });

    expect(screen.getByTestId('print-modal-printing')).toBeInTheDocument();
    expect(screen.getByTestId('print-modal-printing')).toHaveTextContent('Připravuji štítek 1/3…');
  });

  it('shows label count after first label is printed', async () => {
    mockPrintWithReadiness.mockResolvedValueOnce({ printed: true });

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);
    });

    expect(screen.getByTestId('print-modal-label-count')).toHaveTextContent('Štítek 1/3');
  });

  it('clicking "Tisknout další štítek" calls printLabelWithReadiness for label 2', async () => {
    mockPrintWithReadiness
      .mockResolvedValueOnce({ printed: true })
      .mockReturnValue(new Promise(() => {}));

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('print-next-label-button'));
    });

    expect(mockPrintWithReadiness).toHaveBeenNthCalledWith(
      2,
      'ORD-001',
      2,
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('calls complete mutation exactly once after all N labels are printed', async () => {
    mockPrintWithReadiness
      .mockResolvedValueOnce({ printed: true })
      .mockResolvedValueOnce({ printed: true })
      .mockResolvedValueOnce({ printed: true });
    mockCompleteMutate.mockImplementation(() => {});

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);
    });

    // Print label 2
    await act(async () => {
      fireEvent.click(screen.getByTestId('print-next-label-button'));
    });

    // Print label 3 (last)
    await act(async () => {
      fireEvent.click(screen.getByTestId('print-next-label-button'));
    });

    expect(mockCompleteMutate).toHaveBeenCalledTimes(1);
    expect(mockCompleteMutate).toHaveBeenCalledWith('ORD-001', expect.objectContaining({
      onSuccess: expect.any(Function),
      onError: expect.any(Function),
    }));
  });

  it('calls onComplete after successful completion', async () => {
    const onComplete = jest.fn();
    mockPrintWithReadiness
      .mockResolvedValueOnce({ printed: true })
      .mockResolvedValueOnce({ printed: true })
      .mockResolvedValueOnce({ printed: true });
    mockCompleteMutate.mockImplementation((_code: string, opts: { onSuccess: () => void }) => {
      opts.onSuccess();
    });

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={onComplete} onCancel={noop} />);
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('print-next-label-button'));
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('print-next-label-button'));
    });

    expect(onComplete).toHaveBeenCalledTimes(1);
  });

  it('cancel mid-sequence calls onCancel and does NOT trigger complete mutation', async () => {
    const onCancel = jest.fn();
    mockPrintWithReadiness.mockReturnValue(new Promise(() => {}));

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={onCancel} />);
    });

    fireEvent.click(screen.getByTestId('cancel-print-modal-button'));

    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(mockCompleteMutate).not.toHaveBeenCalled();
  });

  it('shows timeout phase with retry and cancel when printLabelWithReadiness times out', async () => {
    mockPrintWithReadiness.mockResolvedValueOnce({ printed: false, timedOut: true });

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);
    });

    expect(screen.getByTestId('print-modal-timeout')).toBeInTheDocument();
    expect(screen.getByTestId('retry-print-button')).toBeInTheDocument();
    expect(screen.getByTestId('cancel-print-modal-button')).toBeInTheDocument();
  });

  it('retry in timeout phase calls printLabelWithReadiness again for same label', async () => {
    mockPrintWithReadiness
      .mockResolvedValueOnce({ printed: false, timedOut: true })
      .mockReturnValue(new Promise(() => {}));

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('retry-print-button'));
    });

    expect(mockPrintWithReadiness).toHaveBeenCalledTimes(2);
    expect(mockPrintWithReadiness).toHaveBeenNthCalledWith(
      2,
      'ORD-001',
      1,
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('shows error phase with retry and cancel when fetch fails', async () => {
    mockPrintWithReadiness.mockResolvedValueOnce({ printed: false, status: 503 });

    await act(async () => {
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);
    });

    expect(screen.getByTestId('print-modal-error')).toBeInTheDocument();
    expect(screen.getByTestId('retry-print-button')).toBeInTheDocument();
    expect(screen.getByTestId('cancel-print-modal-button')).toBeInTheDocument();
  });

  it('complete mutation called exactly once even with re-renders (completedRef guard)', async () => {
    mockPrintWithReadiness
      .mockResolvedValueOnce({ printed: true })
      .mockResolvedValueOnce({ printed: true })
      .mockResolvedValueOnce({ printed: true });
    mockCompleteMutate.mockImplementation(() => {});

    const { rerender } = await act(async () =>
      render(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />),
    );

    await act(async () => {
      fireEvent.click(screen.getByTestId('print-next-label-button'));
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('print-next-label-button'));
    });

    // Force re-render — guard must prevent a second call
    rerender(<PackingLabelPrintModal order={order} shipment={shipment} onComplete={noop} onCancel={noop} />);

    expect(mockCompleteMutate).toHaveBeenCalledTimes(1);
  });
});
