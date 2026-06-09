import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export type Cooling = 'None' | 'L1' | 'L2';

export interface PackingOrderItem {
  name: string;
  quantity: number;
  imageUrl: string | null;
  setName: string | null;
}

export interface PackingEligibility {
  isEligible: boolean;
  warningTitle: string | null;
  warningBody: string | null;
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
  name: string;
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

const SCAN_ERROR_MESSAGES: Partial<Record<string, string>> = {
  ShoptetOrderNotFound: 'Objednávka nebyla nalezena.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit pro tuto objednávku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit zásilku — zkuste znovu.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
};

const GENERIC_SCAN_ERROR = 'Chyba při skenování objednávky.';

export type ScanPackingOrderVariables = {
  orderCode: string;
  numberOfPackages?: number;
};

const scanPackingOrder = async ({
  orderCode,
  numberOfPackages = 1,
}: ScanPackingOrderVariables): Promise<ScanPackingOrderResult> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/scan?numberOfPackages=${numberOfPackages}`,
    { method: 'POST', headers: { 'Content-Type': 'application/json' } }
  );
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (!data.success) {
    const message = (data.errorCode && SCAN_ERROR_MESSAGES[data.errorCode as string]) ?? GENERIC_SCAN_ERROR;
    throw new Error(message);
  }

  return {
    order: data.order as PackingOrder,
    shipment: (data.shipment as ScanShipment) ?? null,
  };
};

export const useScanPackingOrder = () =>
  useMutation<ScanPackingOrderResult, Error, ScanPackingOrderVariables>({
    mutationFn: scanPackingOrder,
  });
