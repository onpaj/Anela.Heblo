import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { startNewTelemetryOperation } from '../../telemetry/appInsights';
import { ScanOrderBody } from '../generated/api-client';
import type { ScanOrderData, ScanShipmentData } from '../generated/api-client';

// String literal union — values must match the backend string serialization
// (same pattern as useCarrierCooling.ts's local Cooling type).
export type Cooling = 'None' | 'L1' | 'L2';

export interface PackingOrderItem {
  name: string;
  quantity: number;
  imageUrl: string | null;
  setName: string | null;
}

export interface PackingEligibility {
  isEligible: boolean;
}

export interface ShippingAddress {
  street: string | null;
  city: string | null;
  zip: string | null;
}

// Shaped to match usePackingOrder's PackingOrder so existing sub-components work unchanged
export interface PackingOrder {
  code: string;
  customerName: string;
  shippingMethodName: string;
  shippingAddress: ShippingAddress | null;
  cooling: Cooling;
  isCooled: boolean;
  customerNote: string | null;
  eshopNote: string | null;
  eligibility: PackingEligibility;
  items: PackingOrderItem[];
}

export interface ScanShipmentPackage {
  trackingNumber: string | null;
  labelUrl: string | null;
  labelZpl: string | null;
}

export interface ScanShipment {
  shipmentGuid: string;
  packages: ScanShipmentPackage[];
  alreadyExisted: boolean;
  pendingCompletion?: boolean;
}

export interface ScanPackingOrderResult {
  order: PackingOrder;
  shipment: ScanShipment | null;
}

const toPackingOrder = (data: ScanOrderData): PackingOrder => ({
  code: data.code ?? '',
  customerName: data.customerName ?? '',
  shippingMethodName: data.shippingMethodName ?? '',
  shippingAddress: data.shippingAddress
    ? {
        street: data.shippingAddress.street ?? null,
        city: data.shippingAddress.city ?? null,
        zip: data.shippingAddress.zip ?? null,
      }
    : null,
  cooling: (data.cooling as unknown as Cooling) ?? 'None',
  isCooled: data.isCooled ?? false,
  customerNote: data.customerNote ?? null,
  eshopNote: data.eshopNote ?? null,
  eligibility: { isEligible: data.eligibility?.isEligible ?? false },
  items: (data.items ?? []).map((item) => ({
    name: item.name ?? '',
    quantity: item.quantity ?? 0,
    imageUrl: item.imageUrl ?? null,
    setName: item.setName ?? null,
  })),
});

const toScanShipment = (data: ScanShipmentData): ScanShipment => ({
  shipmentGuid: data.shipmentGuid ?? '',
  packages: (data.packages ?? []).map((pkg) => ({
    trackingNumber: pkg.trackingNumber ?? null,
    labelUrl: pkg.labelUrl ?? null,
    labelZpl: pkg.labelZpl ?? null,
  })),
  alreadyExisted: data.alreadyExisted ?? false,
  pendingCompletion: data.pendingCompletion,
});

const SCAN_ERROR_MESSAGES: Partial<Record<string, string>> = {
  ShoptetOrderNotFound: 'Objednávka nebyla nalezena.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit pro tuto objednávku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit zásilku — zkuste znovu.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
  PackingUserNotEligible: 'Vybraný balič není aktivní nebo nemá oprávnění balit. Vyberte baliče znovu.',
};

const GENERIC_SCAN_ERROR = 'Chyba při skenování objednávky.';

export type ScanPackingOrderVariables = {
  orderCode: string;
  numberOfPackages?: number;
  packingUserId?: string | null;
};

const scanPackingOrder = async ({
  orderCode,
  numberOfPackages = 1,
  packingUserId = null,
}: ScanPackingOrderVariables): Promise<ScanPackingOrderResult> => {
  const apiClient = getAuthenticatedApiClient(false);
  const response = await apiClient.packaging_ScanOrder(
    orderCode,
    numberOfPackages,
    { packingUserId: packingUserId ?? undefined } as ScanOrderBody,
  );

  if (!response.success) {
    const message = (response.errorCode && SCAN_ERROR_MESSAGES[response.errorCode]) ?? GENERIC_SCAN_ERROR;
    throw new Error(message);
  }

  return {
    order: toPackingOrder(response.order!),
    shipment: response.shipment ? toScanShipment(response.shipment) : null,
  };
};

export const useScanPackingOrder = () =>
  useMutation<ScanPackingOrderResult, Error, ScanPackingOrderVariables>({
    mutationFn: scanPackingOrder,
    onMutate: () => {
      // Each scan is its own AI operation so repeated scans on the same page do
      // not collapse into a single end-to-end transaction.
      startNewTelemetryOperation('packaging-scan');
    },
  });
