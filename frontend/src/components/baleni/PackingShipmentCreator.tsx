import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { useCreateShipment, CreateShipmentResult } from '../../api/hooks/useCreateShipment';
import { useShipmentLabels } from '../../api/hooks/useShipmentLabels';
import PackingLabelPrinter from './PackingLabelPrinter';
import type { ShipmentLabelDto } from '../../api/generated/api-client';

interface PackingShipmentCreatorProps {
  orderCode: string;
}

function PackingShipmentCreator({ orderCode }: PackingShipmentCreatorProps) {
  const mutation = useCreateShipment();
  const [reuseExisting, setReuseExisting] = useState(false);

  // useShipmentLabels with enabled=false — only used for its refetch on the retry path
  const labelsQuery = useShipmentLabels(orderCode, false);

  const handleCreate = (forceCreate: boolean) => {
    setReuseExisting(false);
    mutation.mutate({ orderCode, forceCreate });
  };

  const handleUseExisting = (_existingLabels: ShipmentLabelDto[]) => {
    setReuseExisting(true);
  };

  const handleRetry = () => {
    void labelsQuery.refetch();
  };

  if (reuseExisting) {
    return <PackingLabelPrinter orderCode={orderCode} />;
  }

  if (mutation.isPending) {
    return (
      <div data-testid="shipment-creating-spinner" className="flex items-center gap-2 text-neutral-gray">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span>Vytvářím zásilku…</span>
      </div>
    );
  }

  if (mutation.isError) {
    return (
      <div
        data-testid="shipment-error-banner"
        className="rounded border border-red-300 bg-red-50 px-4 py-2 text-sm text-red-700"
      >
        {mutation.error?.message ?? 'Zásilku se nepodařilo vytvořit'}
        <button
          className="ml-4 underline"
          onClick={() => mutation.reset()}
        >
          Zpět
        </button>
      </div>
    );
  }

  const result: CreateShipmentResult | undefined = mutation.data;

  if (result?.existingShipmentFound) {
    return (
      <div className="flex flex-col gap-3">
        <p className="text-sm font-semibold text-amber-700">
          Zásilka již existuje pro tuto objednávku.
        </p>
        <div className="flex gap-3">
          <button
            className="rounded-lg border border-neutral-300 bg-white px-5 py-3 text-sm font-medium shadow"
            onClick={() => handleUseExisting(result.labels)}
          >
            Použít existující
          </button>
          <button
            className="rounded-lg bg-brand-600 px-5 py-3 text-sm font-semibold text-white shadow active:scale-95"
            onClick={() => handleCreate(true)}
          >
            Vytvořit novou
          </button>
        </div>
      </div>
    );
  }

  if (result?.labelReady && result.labels.length > 0) {
    return <PackingLabelPrinter orderCode={orderCode} />;
  }

  if (result && !result.labelReady) {
    if (labelsQuery.data && labelsQuery.data.length > 0) {
      return <PackingLabelPrinter orderCode={orderCode} />;
    }
    return (
      <button
        className="rounded-lg border border-neutral-300 bg-white px-5 py-3 text-sm font-medium shadow"
        onClick={handleRetry}
      >
        Zkusit znovu
      </button>
    );
  }

  return (
    <button
      className="rounded-lg bg-brand-600 px-6 py-4 text-lg font-semibold text-white shadow active:scale-95"
      onClick={() => handleCreate(false)}
    >
      Vytvořit zásilku
    </button>
  );
}

export default PackingShipmentCreator;
