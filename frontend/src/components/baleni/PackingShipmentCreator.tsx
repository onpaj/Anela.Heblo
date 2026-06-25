import { useEffect, useState } from 'react';
import { Minus, Plus } from 'lucide-react';
import { useResetOrderShipment } from '../../api/hooks/useResetOrderShipment';
import { useOrderTrackingNumbers } from '../../api/hooks/useOrderTrackingNumbers';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';
import PackingLabelPrintModal from './PackingLabelPrintModal';
import PackingLabelPrinter from './PackingLabelPrinter';
import PackingShipmentDoneView from './PackingShipmentDoneView';

const MIN_PACKAGES = 1;
const MAX_PACKAGES = 10;

interface PackingShipmentCreatorProps {
  order: PackingOrder;
  scanShipment: ScanShipment | null;
  onDoneStateChange?: (isDone: boolean) => void;
  onPrintModalOpenChange?: (isOpen: boolean) => void;
}

function PackingShipmentCreator({ order, scanShipment, onDoneStateChange, onPrintModalOpenChange }: PackingShipmentCreatorProps) {
  const [showDialog, setShowDialog] = useState(false);
  const [shipmentForPrint, setShipmentForPrint] = useState<ScanShipment | null>(null);
  const [printerIsDone, setPrinterIsDone] = useState(false);
  const [completedMultiShipment, setCompletedMultiShipment] = useState<ScanShipment | null>(null);
  const [recreateCount, setRecreateCount] = useState(1);
  const resetMutation = useResetOrderShipment();

  const isEligible = order.eligibility.isEligible;
  const showsNonEligibleReview =
    scanShipment !== null && !isEligible && shipmentForPrint === null;
  const isShowingDoneView = showsNonEligibleReview || printerIsDone;

  // The done view (multi-package completion + non-eligible review) re-fetches per-package tracking
  // numbers, since the values captured at scan time are usually null (Shoptet assigns them async).
  const showsTrackingDoneView =
    (printerIsDone && completedMultiShipment !== null) || showsNonEligibleReview;
  const trackingNumbersQuery = useOrderTrackingNumbers(order.code, showsTrackingDoneView);

  useEffect(() => {
    setPrinterIsDone(false);
    setShowDialog(false);
    setShipmentForPrint(null);
    setCompletedMultiShipment(null);
    setRecreateCount(1);
    if (!scanShipment) return;
    if (!isEligible) return;
    if (scanShipment.alreadyExisted) {
      setShowDialog(true);
    } else {
      setShipmentForPrint(scanShipment);
    }
  }, [scanShipment, isEligible]);

  useEffect(() => {
    onDoneStateChange?.(isShowingDoneView);
  }, [isShowingDoneView, onDoneStateChange]);

  const isMultiPackageModalOpen =
    shipmentForPrint?.pendingCompletion === true && (shipmentForPrint?.packages.length ?? 0) >= 2;
  useEffect(() => {
    onPrintModalOpenChange?.(isMultiPackageModalOpen);
  }, [isMultiPackageModalOpen, onPrintModalOpenChange]);

  function handleReprint() {
    setShowDialog(false);
    setShipmentForPrint(scanShipment!);
  }

  function handleInvalidateAndNew() {
    setShowDialog(false);
    resetMutation.mutate(
      { orderCode: order.code, numberOfPackages: recreateCount },
      {
        onSuccess: (newShipment) => {
          setShipmentForPrint(newShipment);
        },
      },
    );
  }

  function handleNonEligibleReprint() {
    if (scanShipment) {
      setShipmentForPrint(scanShipment);
    }
  }

  if (shipmentForPrint) {
    if (isMultiPackageModalOpen) {
      return (
        <PackingLabelPrintModal
          order={order}
          shipment={shipmentForPrint}
          onComplete={() => {
            setCompletedMultiShipment(shipmentForPrint);
            setShipmentForPrint(null);
            setPrinterIsDone(true);
          }}
          onCancel={() => setShipmentForPrint(null)}
        />
      );
    }
    return (
      <PackingLabelPrinter
        order={order}
        shipment={shipmentForPrint}
        onDoneStateChange={setPrinterIsDone}
      />
    );
  }

  if (printerIsDone && completedMultiShipment) {
    return (
      <PackingShipmentDoneView
        order={order}
        shipment={completedMultiShipment}
        resolvedTrackingNumbers={trackingNumbersQuery.data ?? null}
        onReprint={() => {
          setCompletedMultiShipment(null);
          setPrinterIsDone(false);
          setShipmentForPrint(completedMultiShipment);
        }}
      />
    );
  }

  if (showsNonEligibleReview && scanShipment) {
    return (
      <PackingShipmentDoneView
        order={order}
        shipment={scanShipment}
        resolvedTrackingNumbers={trackingNumbersQuery.data ?? null}
        onReprint={handleNonEligibleReprint}
      />
    );
  }

  if (resetMutation.isPending) {
    return (
      <div
        data-testid="shipment-creating-spinner"
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6"
      >
        <div className="flex w-full max-w-md flex-col items-center gap-6 rounded-2xl bg-white dark:bg-graphite-surface p-10 shadow-2xl">
          <p className="text-2xl font-bold text-neutral-slate dark:text-graphite-text">Vytvářím novou zásilku…</p>
          <div className="relative h-4 w-full overflow-hidden rounded-full bg-neutral-200 dark:bg-graphite-surface-2">
            <div
              className="absolute h-full rounded-full bg-primary-blue"
              style={{ animation: 'indeterminate-progress 1.5s ease-in-out infinite' }}
            />
          </div>
          <p className="text-sm text-neutral-gray dark:text-graphite-muted">Prosím čekejte</p>
        </div>
      </div>
    );
  }

  if (resetMutation.isError) {
    return (
      <div
        data-testid="shipment-error-banner"
        className="rounded border border-red-300 dark:border-red-500/40 bg-red-50 dark:bg-red-900/20 px-4 py-2 text-sm text-red-700 dark:text-red-300"
      >
        {resetMutation.error?.message ?? 'Zásilku se nepodařilo vytvořit'}
      </div>
    );
  }

  if (showDialog) {
    return (
      <div
        data-testid="existing-shipment-modal"
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6"
      >
        <div className="flex w-full max-w-sm flex-col gap-6 rounded-2xl bg-white dark:bg-graphite-surface p-8 shadow-2xl">
          <p className="text-center text-2xl font-bold text-amber-700 dark:text-amber-400">
            Zásilka pro tuto objednávku již existuje.
          </p>
          <div className="flex flex-col gap-4">
            <button
              className="w-full rounded-xl border-2 border-neutral-300 dark:border-graphite-border bg-white dark:bg-graphite-surface py-5 text-lg font-semibold text-neutral-slate dark:text-graphite-text shadow dark:shadow-soft-dark active:scale-95"
              onClick={handleReprint}
            >
              Použít existující zásilku
              {scanShipment && (
                <span className="block text-sm font-normal text-neutral-gray dark:text-graphite-muted">
                  {scanShipment.packages.map((p, index) => p.trackingNumber ?? `Balík ${index + 1}`).join(', ')}
                </span>
              )}
            </button>
            <div className="flex flex-col gap-3 rounded-xl border-2 border-neutral-200 dark:border-graphite-border p-4">
              <span className="text-center text-sm font-medium text-neutral-gray dark:text-graphite-muted">Počet balíků</span>
              <div className="flex items-center justify-center gap-6">
                <button
                  type="button"
                  aria-label="Méně balíků"
                  data-testid="recreate-package-decrement"
                  disabled={recreateCount <= MIN_PACKAGES}
                  onClick={() => setRecreateCount((c) => Math.max(MIN_PACKAGES, c - 1))}
                  className="flex h-14 w-14 items-center justify-center rounded-2xl border-2 border-neutral-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-neutral-slate dark:text-graphite-text shadow dark:shadow-soft-dark active:scale-95 disabled:opacity-40"
                >
                  <Minus className="h-7 w-7" />
                </button>
                <span
                  data-testid="recreate-package-count"
                  className="w-12 text-center text-4xl font-bold text-neutral-slate dark:text-graphite-text"
                >
                  {recreateCount}
                </span>
                <button
                  type="button"
                  aria-label="Více balíků"
                  data-testid="recreate-package-increment"
                  disabled={recreateCount >= MAX_PACKAGES}
                  onClick={() => setRecreateCount((c) => Math.min(MAX_PACKAGES, c + 1))}
                  className="flex h-14 w-14 items-center justify-center rounded-2xl bg-primary-blue text-white shadow active:scale-95 disabled:opacity-40"
                >
                  <Plus className="h-7 w-7" />
                </button>
              </div>
              <button
                className="w-full rounded-xl bg-primary-blue py-5 text-lg font-semibold text-white shadow active:scale-95"
                onClick={handleInvalidateAndNew}
              >
                Vytvořit novou zásilku
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return null;
}

export default PackingShipmentCreator;
