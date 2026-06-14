import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const COMPLETE_ERROR_MESSAGES: Partial<Record<string, string>> = {
  PackingCompletionFailed: 'Nepodařilo se dokončit balení objednávky.',
};

const GENERIC_COMPLETE_ERROR = 'Chyba při dokončení balení.';

export const completePackingOrder = async (orderCode: string): Promise<void> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/packing/complete`,
    { method: 'POST' }
  );
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (!data.success) {
    const message = (data.errorCode && COMPLETE_ERROR_MESSAGES[data.errorCode as string]) ?? GENERIC_COMPLETE_ERROR;
    throw new Error(message);
  }
};

export const useCompletePackingOrder = () =>
  useMutation<void, Error, string>({
    mutationFn: completePackingOrder,
  });
