import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export interface GetMarketingCostsRequest {
  platform?: string;
  dateFrom?: string;
  dateTo?: string;
  isSynced?: boolean | null;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

export interface MarketingCostListItemDto {
  id: number;
  transactionId: string;
  platform: string;
  amount: number;
  currency: string | null;
  transactionDate: string;
  importedAt: string;
  isSynced: boolean;
}

export interface GetMarketingCostsListResponse {
  items: MarketingCostListItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  success: boolean;
}

export interface MarketingCostDetailDto {
  id: number;
  transactionId: string;
  platform: string;
  amount: number;
  currency: string | null;
  transactionDate: string;
  importedAt: string;
  isSynced: boolean;
  description: string | null;
  errorMessage: string | null;
  rawData: string | null;
}

export interface GetMarketingCostDetailResponse {
  item: MarketingCostDetailDto | null;
  success: boolean;
}

export const marketingCostsKeys = {
  all: ["marketing-costs"] as const,
  lists: () => [...marketingCostsKeys.all, "list"] as const,
  list: (filters: GetMarketingCostsRequest) =>
    [...marketingCostsKeys.lists(), filters] as const,
  details: () => [...marketingCostsKeys.all, "detail"] as const,
  detail: (id: number) => [...marketingCostsKeys.details(), id] as const,
};

export const useMarketingCostsQuery = (request: GetMarketingCostsRequest) => {
  return useQuery({
    queryKey: marketingCostsKeys.list(request),
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/marketing-costs`;
      const params = new URLSearchParams();

      if (request.platform) params.append("Platform", request.platform);
      if (request.dateFrom) params.append("DateFrom", request.dateFrom);
      if (request.dateTo) params.append("DateTo", request.dateTo);
      if (request.isSynced !== null && request.isSynced !== undefined)
        params.append("IsSynced", request.isSynced.toString());
      if (request.pageNumber !== undefined)
        params.append("PageNumber", request.pageNumber.toString());
      if (request.pageSize !== undefined)
        params.append("PageSize", request.pageSize.toString());
      if (request.sortBy) params.append("SortBy", request.sortBy);
      if (request.sortDescending !== undefined)
        params.append("SortDescending", request.sortDescending.toString());

      const queryString = params.toString();
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}${queryString ? `?${queryString}` : ""}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: { Accept: "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetMarketingCostsListResponse>;
    },
    staleTime: 1000 * 60 * 5,
  });
};

export const useMarketingCostDetailQuery = (id: number | null) => {
  return useQuery({
    queryKey: marketingCostsKeys.detail(id!),
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/marketing-costs/${id}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: { Accept: "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetMarketingCostDetailResponse>;
    },
    enabled: id !== null,
    staleTime: 1000 * 60 * 5,
  });
};
