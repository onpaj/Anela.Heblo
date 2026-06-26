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
      const query = batchSize != null ? `?batchSize=${batchSize}` : '';
      const relativeUrl = `/api/manufacture-batch/recipe-pdf/${productCode}${query}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      const blob = await response.blob();
      const blobUrl = URL.createObjectURL(blob);
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
