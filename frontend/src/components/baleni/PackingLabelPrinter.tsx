import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { printLabelPdf } from './printLabelPdf';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';
import type { ShipmentLabelDto } from '../../api/generated/api-client';
import PackingShipmentDoneView from './PackingShipmentDoneView';
import { useCompletePackingOrder } from '../../api/hooks/useCompletePackingOrder';
import { useOrderTrackingNumber } from '../../api/hooks/useOrderTrackingNumber';

interface PackingLabelPrinterProps {
  order: PackingOrder;
  shipment: ScanShipment;
  onDoneStateChange?: (isDone: boolean) => void;
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

function PackingLabelPrinter({ order, shipment, onDoneStateChange }: PackingLabelPrinterProps) {
  const [printedCount, setPrintedCount] = useState(0);
  const [acknowledgedCount, setAcknowledgedCount] = useState(0);
  const [printError, setPrintError] = useState<{ packageNumber: number; status?: number } | null>(null);
  const [reprintGeneration, setReprintGeneration] = useState(0);

  // Tracks which "print context" we've already auto-printed for. A state guard
  // (printedCount === 0) can't prevent the double-print because React StrictMode
  // double-invokes effects synchronously in development, before the state update
  // commits — so both invocations see the stale value. A ref is written
  // synchronously and survives that double-invoke, firing the print exactly once.
  const autoPrintedContextRef = useRef<string | null>(null);

  const completeMutation = useCompletePackingOrder();
  const completedRef = useRef(false);

  const labels = useMemo(() => toLabels(shipment), [shipment]);
  const isDone = labels.length > 0 && acknowledgedCount >= labels.length;

  const trackingQuery = useOrderTrackingNumber(order.code, isDone);

  // Print a package, clearing the error on success and recording it on failure.
  // A failed fetch (e.g. 404 = carrier label not generated) must NOT advance the
  // counter — that would falsely mark the shipment as printed.
  const printPackage = useCallback(
    (packageNumber: number) => {
      printLabelPdf(
        order.code,
        { packageNumber },
        () => {
          setPrintError(null);
          setAcknowledgedCount((c) => c + 1);
        },
        (status) => setPrintError({ packageNumber, status }),
      );
    },
    [order.code],
  );

  // Reset the completion guard only when the order changes — NOT on reprint, so a
  // reprinted pendingCompletion order never re-fires the mark-as-packed mutation.
  useEffect(() => {
    completedRef.current = false;
  }, [order.code]);

  useEffect(() => {
    onDoneStateChange?.(isDone);
  }, [isDone, onDoneStateChange]);

  useEffect(() => {
    if (isDone && shipment.pendingCompletion && !completedRef.current) {
      completedRef.current = true;
      completeMutation.mutate(order.code, {
        onError: () => {
          completedRef.current = false;
        },
      });
    }
    // completeMutation identity is not stable across renders; the ref guards double-fire.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isDone, shipment.pendingCompletion, order.code]);

  useEffect(() => {
    if (labels.length === 0) return;

    // A new order or a reprint click starts a fresh print context. Re-runs within
    // the same context (StrictMode double-invoke, unrelated re-renders) are ignored.
    const printContext = `${order.code}|${reprintGeneration}`;
    if (autoPrintedContextRef.current === printContext) return;
    autoPrintedContextRef.current = printContext;

    setPrintedCount(1);
    setAcknowledgedCount(0);
    setPrintError(null);
    printPackage(1);
  }, [labels, order.code, reprintGeneration, printPackage]);

  function handleReprint() {
    setPrintedCount(0);
    setAcknowledgedCount(0);
    setPrintError(null);
    setReprintGeneration((g) => g + 1);
  }

  if (labels.length === 0) {
    return null;
  }

  if (isDone) {
    return (
      <PackingShipmentDoneView
        order={order}
        shipment={shipment}
        resolvedTrackingNumber={trackingQuery.data ?? null}
        onReprint={handleReprint}
      />
    );
  }

  if (printError) {
    return (
      <div className="flex flex-col items-center gap-3">
        <p className="text-center text-red-600">
          {printError.status === 404
            ? `Štítek ${printError.packageNumber} zatím nebyl u dopravce vygenerován.`
            : `Štítek ${printError.packageNumber} se nepodařilo načíst${printError.status ? ` (chyba ${printError.status})` : ''}.`}
        </p>
        <button
          data-testid="retry-print-label-button"
          className="rounded-lg bg-brand-600 px-6 py-4 text-lg font-semibold text-white shadow active:scale-95"
          onClick={() => printPackage(printError.packageNumber)}
        >
          Zkusit štítek znovu
        </button>
      </div>
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
        printPackage(printedCount + 1);
        setPrintedCount((c) => c + 1);
      }}
    >
      {`Vytisknout štítek ${printedCount + 1}/${total}`}
    </button>
  );
}

export default PackingLabelPrinter;
