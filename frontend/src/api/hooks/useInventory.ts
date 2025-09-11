import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { CatalogItemDto, ProductType } from "./useCatalog";

// Inventory only shows these product types
const INVENTORY_TYPES: ProductType[] = [
  ProductType.Product,
  ProductType.Goods,
  ProductType.Set,
];

export interface GetInventoryListRequest {
  type?: ProductType;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  productName?: string;
  productCode?: string;
}

export interface GetInventoryListResponse {
  items: CatalogItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

// API function to fetch inventory list - uses same endpoint as catalog but with inventory logic
const fetchInventoryList = async (
  params: GetInventoryListRequest = {},
): Promise<GetInventoryListResponse> => {
  const apiClient = getAuthenticatedApiClient();
  
  // If no specific type is selected, we need to fetch all inventory types
  if (!params.type) {
    // For "all types", we'll make multiple calls and combine results
    // This is a simplified approach - in production you might want to modify the backend
    const allResults: CatalogItemDto[] = [];
    
    for (const inventoryType of INVENTORY_TYPES) {
      const searchParams = new URLSearchParams();
      
      searchParams.append("type", inventoryType.toString());
      
      if (params.pageNumber !== undefined) {
        searchParams.append("pageNumber", "1"); // Get all pages for aggregation
      }
      if (params.pageSize !== undefined) {
        searchParams.append("pageSize", "1000"); // Large page size to get all items
      }
      if (params.sortBy) {
        searchParams.append("sortBy", params.sortBy);
      }
      if (params.sortDescending !== undefined) {
        searchParams.append("sortDescending", params.sortDescending.toString());
      }
      if (params.productName) {
        searchParams.append("productName", params.productName);
      }
      if (params.productCode) {
        searchParams.append("productCode", params.productCode);
      }

      const relativeUrl = `/api/catalog${searchParams.toString() ? `?${searchParams.toString()}` : ""}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
      });

      if (!response.ok) {
        throw new Error(
          `Failed to fetch inventory: ${response.status} ${response.statusText}`,
        );
      }
      
      const result = await response.json();
      allResults.push(...result.items);
    }
    
    // Apply client-side pagination to combined results
    const pageNumber = params.pageNumber || 1;
    const pageSize = params.pageSize || 20;
    const startIndex = (pageNumber - 1) * pageSize;
    const endIndex = startIndex + pageSize;
    
    // Sort combined results if needed
    if (params.sortBy) {
      allResults.sort((a, b) => {
        const aValue = (a as any)[params.sortBy!];
        const bValue = (b as any)[params.sortBy!];
        
        let comparison = 0;
        if (aValue > bValue) comparison = 1;
        if (aValue < bValue) comparison = -1;
        
        return params.sortDescending ? -comparison : comparison;
      });
    }
    
    const paginatedItems = allResults.slice(startIndex, endIndex);
    
    return {
      items: paginatedItems,
      totalCount: allResults.length,
      pageNumber,
      pageSize,
      totalPages: Math.ceil(allResults.length / pageSize),
    };
  } else {
    // Single type - use regular API call
    const searchParams = new URLSearchParams();
    
    searchParams.append("type", params.type.toString());
    
    if (params.pageNumber !== undefined) {
      searchParams.append("pageNumber", params.pageNumber.toString());
    }
    if (params.pageSize !== undefined) {
      searchParams.append("pageSize", params.pageSize.toString());
    }
    if (params.sortBy) {
      searchParams.append("sortBy", params.sortBy);
    }
    if (params.sortDescending !== undefined) {
      searchParams.append("sortDescending", params.sortDescending.toString());
    }
    if (params.productName) {
      searchParams.append("productName", params.productName);
    }
    if (params.productCode) {
      searchParams.append("productCode", params.productCode);
    }

    const relativeUrl = `/api/catalog${searchParams.toString() ? `?${searchParams.toString()}` : ""}`;
    const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

    const response = await (apiClient as any).http.fetch(fullUrl, {
      method: "GET",
    });

    if (!response.ok) {
      throw new Error(
        `Failed to fetch inventory: ${response.status} ${response.statusText}`,
      );
    }

    return response.json();
  }
};

// React Query hook for inventory
export const useInventoryQuery = (
  productNameFilter?: string,
  productCodeFilter?: string,
  productTypeFilter?: ProductType | "",
  pageNumber: number = 1,
  pageSize: number = 20,
  sortBy?: string,
  sortDescending: boolean = false,
) => {
  const params: GetInventoryListRequest = {
    pageNumber,
    pageSize,
    type: productTypeFilter !== "" ? productTypeFilter : undefined,
    sortBy,
    sortDescending,
    productName: productNameFilter || undefined,
    productCode: productCodeFilter || undefined,
  };

  return useQuery({
    queryKey: [
      ...QUERY_KEYS.catalog,
      "inventory",
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
    queryFn: () => fetchInventoryList(params),
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};