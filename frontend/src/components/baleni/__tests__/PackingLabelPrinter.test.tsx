import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import PackingLabelPrinter from '../PackingLabelPrinter';
import { printLabelPdf } from '../printLabelPdf';
import type { PackingOrder, ScanShipment } from '../../../api/hooks/useScanPackingOrder';

jest.mock('../printLabelPdf', () => ({
  printLabelPdf: jest.fn(),
}));

const mockComplete = jest.fn();
jest.mock('../../../api/hooks/useCompletePackingOrder', () => ({
  useCompletePackingOrder: () => ({ mutate: mockComplete }),
}));

jest.mock('../PackingShipmentDoneView', () => ({
  __esModule: true,
  default: ({ onReprint }: { onReprint: () => void }) => (
    <div data-testid="done-view">
      <button data-testid="reprint" onClick={onReprint}>R</button>
    </div>
  ),
}));

const mockPrintLabelPdf = printLabelPdf as jest.MockedFunction<typeof printLabelPdf>;

const makeOrder = (code: string): PackingOrder => ({
  code,
  customerName: 'X',
  shippingMethodName: 'Y',
  cooling: 'None',
  isCooled: false,
  customerNote: null,
  eshopNote: null,
  eligibility: { isEligible: true, warningTitle: null, warningBody: null },
  items: [],
  shippingAddress: null,
});

const makeShipment = (packages: ScanShipment['packages']): ScanShipment => ({
  shipmentGuid: 'guid-1',
  packages,
  alreadyExisted: false,
});

const pkg1 = { name: 'PKG-1', trackingNumber: null, labelUrl: 'https://x.com/1.pdf', labelZpl: null };
const pkg2 = { name: 'PKG-2', trackingNumber: null, labelUrl: 'https://x.com/2.pdf', labelZpl: null };
const pkg3 = { name: 'PKG-3', trackingNumber: null, labelUrl: 'https://x.com/3.pdf', labelZpl: null };

const expectedLabel = (shipmentGuid: string, packageName: string, labelUrl: string) => ({
  shipmentGuid,
  packageName,
  labelUrl,
  labelZpl: undefined,
});

function fireAck(callIndex: number): void {
  const cb = mockPrintLabelPdf.mock.calls[callIndex][2];
  expect(typeof cb).toBe('function');
  act(() => {
    cb?.();
  });
}

beforeEach(() => {
  jest.clearAllMocks();
  mockComplete.mockClear();
});

describe('PackingLabelPrinter', () => {
  it('renders nothing when shipment has no packages', () => {
    const { container } = render(
      <PackingLabelPrinter order={makeOrder('250001')} shipment={makeShipment([])} />
    );
    expect(container).toBeEmptyDOMElement();
    expect(mockPrintLabelPdf).not.toHaveBeenCalled();
  });

  it('auto-prints first label on mount with an onAfterPrint callback', () => {
    render(
      <PackingLabelPrinter order={makeOrder('250001')} shipment={makeShipment([pkg1])} />
    );

    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
    expect(mockPrintLabelPdf).toHaveBeenCalledWith(
      '250001',
      expectedLabel('guid-1', 'PKG-1', 'https://x.com/1.pdf'),
      expect.any(Function)
    );
  });

  it('does NOT show done view until the last label\'s afterprint callback fires (single label)', () => {
    render(
      <PackingLabelPrinter order={makeOrder('250001')} shipment={makeShipment([pkg1])} />
    );

    expect(screen.queryByTestId('done-view')).not.toBeInTheDocument();

    fireAck(0);

    expect(screen.getByTestId('done-view')).toBeInTheDocument();
  });

  it('multi label: each button click prints next label with its own callback; done view appears only after the LAST callback fires', () => {
    render(
      <PackingLabelPrinter
        order={makeOrder('250001')}
        shipment={makeShipment([pkg1, pkg2])}
      />
    );

    // After mount: label1 auto-printed, button for 2/2 visible
    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('print-next-label-button')).toHaveTextContent('Vytisknout štítek 2/2');
    expect(screen.queryByTestId('done-view')).not.toBeInTheDocument();

    // Fire first ack -> still no done view (1 of 2 labels printed; need to print 2nd)
    fireAck(0);
    expect(screen.queryByTestId('done-view')).not.toBeInTheDocument();
    expect(screen.getByTestId('print-next-label-button')).toBeInTheDocument();

    // Click button to print second label
    fireEvent.click(screen.getByTestId('print-next-label-button'));
    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(2);
    expect(mockPrintLabelPdf).toHaveBeenNthCalledWith(
      2,
      '250001',
      expectedLabel('guid-1', 'PKG-2', 'https://x.com/2.pdf'),
      expect.any(Function)
    );

    // printedCount=2, ack=1 → render rule #3 fires (null), button gone but no done view yet
    expect(screen.queryByTestId('print-next-label-button')).not.toBeInTheDocument();
    expect(screen.queryByTestId('done-view')).not.toBeInTheDocument();

    // Fire 2nd ack -> done view appears
    fireAck(1);
    expect(screen.getByTestId('done-view')).toBeInTheDocument();
  });

  it('three labels: button cycles through and done view shows only after final ack', () => {
    render(
      <PackingLabelPrinter
        order={makeOrder('250001')}
        shipment={makeShipment([pkg1, pkg2, pkg3])}
      />
    );

    expect(screen.getByTestId('print-next-label-button')).toHaveTextContent('Vytisknout štítek 2/3');
    fireAck(0);

    fireEvent.click(screen.getByTestId('print-next-label-button'));
    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(2);
    expect(mockPrintLabelPdf).toHaveBeenNthCalledWith(
      2,
      '250001',
      expectedLabel('guid-1', 'PKG-2', 'https://x.com/2.pdf'),
      expect.any(Function)
    );
    expect(screen.getByTestId('print-next-label-button')).toHaveTextContent('Vytisknout štítek 3/3');
    fireAck(1);

    fireEvent.click(screen.getByTestId('print-next-label-button'));
    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(3);
    expect(mockPrintLabelPdf).toHaveBeenNthCalledWith(
      3,
      '250001',
      expectedLabel('guid-1', 'PKG-3', 'https://x.com/3.pdf'),
      expect.any(Function)
    );

    // All printed but final ack pending → null
    expect(screen.queryByTestId('print-next-label-button')).not.toBeInTheDocument();
    expect(screen.queryByTestId('done-view')).not.toBeInTheDocument();

    fireAck(2);
    expect(screen.getByTestId('done-view')).toBeInTheDocument();
  });

  it('clicking reprint in done view resets state and re-prints from label 0', () => {
    render(
      <PackingLabelPrinter order={makeOrder('250001')} shipment={makeShipment([pkg1])} />
    );

    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
    fireAck(0);
    expect(screen.getByTestId('done-view')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('reprint'));

    // After reprint click: effect re-fires, prints label[0] again
    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(2);
    expect(mockPrintLabelPdf).toHaveBeenNthCalledWith(
      2,
      '250001',
      expectedLabel('guid-1', 'PKG-1', 'https://x.com/1.pdf'),
      expect.any(Function)
    );
    expect(screen.queryByTestId('done-view')).not.toBeInTheDocument();
  });

  it('resets state when order.code changes', () => {
    const shipment = makeShipment([pkg1]);
    const { rerender } = render(
      <PackingLabelPrinter order={makeOrder('250001')} shipment={shipment} />
    );

    fireAck(0);
    expect(screen.getByTestId('done-view')).toBeInTheDocument();

    rerender(<PackingLabelPrinter order={makeOrder('250002')} shipment={shipment} />);

    // Counters reset; auto-prints labels[0] of new order
    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(2);
    expect(mockPrintLabelPdf).toHaveBeenLastCalledWith(
      '250002',
      expectedLabel('guid-1', 'PKG-1', 'https://x.com/1.pdf'),
      expect.any(Function)
    );
    expect(screen.queryByTestId('done-view')).not.toBeInTheDocument();
  });

  it('fires completion once when done and shipment is pendingCompletion', () => {
    const shipment = { ...makeShipment([pkg1, pkg2]), pendingCompletion: true };
    render(<PackingLabelPrinter order={makeOrder('250001')} shipment={shipment} />);

    fireAck(0); // first label acknowledged
    fireEvent.click(screen.getByTestId('print-next-label-button'));
    fireAck(1); // last label acknowledged → done

    expect(mockComplete).toHaveBeenCalledTimes(1);
    expect(mockComplete).toHaveBeenCalledWith('250001');
  });

  it('does NOT fire completion for a single-package (pendingCompletion absent) shipment', () => {
    render(<PackingLabelPrinter order={makeOrder('250001')} shipment={makeShipment([pkg1])} />);
    fireAck(0);
    expect(screen.getByTestId('done-view')).toBeInTheDocument();
    expect(mockComplete).not.toHaveBeenCalled();
  });
});
