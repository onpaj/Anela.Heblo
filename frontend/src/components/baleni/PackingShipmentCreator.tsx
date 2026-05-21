import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../../api/client';
import { ErrorCodes, ShipmentLabelDto } from '../../api/generated/api-client';
import PackingLabelPrinter from './PackingLabelPrinter';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

interface PrepareOrderLabelInput {
  orderCode: string;
  forceRecreate: boolean;
}

interface PrepareOrderLabelResult {
  existingShipmentFound: boolean;
  labelReady: boolean;
  labels: ShipmentLabelDto[];
}

const MESSAGES: Partial<Record<string, string>> = {
  [ErrorCodes.OrderNotInPackingState]: 'Objednávka není ve stavu Balí se — zásilku nelze vytvořit',
  [ErrorCodes.ShipmentCarrierNotResolved]: 'Dopravce se nepodařilo určit pro tuto objednávku',
  [ErrorCodes.ShipmentCreationFailed]: 'Shoptet nemohl vytvořit zásilku — zkuste znovu',
  [ErrorCodes.ShipmentOrderWeightUnavailable]: 'Nelze zjistit hmotnost objednávky',
};

const GENERIC_ERROR = 'Zásilku se nepodařilo vytvořit';

const prepareOrderLabel = async (input: PrepareOrderLabelInput): Promise<PrepareOrderLabelResult> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(input.orderCode)}/label`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ forceRecreate: input.forceRecreate }),
    }
  );

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (!data.success) {
    const message = (data.errorCode && MESSAGES[data.errorCode as string]) ?? GENERIC_ERROR;
    throw new Error(message);
  }

  return {
    existingShipmentFound: (data.existingShipmentFound as boolean) ?? false,
    labelReady: (data.labelReady as boolean) ?? false,
    labels: (data.labels as ShipmentLabelDto[]) ?? [],
  };
};

const usePrepareOrderLabel = () =>
  useMutation<PrepareOrderLabelResult, Error, PrepareOrderLabelInput>({
    mutationFn: prepareOrderLabel,
  });

interface PackingShipmentCreatorProps {
  orderCode: string;
}

function PackingShipmentCreator({ orderCode }: PackingShipmentCreatorProps) {
  const mutation = usePrepareOrderLabel();
  const [useExisting, setUseExisting] = useState(false);
  const [isRetrying, setIsRetrying] = useState(false);

  const handleCreate = (forceRecreate: boolean) => {
    setUseExisting(false);
    setIsRetrying(false);
    mutation.mutate({ orderCode, forceRecreate });
  };

  const handleRetry = () => {
    setIsRetrying(true);
    setUseExisting(false);
    mutation.mutate({ orderCode, forceRecreate: false });
  };

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

  const result = mutation.data;

  if (result?.existingShipmentFound) {
    if (useExisting || isRetrying) {
      return <PackingLabelPrinter orderCode={orderCode} labels={result.labels} />;
    }
    return (
      <div className="flex flex-col gap-3">
        <p className="text-sm font-semibold text-amber-700">
          Zásilka již existuje pro tuto objednávku.
        </p>
        <div className="flex gap-3">
          <button
            className="rounded-lg border border-neutral-300 bg-white px-5 py-3 text-sm font-medium shadow"
            onClick={() => setUseExisting(true)}
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
    return <PackingLabelPrinter orderCode={orderCode} labels={result.labels} />;
  }

  if (result && !result.labelReady) {
    if (result.labels.length > 0) {
      return <PackingLabelPrinter orderCode={orderCode} labels={result.labels} />;
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
