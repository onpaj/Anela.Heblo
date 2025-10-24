import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { SubmitStockTakingRequest, SubmitStockTakingResponse, GetStockTakingHistoryResponse, EnqueueStockTakingRequest, EnqueueStockTakingResponse, GetStockTakingJobStatusResponse } from "../generated/api-client";

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

export interface AsyncStockTakingRequest {
  productCode: string;
  targetAmount: number;
  softStockTaking?: boolean;
}

// API function to submit stock taking
const submitStockTaking = async (request: StockTakingSubmitRequest): Promise<SubmitStockTakingResponse> => {
  const apiClient = getAuthenticatedApiClient();
  
  const submitRequest = new SubmitStockTakingRequest({
    productCode: request.productCode,
    targetAmount: request.targetAmount,
    softStockTaking: request.softStockTaking ?? true, // Default to true
  });

  return await apiClient.stockTaking_SubmitStockTaking(submitRequest);
};

// React Query mutation hook for stock taking submission
export const useSubmitStockTaking = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: submitStockTaking,
    onSuccess: (data, variables) => {
      // Only update cache if stock actually changed (not soft stock taking)
      if (!variables.softStockTaking) {
        // Optimistically update catalog detail cache
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
                    available: variables.targetAmount
                  }
                }
              };
            }
            return oldData;
          }
        );
      }
      
      // Invalidate related queries to refresh data
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.catalog] 
      });
      
      // Specifically invalidate catalog detail for the updated product
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.catalog, "detail", variables.productCode] 
      });
      
      // Invalidate inventory queries
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.catalog, "inventory"] 
      });
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
    request.sortDescending ?? true // Default to newest first
  );
};

// React Query hook for stock taking history
export const useStockTakingHistory = (request: StockTakingHistoryRequest) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.stockTaking, "history", request],
    queryFn: () => getStockTakingHistory(request),
    enabled: !!request.productCode, // Only run query when productCode is available
    staleTime: 5 * 60 * 1000, // 5 minutes - history doesn't change frequently
    gcTime: 10 * 60 * 1000, // 10 minutes cache time
  });
};

// API function to enqueue async stock taking
const enqueueStockTaking = async (request: AsyncStockTakingRequest): Promise<EnqueueStockTakingResponse> => {
  const apiClient = getAuthenticatedApiClient();
  
  const enqueueRequest = new EnqueueStockTakingRequest({
    productCode: request.productCode,
    targetAmount: request.targetAmount,
    softStockTaking: request.softStockTaking ?? false, // Default to false for async
  });

  return await apiClient.catalog_EnqueueStockTaking(enqueueRequest);
};

// API function to get stock taking job status
const getStockTakingJobStatus = async (jobId: string): Promise<GetStockTakingJobStatusResponse> => {
  const apiClient = getAuthenticatedApiClient();
  return await apiClient.catalog_GetStockTakingJobStatus(jobId);
};

// React Query mutation hook for async stock taking submission
export const useEnqueueStockTaking = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: enqueueStockTaking,
    onSuccess: (data, variables) => {
      // Optimistically update catalog detail cache immediately (before job completion)
      if (!variables.softStockTaking) {
        // Update catalog detail cache
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
                    eshop: variables.targetAmount // Update eshop stock for inventory list
                  }
                }
              };
            }
            return oldData;
          }
        );

        // Update inventory list cache optimistically
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
                        eshop: variables.targetAmount
                      }
                    };
                  }
                  return item;
                })
              };
            }
            return oldData;
          }
        );
      }
      
      // Invalidate related queries to refresh data after job completion
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.catalog] 
      });
      
      // Specifically invalidate catalog detail for the updated product
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.catalog, "detail", variables.productCode] 
      });
      
      // Invalidate inventory queries
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.catalog, "inventory"] 
      });
    },
    onError: (error, variables) => {
      console.error("Async stock taking enqueue failed:", error, "for product:", variables.productCode);
    },
  });
};

// React Query hook for stock taking job status polling
export const useStockTakingJobStatus = (jobId: string | null, options?: { 
  enabled?: boolean;
  refetchInterval?: number;
}) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.stockTaking, "job-status", jobId],
    queryFn: () => getStockTakingJobStatus(jobId!),
    enabled: !!jobId && (options?.enabled ?? true),
    refetchInterval: (query) => {
      // Stop polling when job is completed (succeeded or failed)
      if (query.state.data?.isCompleted) {
        return false;
      }
      // Poll every 2 seconds while job is running
      return options?.refetchInterval ?? 2000;
    },
    retry: (failureCount, error) => {
      // Don't retry if job not found (404)
      if (error && 'status' in error && error.status === 404) {
        return false;
      }
      // Retry up to 3 times for other errors
      return failureCount < 3;
    },
    staleTime: 0, // Always fetch fresh status
    gcTime: 5 * 60 * 1000, // 5 minutes cache time for completed jobs
  });
};