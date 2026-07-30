import { useState, useEffect } from "react";
import { useQuery, keepPreviousData } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { SupplierDto, SearchSuppliersResponse } from "../generated/api-client";

// Re-export types from generated client
export type { SupplierDto, SearchSuppliersResponse };

// Hook for searching suppliers with debouncing
export function useSupplierSearch(searchTerm: string, limit: number = 10) {
  const [debouncedSearchTerm, setDebouncedSearchTerm] = useState(searchTerm);

  useEffect(() => {
    const timeoutId = setTimeout(() => {
      setDebouncedSearchTerm(searchTerm);
    }, 300); // 300ms debounce

    return () => clearTimeout(timeoutId);
  }, [searchTerm]);

  const query = useQuery({
    queryKey: ["suppliers", "search", debouncedSearchTerm, limit],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.suppliers_SearchSuppliers(debouncedSearchTerm, limit);
    },
    enabled: debouncedSearchTerm.length >= 2,
    placeholderData: keepPreviousData,
  });

  const suppliers =
    searchTerm.length < 2 ? [] : query.data?.suppliers || [];
  const isLoading = searchTerm.length >= 2 && query.isFetching;
  const error = query.error
    ? query.error instanceof Error
      ? query.error.message
      : "Failed to search suppliers"
    : null;

  return { suppliers, isLoading, error };
}
