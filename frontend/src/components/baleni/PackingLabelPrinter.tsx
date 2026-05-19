import { useEffect, useState } from 'react';
import { useShipmentLabels } from '../../api/hooks/useShipmentLabels';
import { printLabelPdf } from './printLabelPdf';

interface PackingLabelPrinterProps {
  orderCode: string;
}

function PackingLabelPrinter({ orderCode }: PackingLabelPrinterProps) {
  const { data: labels, isError, error } = useShipmentLabels(orderCode, true);
  const [printedCount, setPrintedCount] = useState(0);

  useEffect(() => {
    setPrintedCount(0);
  }, [orderCode]);

  useEffect(() => {
    if (labels && labels.length > 0 && printedCount === 0) {
      printLabelPdf(orderCode, labels[0]);
      setPrintedCount(1);
    }
  }, [labels, orderCode, printedCount]);

  if (isError && error) {
    return (
      <div
        data-testid="label-print-error"
        className="rounded border border-red-300 bg-red-50 px-4 py-2 text-sm text-red-700"
      >
        {(error as Error).message}
      </div>
    );
  }

  if (!labels || printedCount === 0 || printedCount >= labels.length) {
    return null;
  }

  const total = labels.length;

  return (
    <button
      data-testid="print-next-label-button"
      className="rounded-lg bg-brand-600 px-6 py-4 text-lg font-semibold text-white shadow active:scale-95"
      onClick={() => {
        printLabelPdf(orderCode, labels[printedCount]);
        setPrintedCount((c) => c + 1);
      }}
    >
      {`Vytisknout štítek ${printedCount + 1}/${total}`}
    </button>
  );
}

export default PackingLabelPrinter;
