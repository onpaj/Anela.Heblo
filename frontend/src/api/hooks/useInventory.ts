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

// API function to fetch inventory list - uses generated API client
const fetchInventoryList = async (
  params: GetInventoryListRequest = {},
): Promise<GetInventoryListResponse> => {
  const apiClient = await getAuthenticatedApiClient();
  
  // If no specific type is selected, we need to fetch all inventory types
  if (!params.type) {
    // For "all types", we'll make multiple calls and combine results
    // This is a simplified approach - in production you might want to modify the backend
    const allResults: CatalogItemDto[] = [];
    
    for (const inventoryType of INVENTORY_TYPES) {
      const result = await apiClient.catalog_GetCatalogList(
        inventoryType,
        1, // pageNumber - get all pages for aggregation
        1000, // pageSize - large page size to get all items
        params.sortBy || undefined,
        params.sortDescending,
        params.productName || undefined,
        params.productCode || undefined,
        undefined, // searchTerm
        false // withBoMOnly - show all products in inventory
      );
      
      if (result.items) {
        allResults.push(...result.items);
      }
    }
    
    // Apply client-side pagination to combined results
    const pageNumber = params.pageNumber || 1;
    const pageSize = params.pageSize || 20;
    const startIndex = (pageNumber - 1) * pageSize;
    const endIndex = startIndex + pageSize;
    
    // Sort combined results if needed
    if (params.sortBy) {
      if (params.sortBy.toLowerCase() === 'lastinventorydays') {
        // Special sorting for lastInventoryDays
        allResults.sort((a, b) => {
          const aHasDate = !!a.lastStockTaking;
          const bHasDate = !!b.lastStockTaking;
          
          if (params.sortDescending) {
            // Descending: Items WITHOUT inventory first, then items with inventory by oldest first (biggest days)
            if (!aHasDate && !bHasDate) {
              // Both don't have dates, sort by location
              return (a.location || '').localeCompare(b.location || '');
            }
            if (!aHasDate) return -1; // a (no date) comes first
            if (!bHasDate) return 1;  // b (no date) comes first
            
            // Both have dates, sort by oldest first (ascending = biggest days)
            const aDate = new Date(a.lastStockTaking!).getTime();
            const bDate = new Date(b.lastStockTaking!).getTime();
            return aDate - bDate; // ascending = oldest first = biggest days
          } else {
            // Ascending: Items WITH inventory first by newest first (smallest days), then items WITHOUT inventory
            if (!aHasDate && !bHasDate) {
              // Both don't have dates, sort by location
              return (a.location || '').localeCompare(b.location || '');
            }
            if (!aHasDate) return 1;  // a (no date) comes last
            if (!bHasDate) return -1; // b (no date) comes last
            
            // Both have dates, sort by newest first (descending = smallest days)
            const aDate = new Date(a.lastStockTaking!).getTime();
            const bDate = new Date(b.lastStockTaking!).getTime();
            return bDate - aDate; // descending = newest first = smallest days
          }
        });
      } else {
        // Standard sorting for other fields
        allResults.sort((a, b) => {
          const aValue = (a as any)[params.sortBy!];
          const bValue = (b as any)[params.sortBy!];
          
          let comparison = 0;
          if (aValue > bValue) comparison = 1;
          if (aValue < bValue) comparison = -1;
          
          return params.sortDescending ? -comparison : comparison;
        });
      }
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
    // Single type - use generated API client
    const result = await apiClient.catalog_GetCatalogList(
      params.type,
      params.pageNumber,
      params.pageSize,
      params.sortBy || undefined,
      params.sortDescending,
      params.productName || undefined,
      params.productCode || undefined,
      undefined, // searchTerm
      false // withBoMOnly - show all products in inventory
    );

    return {
      items: result.items || [],
      totalCount: result.totalCount || 0,
      pageNumber: result.pageNumber || 1,
      pageSize: result.pageSize || 20,
      totalPages: result.totalPages || 1,
    };
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