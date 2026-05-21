import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { ErrorCodes, ShipmentLabelDto } from '../generated/api-client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export interface PrepareOrderLabelInput {
  orderCode: string;
  forceRecreate: boolean;
}

export interface PrepareOrderLabelResult {
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

export const usePrepareOrderLabel = () =>
  useMutation<PrepareOrderLabelResult, Error, PrepareOrderLabelInput>({
    mutationFn: prepareOrderLabel,
  });
