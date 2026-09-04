import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { callApi } from "../apiErrorEnvelope";
import { getErrorMessage } from "../../utils/errorHandler";
import {
  ProductPriceDto,
  PriceSyncConflictDto,
  PriceSyncStatus,
  PriceSyncTarget,
  PriceConflictResolution,
  SetProductPriceRequest,
  SetProductPriceResponse,
  ResolvePriceSyncConflictRequest,
  ResolvePriceSyncConflictResponse,
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

const GENERIC_SET_PRICE_ERROR = "Cenu se nepodařilo uložit.";
const GENERIC_RESOLVE_CONFLICT_ERROR = "Konflikt se nepodařilo vyřešit.";
const GENERIC_TRIGGER_SYNC_ERROR = "Synchronizaci se nepodařilo spustit.";

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

  return useMutation<SetProductPriceResponse, Error, SetProductPriceInput>({
    mutationFn: ({ productCode, priceWithVat }: SetProductPriceInput) =>
      callApi(
        () =>
          getAuthenticatedApiClient().productPricing_SetPrice(
            productCode,
            new SetProductPriceRequest({ productCode, priceWithVat }),
          ),
        ({ errorCode, params }) =>
          errorCode ? getErrorMessage(errorCode, params) : GENERIC_SET_PRICE_ERROR,
      ),
    // Setting a price resets BOTH sync states to Pending, which changes the
    // conflict list as well as the price list.
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.prices });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.conflicts });
    },
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

  return useMutation<ResolvePriceSyncConflictResponse, Error, ResolvePriceConflictInput>({
    mutationFn: (input: ResolvePriceConflictInput) =>
      callApi(
        () =>
          getAuthenticatedApiClient().productPricing_ResolveConflict(
            new ResolvePriceSyncConflictRequest(input),
          ),
        ({ errorCode, params }) =>
          errorCode ? getErrorMessage(errorCode, params) : GENERIC_RESOLVE_CONFLICT_ERROR,
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
    mutationFn: () =>
      callApi(
        () => getAuthenticatedApiClient().productPricing_TriggerSync(),
        ({ errorCode, params }) =>
          errorCode ? getErrorMessage(errorCode, params) : GENERIC_TRIGGER_SYNC_ERROR,
      ),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.prices });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.conflicts });
    },
  });
};
