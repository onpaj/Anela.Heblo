import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { SubmitStockTakingRequest, SubmitStockTakingResponse, GetStockTakingHistoryResponse } from "../generated/api-client";

export interface StockTakingSubmitRequest {
  productCode: string;
  targetAmount: number;
  softStockTaking?: boolean;
}

export interface StockTakingHistoryRequest {
  productCode?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

// API function to submit stock taking
const submitStockTaking = async (request: StockTakingSubmitRequest): Promise<SubmitStockTakingResponse> => {
  const apiClient = getAuthenticatedApiClient();

  const submitRequest = new SubmitStockTakingRequest({
    productCode: request.productCode,
    targetAmount: request.targetAmount,
    softStockTaking: request.softStockTaking ?? false,
  });

  return await apiClient.stockTaking_SubmitStockTaking(submitRequest);
};

// React Query mutation hook for stock taking submission
export const useSubmitStockTaking = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: submitStockTaking,
    onSuccess: (data, variables) => {
      if (!variables.softStockTaking) {
        queryClient.setQueryData(
          [...QUERY_KEYS.catalog, "detail", variables.productCode, 1],
          (oldData: any) => {
            if (oldData?.item?.stock) {
              return {
                ...oldData,
                item: {
                  ...oldData.item,
                  stock: {
                    ...oldData.item.stock,
                    available: variables.targetAmount,
                    eshop: variables.targetAmount,
                  },
                },
              };
            }
            return oldData;
          }
        );

        queryClient.setQueriesData(
          { queryKey: [...QUERY_KEYS.catalog, "inventory"] },
          (oldData: any) => {
            if (oldData?.items) {
              return {
                ...oldData,
                items: oldData.items.map((item: any) => {
                  if (item.productCode === variables.productCode) {
                    return {
                      ...item,
                      stock: {
                        ...item.stock,
                        available: variables.targetAmount,
                        eshop: variables.targetAmount,
                      },
                    };
                  }
                  return item;
                }),
              };
            }
            return oldData;
          }
        );
      }

      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.catalog] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.catalog, "detail", variables.productCode] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.catalog, "inventory"] });
    },
    onError: (error, variables) => {
      console.error("Stock taking submission failed:", error, "for product:", variables.productCode);
    },
  });
};

// API function to get stock taking history
const getStockTakingHistory = async (request: StockTakingHistoryRequest): Promise<GetStockTakingHistoryResponse> => {
  const apiClient = getAuthenticatedApiClient();

  return await apiClient.stockTaking_GetStockTakingHistory(
    request.productCode || undefined,
    request.pageNumber || 1,
    request.pageSize || 20,
    request.sortBy || "date",
    request.sortDescending ?? true
  );
};

// React Query hook for stock taking history
export const useStockTakingHistory = (request: StockTakingHistoryRequest) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.stockTaking, "history", request],
    queryFn: () => getStockTakingHistory(request),
    enabled: !!request.productCode,
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};
