import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetProductMarginsResponse,
  ProductMarginDto,
  ProductType,
} from "../generated/api-client";

// Re-export the generated types for convenience
export { GetProductMarginsResponse, ProductMarginDto };

export const useProductMarginsQuery = (
  productCode?: string,
  productName?: string,
  productType?: string,
  pageNumber: number = 1,
  pageSize: number = 20,
  sortBy?: string,
  sortDescending: boolean = false,
  dateFrom?: Date,
  dateTo?: Date,
) => {
  return useQuery<GetProductMarginsResponse, Error>({
    queryKey: [
      ...QUERY_KEYS.productMargins,
      productCode,
      productName,
      productType,
      pageNumber,
      pageSize,
      sortBy,
      sortDescending,
      dateFrom,
      dateTo,
    ],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();

      // Convert string productType to enum value for API call
      let productTypeEnum = null;
      if (productType === "Product") productTypeEnum = ProductType.Product;
      if (productType === "Goods") productTypeEnum = ProductType.Goods;
      if (productType === "Material") productTypeEnum = ProductType.Material;
      if (productType === "SemiProduct")
        productTypeEnum = ProductType.SemiProduct;

      return apiClient.productMargins_GetProductMargins(
        productCode || null,
        productName || null,
        productTypeEnum,
        pageNumber,
        pageSize,
        sortBy || null,
        sortDescending,
        dateFrom || null,
        dateTo || null,
      );
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};
