import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import {
  ProductPriceDto,
  PriceSyncConflictDto,
  PriceSyncStatus,
  PriceSyncTarget,
  PriceConflictResolution,
  SetProductPriceRequest,
  ResolvePriceSyncConflictRequest,
} from "../generated/api-client";

export {
  ProductPriceDto,
  PriceSyncConflictDto,
  PriceSyncStatus,
  PriceSyncTarget,
  PriceConflictResolution,
};

const QUERY_KEYS = {
  prices: ["product-pricing", "prices"] as const,
  conflicts: ["product-pricing", "conflicts"] as const,
};

export interface SetProductPriceInput {
  productCode: string;
  priceWithVat: number;
}

export interface ResolvePriceConflictInput {
  productCode: string;
  target: PriceSyncTarget;
  resolution: PriceConflictResolution;
}

export const useProductPrices = () =>
  useQuery({
    queryKey: QUERY_KEYS.prices,
    queryFn: async () => {
      const response = await getAuthenticatedApiClient().productPricing_GetPrices();
      return response.prices ?? [];
    },
  });

export const useSetProductPrice = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ productCode, priceWithVat }: SetProductPriceInput) =>
      getAuthenticatedApiClient().productPricing_SetPrice(
        productCode,
        new SetProductPriceRequest({ productCode, priceWithVat }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: QUERY_KEYS.prices }),
  });
};

export const usePriceSyncConflicts = () =>
  useQuery({
    queryKey: QUERY_KEYS.conflicts,
    queryFn: async () => {
      const response = await getAuthenticatedApiClient().productPricing_GetConflicts();
      return response.conflicts ?? [];
    },
  });

export const useResolvePriceConflict = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: ResolvePriceConflictInput) =>
      getAuthenticatedApiClient().productPricing_ResolveConflict(
        new ResolvePriceSyncConflictRequest(input),
      ),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.prices });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.conflicts });
    },
  });
};

export const useTriggerPriceSync = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => getAuthenticatedApiClient().productPricing_TriggerSync(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.prices });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.conflicts });
    },
  });
};
