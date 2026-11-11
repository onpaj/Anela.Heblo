import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  SetNdRequest,
  SetNdResponse,
} from "../generated/api-client";
import { manufacturingStockAnalysisKeys } from "./useManufacturingStockAnalysis";

export interface SetNdVariables {
  productCode: string;
  isNd: boolean;
}

// Hook to mark a product as a top product (stored as an attribute in ABRA Flexi)
export const useSetNd = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      productCode,
      isNd,
    }: SetNdVariables): Promise<SetNdResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_SetNd(
        productCode,
        new SetNdRequest({ productCode, isNd }),
      );
    },
    // Returned so the mutation stays pending until the refetch lands and the UI never shows the stale flag
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: QUERY_KEYS.catalog }),
        queryClient.invalidateQueries({
          queryKey: manufacturingStockAnalysisKeys.all,
        }),
      ]),
  });
};
