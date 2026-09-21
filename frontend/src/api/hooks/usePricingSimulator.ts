import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetPricingBaselineResponse,
  GetPricingScenarioResponse,
  GetPricingScenariosResponse,
  IPricingEditDto,
  IPricingOverrideDto,
  PricingEditDto,
  PricingOverrideDto,
  ProductType,
  RecalculatePricingRequest,
  RecalculatePricingResponse,
  SavePricingScenarioRequest,
  SavePricingScenarioResponse,
} from "../generated/api-client";

// Re-export the generated types for convenience
export {
  GetPricingBaselineResponse,
  GetPricingScenarioResponse,
  GetPricingScenariosResponse,
  RecalculatePricingResponse,
  SavePricingScenarioResponse,
};

export interface PricingBaselineFilter {
  productCode?: string;
  productName?: string;
  productType?: ProductType;
}

export const usePricingBaselineQuery = (filter: PricingBaselineFilter = {}) => {
  const { productCode, productName, productType } = filter;

  return useQuery<GetPricingBaselineResponse, Error>({
    queryKey: [...QUERY_KEYS.pricingBaseline, productCode, productName, productType],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.pricingSimulator_GetBaseline(
        productCode ?? null,
        productName ?? null,
        productType ?? null,
      );
    },
  });
};

export interface RecalculatePricingPayload {
  productCode?: string;
  productName?: string;
  productType?: ProductType;
  overrides: IPricingOverrideDto[];
  edit?: IPricingEditDto | null;
}

export const useRecalculatePricingMutation = () => {
  return useMutation<RecalculatePricingResponse, Error, RecalculatePricingPayload>({
    mutationFn: async (payload: RecalculatePricingPayload) => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.pricingSimulator_Recalculate(
        new RecalculatePricingRequest({
          productCode: payload.productCode,
          productName: payload.productName,
          productType: payload.productType,
          overrides: payload.overrides.map((override) => new PricingOverrideDto(override)),
          edit: payload.edit ? new PricingEditDto(payload.edit) : undefined,
        }),
      );
    },
    // Deliberately does NOT invalidate QUERY_KEYS.pricingBaseline: the response already
    // carries the recalculated rows, and invalidating would refetch the untouched
    // baseline and wipe every edit the user has made.
  });
};

export const usePricingScenariosQuery = () => {
  return useQuery<GetPricingScenariosResponse, Error>({
    queryKey: QUERY_KEYS.pricingScenarios,
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.pricingScenarios_GetScenarios();
    },
  });
};

export const usePricingScenarioQuery = (id?: string) => {
  return useQuery<GetPricingScenarioResponse, Error>({
    queryKey: [...QUERY_KEYS.pricingScenarios, id],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.pricingScenarios_GetScenario(id as string);
    },
    enabled: !!id,
  });
};

export interface SavePricingScenarioPayload {
  id?: string;
  name: string;
  description?: string;
  productCode?: string;
  productName?: string;
  productType?: ProductType;
  overrides: IPricingOverrideDto[];
}

export const useSavePricingScenarioMutation = () => {
  const queryClient = useQueryClient();

  return useMutation<SavePricingScenarioResponse, Error, SavePricingScenarioPayload>({
    mutationFn: async (payload: SavePricingScenarioPayload) => {
      const apiClient = await getAuthenticatedApiClient();
      const request = new SavePricingScenarioRequest({
        id: payload.id,
        name: payload.name,
        description: payload.description,
        productCode: payload.productCode,
        productName: payload.productName,
        productType: payload.productType,
        overrides: payload.overrides.map((override) => new PricingOverrideDto(override)),
      });

      if (payload.id) {
        return apiClient.pricingScenarios_UpdateScenario(payload.id, request);
      }
      return apiClient.pricingScenarios_CreateScenario(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.pricingScenarios });
    },
  });
};

export const useDeletePricingScenarioMutation = () => {
  const queryClient = useQueryClient();

  return useMutation<void, Error, string>({
    mutationFn: async (id: string) => {
      const apiClient = await getAuthenticatedApiClient();
      await apiClient.pricingScenarios_DeleteScenario(id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.pricingScenarios });
    },
  });
};
