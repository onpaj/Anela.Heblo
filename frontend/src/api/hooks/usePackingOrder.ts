import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

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

export interface PackingOrder {
  code: string;
  customerName: string;
  shippingMethodName: string;
  cooling: Cooling;
  isCooled: boolean;
  customerNote: string | null;
  eshopNote: string | null;
  eligibility: PackingEligibility;
  items: PackingOrderItem[];
}

/** Thrown when the scanned order code does not exist in Shoptet. */
export class PackingOrderNotFoundError extends Error {
  constructor(code: string) {
    super(`Order not found: ${code}`);
    this.name = 'PackingOrderNotFoundError';
  }
}

const fetchPackingOrder = async (code: string): Promise<PackingOrder> => {
  const apiClient = getAuthenticatedApiClient(false);
  const fullUrl = `${(apiClient as any).baseUrl}/api/shoptet-orders/${encodeURIComponent(code)}/packing`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });

  if (response.status === 404) {
    throw new PackingOrderNotFoundError(code);
  }
  if (!response.ok) {
    throw new Error(`Failed to load packing order: ${response.status}`);
  }
  return response.json();
};

/** Loads a packing order by scanned code. Disabled until a code is provided. */
export const usePackingOrder = (code: string | null) =>
  useQuery({
    queryKey: [...QUERY_KEYS.packingOrder, code],
    queryFn: () => fetchPackingOrder(code as string),
    enabled: !!code,
    retry: false,
    gcTime: 0,
  });
