import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

const COMPLETE_ERROR_MESSAGES: Partial<Record<string, string>> = {
  PackingCompletionFailed: 'Nepodařilo se dokončit balení objednávky.',
};

const GENERIC_COMPLETE_ERROR = 'Chyba při dokončení balení.';

export const completePackingOrder = async (orderCode: string): Promise<void> => {
  const apiClient = getAuthenticatedApiClient(false);
  const response = await apiClient.packaging_CompletePacking(orderCode);

  if (!response.success) {
    const message = (response.errorCode && COMPLETE_ERROR_MESSAGES[response.errorCode]) ?? GENERIC_COMPLETE_ERROR;
    throw new Error(message);
  }
};

export const useCompletePackingOrder = () =>
  useMutation<void, Error, string>({
    mutationFn: completePackingOrder,
  });
