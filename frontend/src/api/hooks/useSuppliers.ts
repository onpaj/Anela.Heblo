import { useState, useEffect } from "react";
import { getAuthenticatedApiClient } from "../client";
import { SupplierDto, SearchSuppliersResponse } from "../generated/api-client";

// Re-export types from generated client
export type { SupplierDto, SearchSuppliersResponse };

// Hook for searching suppliers with debouncing
export function useSupplierSearch(searchTerm: string, limit: number = 10) {
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!searchTerm || searchTerm.length < 2) {
      setSuppliers([]);
      return;
    }

    const timeoutId = setTimeout(async () => {
      setIsLoading(true);
      setError(null);

      try {
        // Call real API to search suppliers
        const apiClient = await getAuthenticatedApiClient();
        const response = await apiClient.suppliers_SearchSuppliers(
          searchTerm,
          limit,
        );

        setSuppliers(response.suppliers || []);
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Failed to search suppliers",
        );
      } finally {
        setIsLoading(false);
      }
    }, 300); // 300ms debounce

    return () => clearTimeout(timeoutId);
  }, [searchTerm, limit]);

  return { suppliers, isLoading, error };
}
