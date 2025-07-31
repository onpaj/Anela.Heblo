import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

// Types matching backend DTOs
export interface StockDto {
  eshop: number;
  erp: number;
  transport: number;
  reserve: number;
  available: number;
}

export interface PropertiesDto {
  optimalStockDaysSetup: number;
  stockMinSetup: number;
  batchSize: number;
  seasonMonths: number[];
}

export enum ProductType {
  Product = 8,
  Goods = 1,
  Material = 3,
  SemiProduct = 7,
  UNDEFINED = 0,
}

export interface CatalogItemDto {
  productCode: string;
  productName: string;
  type: ProductType;
  stock: StockDto;
  properties: PropertiesDto;
  location: string;
  minimalOrderQuantity: string;
  minimalManufactureQuantity: number;
}

export interface GetCatalogListRequest {
  type?: ProductType;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

export interface GetCatalogListResponse {
  items: CatalogItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

// API function to fetch catalog list
const fetchCatalogList = async (params: GetCatalogListRequest = {}): Promise<GetCatalogListResponse> => {
  const apiClient = getAuthenticatedApiClient();
  const searchParams = new URLSearchParams();
  
  if (params.type !== undefined) {
    searchParams.append('type', params.type.toString());
  }
  if (params.pageNumber !== undefined) {
    searchParams.append('pageNumber', params.pageNumber.toString());
  }
  if (params.pageSize !== undefined) {
    searchParams.append('pageSize', params.pageSize.toString());
  }
  if (params.sortBy) {
    searchParams.append('sortBy', params.sortBy);
  }
  if (params.sortDescending !== undefined) {
    searchParams.append('sortDescending', params.sortDescending.toString());
  }

  const url = `/api/catalog${searchParams.toString() ? `?${searchParams.toString()}` : ''}`;
  const headers = await (apiClient as any).getAuthHeaders();
  
  const response = await fetch(`${(apiClient as any).baseUrl}${url}`, {
    method: 'GET',
    headers,
  });
  
  if (!response.ok) {
    throw new Error(`Failed to fetch catalog: ${response.status} ${response.statusText}`);
  }
  
  return response.json();
};

// React Query hook
export const useCatalogList = (params: GetCatalogListRequest = {}) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.catalog, 'list', params],
    queryFn: () => fetchCatalogList(params),
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes
  });
};

// Hook for filtered and paginated catalog data
export const useCatalogQuery = (
  productNameFilter?: string,
  productCodeFilter?: string,
  productTypeFilter?: ProductType | '',
  pageNumber: number = 1,
  pageSize: number = 20
) => {
  const params: GetCatalogListRequest = {
    pageNumber,
    pageSize,
    type: productTypeFilter !== '' ? productTypeFilter : undefined,
  };

  return useQuery({
    queryKey: [...QUERY_KEYS.catalog, 'filtered', { 
      productNameFilter, 
      productCodeFilter, 
      productTypeFilter, 
      pageNumber, 
      pageSize 
    }],
    queryFn: async () => {
      const response = await fetchCatalogList(params);
      
      // Apply client-side filtering for name and code since the backend doesn't support these filters yet
      let filteredItems = response.items;
      
      if (productNameFilter) {
        filteredItems = filteredItems.filter(item =>
          item.productName.toLowerCase().includes(productNameFilter.toLowerCase())
        );
      }
      
      if (productCodeFilter) {
        filteredItems = filteredItems.filter(item =>
          item.productCode.toLowerCase().includes(productCodeFilter.toLowerCase())
        );
      }
      
      return {
        ...response,
        items: filteredItems,
        totalCount: filteredItems.length,
      };
    },
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};