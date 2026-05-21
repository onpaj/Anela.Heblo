import { useEffect, useState } from 'react';
import { printLabelPdf } from './printLabelPdf';
import type { ShipmentLabelDto } from '../../api/generated/api-client';

interface PackingLabelPrinterProps {
  orderCode: string;
  labels: ShipmentLabelDto[];
}

function PackingLabelPrinter({ orderCode, labels }: PackingLabelPrinterProps) {
  const [printedCount, setPrintedCount] = useState(0);

  useEffect(() => {
    setPrintedCount(0);
  }, [orderCode]);

  useEffect(() => {
    if (labels.length > 0 && printedCount === 0) {
      printLabelPdf(orderCode, labels[0]);
      setPrintedCount(1);
    }
  }, [labels, orderCode, printedCount]);

  if (labels.length === 0 || printedCount === 0 || printedCount >= labels.length) {
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
