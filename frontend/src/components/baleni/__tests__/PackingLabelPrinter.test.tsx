import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import PackingLabelPrinter from '../PackingLabelPrinter';
import * as printLabelPdfModule from '../printLabelPdf';

jest.mock('../printLabelPdf', () => ({
  printLabelPdf: jest.fn(),
}));

const mockPrintLabelPdf = printLabelPdfModule.printLabelPdf as jest.MockedFunction<
  typeof printLabelPdfModule.printLabelPdf
>;

const label1 = { shipmentGuid: 'guid-1', packageName: 'Zásilka 1', labelUrl: 'https://x.com/1.pdf' };
const label2 = { shipmentGuid: 'guid-1', packageName: 'Zásilka 2', labelUrl: 'https://x.com/2.pdf' };
const label3 = { shipmentGuid: 'guid-1', packageName: 'Zásilka 3', labelUrl: 'https://x.com/3.pdf' };

beforeEach(() => {
  jest.clearAllMocks();
});

describe('PackingLabelPrinter', () => {
  it('renders nothing when labels array is empty', () => {
    const { container } = render(<PackingLabelPrinter orderCode="250001" labels={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('auto-prints the first label on mount', () => {
    render(<PackingLabelPrinter orderCode="250001" labels={[label1]} />);

    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
    expect(mockPrintLabelPdf).toHaveBeenCalledWith('250001', label1);
  });

  it('renders nothing visible after auto-printing the only label', () => {
    const { container } = render(<PackingLabelPrinter orderCode="250001" labels={[label1]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows confirmation button for each label after the first', () => {
    render(<PackingLabelPrinter orderCode="250001" labels={[label1, label2, label3]} />);

    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
    expect(mockPrintLabelPdf).toHaveBeenCalledWith('250001', label1);
    expect(screen.getByTestId('print-next-label-button')).toHaveTextContent(
      'Vytisknout štítek 2/3'
    );
  });

  it('prints the next label and updates the button when tapped', () => {
    render(<PackingLabelPrinter orderCode="250001" labels={[label1, label2, label3]} />);

    fireEvent.click(screen.getByTestId('print-next-label-button'));

    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(2);
    expect(mockPrintLabelPdf).toHaveBeenNthCalledWith(2, '250001', label2);
    expect(screen.getByTestId('print-next-label-button')).toHaveTextContent(
      'Vytisknout štítek 3/3'
    );
  });

  it('hides the button after all labels are printed', () => {
    render(<PackingLabelPrinter orderCode="250001" labels={[label1, label2]} />);
    fireEvent.click(screen.getByTestId('print-next-label-button'));

    expect(screen.queryByTestId('print-next-label-button')).not.toBeInTheDocument();
  });

  it('resets printedCount when the orderCode changes', () => {
    const { rerender } = render(<PackingLabelPrinter orderCode="250001" labels={[label1, label2]} />);

    // After first render: auto-printed label1, button shows "2/2"
    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('print-next-label-button')).toHaveTextContent('2/2');

    // New scan
    rerender(<PackingLabelPrinter orderCode="250002" labels={[label1, label2]} />);

    // auto-prints the first label of the new order
    expect(mockPrintLabelPdf).toHaveBeenCalledTimes(2);
    expect(mockPrintLabelPdf).toHaveBeenLastCalledWith('250002', label1);
  });
});
