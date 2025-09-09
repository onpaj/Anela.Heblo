import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { CatalogItemDto, ProductType } from "../generated/api-client";

export function useCatalogAutocomplete(
  searchTerm?: string,
  limit = 20,
  productTypes?: ProductType[],
) {
  return useQuery({
    queryKey: [
      ...QUERY_KEYS.catalog,
      "autocomplete",
      searchTerm,
      limit,
      productTypes,
    ],
    queryFn: async () => {
      if (!searchTerm || searchTerm.length < 2) {
        return { items: [] as CatalogItemDto[] };
      }

      const apiClient = getAuthenticatedApiClient();

      // Use the generated API client method with product types filter
      const response = await apiClient.catalog_GetProductsForAutocomplete(
        searchTerm,
        limit,
        productTypes,
      );

      return response; // Return the full response with CatalogItemDto items
    },
    enabled: Boolean(searchTerm && searchTerm.length >= 2),
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
}
