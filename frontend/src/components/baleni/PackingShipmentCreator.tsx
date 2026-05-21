import { useEffect, useState } from 'react';
import { Loader2 } from 'lucide-react';
import { useResetOrderShipment } from '../../api/hooks/useResetOrderShipment';
import type { ScanShipment } from '../../api/hooks/useScanPackingOrder';
import type { ShipmentLabelDto } from '../../api/generated/api-client';
import PackingLabelPrinter from './PackingLabelPrinter';

interface PackingShipmentCreatorProps {
  orderCode: string;
  scanShipment: ScanShipment | null;
}

function toLabels(shipment: ScanShipment): ShipmentLabelDto[] {
  return shipment.packages.map(
    (pkg) =>
      ({
        shipmentGuid: shipment.shipmentGuid,
        packageName: pkg.name,
      }) as ShipmentLabelDto
  );
}

function PackingShipmentCreator({ orderCode, scanShipment }: PackingShipmentCreatorProps) {
  const [showDialog, setShowDialog] = useState(false);
  const [labelsForPrint, setLabelsForPrint] = useState<ShipmentLabelDto[] | null>(null);
  const resetMutation = useResetOrderShipment();

  useEffect(() => {
    if (!scanShipment) return;
    setShowDialog(false);
    setLabelsForPrint(null);
    if (scanShipment.alreadyExisted) {
      setShowDialog(true);
    } else {
      setLabelsForPrint(toLabels(scanShipment));
    }
  }, [scanShipment]);

  function handleReprint() {
    setShowDialog(false);
    setLabelsForPrint(toLabels(scanShipment!));
  }

  function handleInvalidateAndNew() {
    setShowDialog(false);
    resetMutation.mutate(orderCode, {
      onSuccess: (newShipment) => {
        setLabelsForPrint(toLabels(newShipment));
      },
    });
  }

  if (labelsForPrint) {
    return <PackingLabelPrinter orderCode={orderCode} labels={labelsForPrint} />;
  }

  if (resetMutation.isPending) {
    return (
      <div data-testid="shipment-creating-spinner" className="flex items-center gap-2 text-neutral-gray">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span>Vytvářím novou zásilku…</span>
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
      <div className="flex flex-col gap-3">
        <p className="text-sm font-semibold text-amber-700">
          Zásilka pro tuto objednávku již existuje.
        </p>
        <div className="flex gap-3">
          <button
            className="rounded-lg border border-neutral-300 bg-white px-5 py-3 text-sm font-medium shadow"
            onClick={handleReprint}
          >
            Použít existující zásilku
          </button>
          <button
            className="rounded-lg bg-brand-600 px-5 py-3 text-sm font-semibold text-white shadow active:scale-95"
            onClick={handleInvalidateAndNew}
          >
            Vytvořit novou zásilku
          </button>
        </div>
      </div>
    );
  }

  return null;
}

export default PackingShipmentCreator;
