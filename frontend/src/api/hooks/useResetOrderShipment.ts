import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import type { ScanShipment } from './useScanPackingOrder';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const RESET_ERROR_MESSAGES: Partial<Record<string, string>> = {
  NoShipmentToReset: 'Žádná zásilka k invalidaci.',
  ShipmentCancelFailed: 'Shoptet nemohl zrušit zásilku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit novou zásilku.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
};

const GENERIC_RESET_ERROR = 'Chyba při invalidaci zásilky.';

const resetOrderShipment = async (orderCode: string): Promise<ScanShipment> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/shipment/reset`,
    { method: 'POST' }
  );
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (!data.success) {
    const message = (data.errorCode && RESET_ERROR_MESSAGES[data.errorCode as string]) ?? GENERIC_RESET_ERROR;
    throw new Error(message);
  }

  return data.shipment as ScanShipment;
};

export const useResetOrderShipment = () =>
  useMutation<ScanShipment, Error, string>({
    mutationFn: resetOrderShipment,
  });
