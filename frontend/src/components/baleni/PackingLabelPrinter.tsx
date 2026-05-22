import { useEffect, useMemo, useState } from 'react';
import { printLabelPdf } from './printLabelPdf';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';
import type { ShipmentLabelDto } from '../../api/generated/api-client';
import PackingShipmentDoneView from './PackingShipmentDoneView';

interface PackingLabelPrinterProps {
  order: PackingOrder;
  shipment: ScanShipment;
}

function toLabels(shipment: ScanShipment): ShipmentLabelDto[] {
  return shipment.packages.map(
    (pkg) =>
      ({
        shipmentGuid: shipment.shipmentGuid,
        packageName: pkg.name,
        labelUrl: pkg.labelUrl ?? undefined,
        labelZpl: pkg.labelZpl ?? undefined,
      }) as ShipmentLabelDto
  );
}

function PackingLabelPrinter({ order, shipment }: PackingLabelPrinterProps) {
  const [printedCount, setPrintedCount] = useState(0);
  const [acknowledgedCount, setAcknowledgedCount] = useState(0);

  const labels = useMemo(() => toLabels(shipment), [shipment]);

  useEffect(() => {
    setPrintedCount(0);
    setAcknowledgedCount(0);
  }, [order.code]);

  useEffect(() => {
    if (labels.length > 0 && printedCount === 0) {
      printLabelPdf(order.code, labels[0], () =>
        setAcknowledgedCount((c) => c + 1)
      );
      setPrintedCount(1);
    }
  }, [labels, order.code, printedCount]);

  function handleReprint() {
    setPrintedCount(0);
    setAcknowledgedCount(0);
  }

  if (labels.length === 0) {
    return null;
  }

  if (acknowledgedCount >= labels.length) {
    return (
      <PackingShipmentDoneView
        order={order}
        shipment={shipment}
        onReprint={handleReprint}
      />
    );
  }

  if (printedCount === 0 || printedCount >= labels.length) {
    return null;
  }

  const total = labels.length;

  return (
    <button
      data-testid="print-next-label-button"
      className="rounded-lg bg-brand-600 px-6 py-4 text-lg font-semibold text-white shadow active:scale-95"
      onClick={() => {
        printLabelPdf(order.code, labels[printedCount], () =>
          setAcknowledgedCount((c) => c + 1)
        );
        setPrintedCount((c) => c + 1);
      }}
    >
      {`Vytisknout štítek ${printedCount + 1}/${total}`}
    </button>
  );
}

export default PackingLabelPrinter;
