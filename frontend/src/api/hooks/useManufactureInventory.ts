import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { CatalogItemDto, ProductType, GetCatalogListResponse } from "./useCatalog";

// Manufacture inventory only shows these product types
const MANUFACTURE_INVENTORY_TYPES: ProductType[] = [
  ProductType.Material,
];

export interface GetManufactureInventoryListRequest {
  type?: ProductType;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  productName?: string;
  productCode?: string;
}

export interface GetManufactureInventoryListResponse {
  items: CatalogItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

// API function to fetch manufacture inventory list - uses generated API client
const fetchManufactureInventoryList = async (
  params: GetManufactureInventoryListRequest = {},
): Promise<GetManufactureInventoryListResponse> => {
  const apiClient = await getAuthenticatedApiClient();
  
  // For manufacture inventory, we primarily work with materials
  // If no specific type is selected, default to Material
  const targetType = params.type || ProductType.Material;
  
  if (!MANUFACTURE_INVENTORY_TYPES.includes(targetType)) {
    // If an unsupported type is requested, return empty results
    return {
      items: [],
      totalCount: 0,
      pageNumber: params.pageNumber || 1,
      pageSize: params.pageSize || 20,
      totalPages: 0,
    };
  }

  // Call the catalog API with the material filter
  const result = await apiClient.catalog_GetCatalogList(
    targetType,
    params.pageNumber,
    params.pageSize,
    params.sortBy || undefined,
    params.sortDescending,
    params.productName || undefined,
    params.productCode || undefined,
    undefined // searchTerm
  );

  return {
    items: result.items || [],
    totalCount: result.totalCount || 0,
    pageNumber: result.pageNumber || 1,
    pageSize: result.pageSize || 20,
    totalPages: result.totalPages || 1,
  };
};

// React Query hook for manufacture inventory
export const useManufactureInventoryQuery = (
  productNameFilter?: string,
  productCodeFilter?: string,
  productTypeFilter?: ProductType | "",
  pageNumber: number = 1,
  pageSize: number = 20,
  sortBy?: string,
  sortDescending: boolean = false,
) => {
  const params: GetManufactureInventoryListRequest = {
    pageNumber,
    pageSize,
    type: productTypeFilter !== "" ? productTypeFilter : ProductType.Material,
    sortBy,
    sortDescending,
    productName: productNameFilter || undefined,
    productCode: productCodeFilter || undefined,
  };

  return useQuery({
    queryKey: [
      ...QUERY_KEYS.catalog,
      "manufacture-inventory",
      {
        productNameFilter,
        productCodeFilter,
        productTypeFilter,
        pageNumber,
        pageSize,
        sortBy,
        sortDescending,
      },
    ],
    queryFn: () => fetchManufactureInventoryList(params),
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};