import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import type { ResetShipmentData } from '../generated/api-client';
import type { ScanShipment } from './useScanPackingOrder';

const toScanShipment = (data: ResetShipmentData): ScanShipment => ({
  shipmentGuid: data.shipmentGuid ?? '',
  packages: (data.packages ?? []).map((pkg) => ({
    trackingNumber: pkg.trackingNumber ?? null,
    labelUrl: pkg.labelUrl ?? null,
    labelZpl: pkg.labelZpl ?? null,
  })),
  // The reset endpoint always creates a fresh shipment.
  alreadyExisted: false,
  pendingCompletion: data.pendingCompletion,
});

const RESET_ERROR_MESSAGES: Partial<Record<string, string>> = {
  NoShipmentToReset: 'Žádná zásilka k invalidaci.',
  InvalidPackageCount: 'Neplatný počet balíků.',
  ShipmentCancelFailed: 'Shoptet nemohl zrušit zásilku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit novou zásilku.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
};

const GENERIC_RESET_ERROR = 'Chyba při invalidaci zásilky.';

export type ResetOrderShipmentVariables = {
  orderCode: string;
  numberOfPackages?: number;
};

const resetOrderShipment = async ({
  orderCode,
  numberOfPackages = 1,
}: ResetOrderShipmentVariables): Promise<ScanShipment> => {
  const apiClient = getAuthenticatedApiClient(false);
  const response = await apiClient.packaging_ResetShipment(orderCode, numberOfPackages);

  if (!response.success) {
    const message = (response.errorCode && RESET_ERROR_MESSAGES[response.errorCode]) ?? GENERIC_RESET_ERROR;
    throw new Error(message);
  }

  return toScanShipment(response.shipment!);
};

export const useResetOrderShipment = () =>
  useMutation<ScanShipment, Error, ResetOrderShipmentVariables>({
    mutationFn: resetOrderShipment,
  });
