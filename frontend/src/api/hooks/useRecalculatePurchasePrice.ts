import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import {
  RecalculatePurchasePriceRequest,
  RecalculatePurchasePriceResponse,
} from "../generated/api-client";

export const useRecalculatePurchasePrice = () => {
  return useMutation<
    RecalculatePurchasePriceResponse,
    Error,
    RecalculatePurchasePriceRequest
  >({
    mutationFn: async (request: RecalculatePurchasePriceRequest) => {
      const apiClient = await getAuthenticatedApiClient();

      try {
        return await apiClient.purchaseOrders_RecalculatePurchasePrice(request);
      } catch (error: any) {
        throw new Error(
          `Failed to recalculate purchase prices: ${error.message}`,
        );
      }
    },
  });
};
