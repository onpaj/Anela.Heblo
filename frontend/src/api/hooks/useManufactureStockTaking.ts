import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { useToast } from "../../contexts/ToastContext";
import { toDateOnlyString, parseLocalDate } from "../../utils/dateUtils";
import { 
  SubmitManufactureStockTakingRequest,
  SubmitManufactureStockTakingResponse,
  GetStockTakingHistoryResponse,
  ManufactureStockTakingLotDto
} from "../generated/api-client";

// Custom interfaces for request handling
export interface GetStockTakingHistoryRequest {
  productCode?: string;
  pageNumber?: number;
  pageSize?: number;
}

// Re-export types from generated client for convenience
export type { SubmitManufactureStockTakingRequest, SubmitManufactureStockTakingResponse };

export interface StockTakingHistoryRecord {
  id?: string;
  type?: string;
  code?: string;
  amountNew: number;
  amountOld: number;
  difference: number;
  date: string;
  user?: string;
}

export interface ManufactureStockTakingLotRequest {
  lotCode?: string | null;
  expiration?: Date | null;
  amount: number;
  softStockTaking?: boolean;
}

// API function to submit manufacture stock taking
const submitManufactureStockTaking = async (
  request: { 
    productCode: string; 
    targetAmount?: number; 
    softStockTaking?: boolean;
    lots?: ManufactureStockTakingLotRequest[];
  }
): Promise<SubmitManufactureStockTakingResponse> => {
  const apiClient = await getAuthenticatedApiClient();
  
  const apiRequest = new SubmitManufactureStockTakingRequest({
    productCode: request.productCode,
    targetAmount: request.targetAmount,
    softStockTaking: request.softStockTaking ?? true,
    lots: request.lots?.map(lot => new ManufactureStockTakingLotDto({
      lotCode: lot.lotCode || undefined,
      expiration: lot.expiration ? parseLocalDate(toDateOnlyString(lot.expiration)!) : undefined,
      amount: lot.amount,
      softStockTaking: lot.softStockTaking ?? true,
    })),
  });
  
  return await apiClient.manufactureStockTaking_SubmitManufactureStockTaking(apiRequest);
};

// API function to get stock taking history
const getStockTakingHistory = async (
  request: GetStockTakingHistoryRequest
): Promise<GetStockTakingHistoryResponse> => {
  const apiClient = await getAuthenticatedApiClient();
  
  return await apiClient.manufactureStockTaking_GetManufactureStockTakingHistory(
    request.productCode,
    request.pageNumber,
    request.pageSize,
    undefined, // sortBy
    undefined  // sortDescending
  );
};

// React Query mutation hook for submitting manufacture stock taking
export const useSubmitManufactureStockTaking = () => {
  const queryClient = useQueryClient();
  const { showError } = useToast();

  return useMutation({
    mutationFn: submitManufactureStockTaking,
    onSuccess: (data, variables) => {
      // Invalidate and refetch related queries
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.catalog, "manufacture-inventory"] 
      });
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.catalog, variables.productCode] 
      });
      queryClient.invalidateQueries({ 
        queryKey: [...QUERY_KEYS.stockTaking, "history", variables.productCode] 
      });
    },
    onError: (error: Error) => {
      showError(
        "Chyba při inventarizaci materiálu",
        error.message || "Inventarizace se nezdařila. Zkuste to prosím znovu.",
        { duration: 5000 }
      );
    },
  });
};

// React Query hook for stock taking history
export const useStockTakingHistory = (request: GetStockTakingHistoryRequest) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.stockTaking, "history", request.productCode, request.pageNumber, request.pageSize],
    queryFn: () => getStockTakingHistory(request),
    enabled: !!request.productCode, // Only fetch if productCode is provided
    staleTime: 2 * 60 * 1000, // 2 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes
  });
};