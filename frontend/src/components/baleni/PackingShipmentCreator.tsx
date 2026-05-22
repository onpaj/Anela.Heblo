import { useEffect, useState } from 'react';
import { useResetOrderShipment } from '../../api/hooks/useResetOrderShipment';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';
import PackingLabelPrinter from './PackingLabelPrinter';

interface PackingShipmentCreatorProps {
  order: PackingOrder;
  scanShipment: ScanShipment | null;
}

function PackingShipmentCreator({ order, scanShipment }: PackingShipmentCreatorProps) {
  const [showDialog, setShowDialog] = useState(false);
  const [shipmentForPrint, setShipmentForPrint] = useState<ScanShipment | null>(null);
  const resetMutation = useResetOrderShipment();

  useEffect(() => {
    if (!scanShipment) return;
    setShowDialog(false);
    setShipmentForPrint(null);
    if (scanShipment.alreadyExisted) {
      setShowDialog(true);
    } else {
      setShipmentForPrint(scanShipment);
    }
  }, [scanShipment]);

  function handleReprint() {
    setShowDialog(false);
    setShipmentForPrint(scanShipment!);
  }

  function handleInvalidateAndNew() {
    setShowDialog(false);
    resetMutation.mutate(order.code, {
      onSuccess: (newShipment) => {
        setShipmentForPrint(newShipment);
      },
    });
  }

  if (shipmentForPrint) {
    return <PackingLabelPrinter order={order} shipment={shipmentForPrint} />;
  }

  if (resetMutation.isPending) {
    return (
      <div
        data-testid="shipment-creating-spinner"
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6"
      >
        <div className="flex w-full max-w-md flex-col items-center gap-6 rounded-2xl bg-white p-10 shadow-2xl">
          <p className="text-2xl font-bold text-neutral-slate">Vytvářím novou zásilku…</p>
          <div className="relative h-4 w-full overflow-hidden rounded-full bg-neutral-200">
            <div
              className="absolute h-full rounded-full bg-primary-blue"
              style={{ animation: 'indeterminate-progress 1.5s ease-in-out infinite' }}
            />
          </div>
          <p className="text-sm text-neutral-gray">Prosím čekejte</p>
        </div>
      </div>
    );
  }

  if (resetMutation.isError) {
    return (
      <div
        data-testid="shipment-error-banner"
        className="rounded border border-red-300 bg-red-50 px-4 py-2 text-sm text-red-700"
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
        <div className="flex w-full max-w-sm flex-col gap-6 rounded-2xl bg-white p-8 shadow-2xl">
          <p className="text-center text-2xl font-bold text-amber-700">
            Zásilka pro tuto objednávku již existuje.
          </p>
          <div className="flex flex-col gap-4">
            <button
              className="w-full rounded-xl border-2 border-neutral-300 bg-white py-5 text-lg font-semibold shadow active:scale-95"
              onClick={handleReprint}
            >
              Použít existující zásilku
              {scanShipment && (
                <span className="block text-sm font-normal text-neutral-gray">
                  {scanShipment.packages.map((p) => p.trackingNumber ?? p.name).join(', ')}
                </span>
              )}
            </button>
            <button
              className="w-full rounded-xl bg-primary-blue py-5 text-lg font-semibold text-white shadow active:scale-95"
              onClick={handleInvalidateAndNew}
            >
              Vytvořit novou zásilku
            </button>
          </div>
        </div>
      </div>
    );
  }

  return null;
}

export default PackingShipmentCreator;
