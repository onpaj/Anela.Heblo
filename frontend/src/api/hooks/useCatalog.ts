import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import { 
  CatalogItemDto, 
  ProductType, 
  StockDto, 
  PropertiesDto, 
  PriceDto, 
  EshopPriceDto, 
  ErpPriceDto, 
  CatalogManufactureRecordDto,
  CatalogHistoricalDataDto,
  CatalogSalesRecordDto,
  CatalogPurchaseRecordDto,
  CatalogConsumedRecordDto,
  GetCatalogDetailResponse
} from '../generated/api-client';

// Re-export types from generated API client
export { 
  CatalogItemDto, 
  ProductType, 
  StockDto, 
  PropertiesDto, 
  PriceDto, 
  EshopPriceDto, 
  ErpPriceDto, 
  CatalogManufactureRecordDto,
  CatalogHistoricalDataDto,
  CatalogSalesRecordDto,
  CatalogPurchaseRecordDto,
  CatalogConsumedRecordDto,
  GetCatalogDetailResponse
};

// All DTOs are now imported from generated API client - no custom interfaces needed

export interface GetCatalogListRequest {
  type?: ProductType;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  productName?: string;
  productCode?: string;
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
  if (params.productName) {
    searchParams.append('productName', params.productName);
  }
  if (params.productCode) {
    searchParams.append('productCode', params.productCode);
  }

  const relativeUrl = `/api/catalog${searchParams.toString() ? `?${searchParams.toString()}` : ''}`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
  
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
  });
  
  if (!response.ok) {
    throw new Error(`Failed to fetch catalog: ${response.status} ${response.statusText}`);
  }
  
  return response.json();
};

// API function to fetch catalog detail
const fetchCatalogDetail = async (productCode: string, monthsBack: number = 13): Promise<GetCatalogDetailResponse> => {
  const apiClient = await getAuthenticatedApiClient();
  return apiClient.catalog_GetCatalogDetail(productCode, monthsBack);
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
  pageSize: number = 20,
  sortBy?: string,
  sortDescending: boolean = false
) => {
  const params: GetCatalogListRequest = {
    pageNumber,
    pageSize,
    type: productTypeFilter !== '' ? productTypeFilter : undefined,
    sortBy,
    sortDescending,
    productName: productNameFilter || undefined,
    productCode: productCodeFilter || undefined,
  };

  return useQuery({
    queryKey: [...QUERY_KEYS.catalog, 'filtered', { 
      productNameFilter, 
      productCodeFilter, 
      productTypeFilter, 
      pageNumber, 
      pageSize,
      sortBy,
      sortDescending
    }],
    queryFn: () => fetchCatalogList(params),
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};

// Hook for catalog detail with historical data
export const useCatalogDetail = (productCode: string, monthsBack: number = 13) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.catalog, 'detail', productCode, monthsBack],
    queryFn: () => fetchCatalogDetail(productCode, monthsBack),
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
    enabled: !!productCode, // Only run query if productCode is provided
  });
};