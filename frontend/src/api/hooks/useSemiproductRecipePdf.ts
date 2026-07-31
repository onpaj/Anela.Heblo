import { useState } from "react";
import { getAuthenticatedApiClient } from "../client";

export const useSemiproductRecipePdf = () => {
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const openRecipePdf = async (productCode: string, batchSize?: number) => {
    setIsLoading(true);
    setError(null);
    try {
      const apiClient = getAuthenticatedApiClient();
      const response = await apiClient.manufactureBatch_GetRecipePdf(
        productCode,
        batchSize,
      );
      const blobUrl = URL.createObjectURL(response.data);
      window.open(blobUrl, '_blank', 'noopener,noreferrer');
      setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
    } catch (err) {
      const error = err instanceof Error ? err : new Error(String(err));
      setError(error);
    } finally {
      setIsLoading(false);
    }
  };

  return { openRecipePdf, isLoading, error };
};
