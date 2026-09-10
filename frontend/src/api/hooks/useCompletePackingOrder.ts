import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { callApi } from '../apiErrorEnvelope';

const COMPLETE_ERROR_MESSAGES: Partial<Record<string, string>> = {
  PackingCompletionFailed: 'Nepodařilo se dokončit balení objednávky.',
};

const GENERIC_COMPLETE_ERROR = 'Chyba při dokončení balení.';

export const completePackingOrder = async (orderCode: string): Promise<void> => {
  const apiClient = getAuthenticatedApiClient(false);
  await callApi(
    () => apiClient.packaging_CompletePacking(orderCode),
    ({ errorCode }) => (errorCode && COMPLETE_ERROR_MESSAGES[errorCode]) ?? GENERIC_COMPLETE_ERROR,
  );
};

export const useCompletePackingOrder = () =>
  useMutation<void, Error, string>({
    mutationFn: completePackingOrder,
  });
