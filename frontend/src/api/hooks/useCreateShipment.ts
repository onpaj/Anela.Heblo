import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { ErrorCodes, ShipmentLabelDto } from '../generated/api-client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export interface CreateShipmentInput {
  orderCode: string;
  forceCreate: boolean;
}

export interface CreateShipmentResult {
  shipmentGuid?: string;
  labelReady: boolean;
  labels: ShipmentLabelDto[];
  existingShipmentFound: boolean;
}

const MESSAGES: Partial<Record<string, string>> = {
  [ErrorCodes.ShipmentAlreadyExists]: 'Zásilka pro tuto objednávku již existuje',
  [ErrorCodes.ShipmentCarrierNotResolved]: 'Dopravce se nepodařilo určit pro tuto objednávku',
  [ErrorCodes.ShipmentCreationFailed]: 'Shoptet nemohl vytvořit zásilku — zkuste znovu',
  [ErrorCodes.ShipmentOrderWeightUnavailable]: 'Nelze zjistit hmotnost objednávky',
};

const GENERIC_ERROR = 'Zásilku se nepodařilo vytvořit';

const createShipment = async (input: CreateShipmentInput): Promise<CreateShipmentResult> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/shipment-labels/create`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ orderCode: input.orderCode, forceCreate: input.forceCreate }),
    }
  );

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (data.errorCode === ErrorCodes.ShipmentAlreadyExists) {
    return {
      labelReady: (data.labels?.length ?? 0) > 0,
      labels: data.labels ?? [],
      existingShipmentFound: true,
    };
  }

  if (!data.success) {
    const message = (data.errorCode && MESSAGES[data.errorCode as string]) ?? GENERIC_ERROR;
    throw new Error(message);
  }

  return {
    shipmentGuid: data.shipmentGuid as string | undefined,
    labelReady: (data.labelReady as boolean) ?? false,
    labels: (data.labels as ShipmentLabelDto[]) ?? [],
    existingShipmentFound: false,
  };
};

export const useCreateShipment = () =>
  useMutation<CreateShipmentResult, Error, CreateShipmentInput>({
    mutationFn: createShipment,
  });
