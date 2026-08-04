import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { SetCarrierCoolingRequest as GeneratedSetCarrierCoolingRequest } from '../generated/api-client';
import type {
  Carriers as GeneratedCarriers,
  DeliveryHandling as GeneratedDeliveryHandling,
  Cooling as GeneratedCooling,
} from '../generated/api-client';

// String literal unions — values must match the backend string serialization
export type Carriers = 'Zasilkovna' | 'PPL' | 'GLS' | 'Osobak';
export type DeliveryHandling = 'NaRuky' | 'Box';
export type Cooling = 'None' | 'L1' | 'L2';

export interface CarrierCoolingRowDto {
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
  coolingText?: string | null;
}

export interface CarrierGroupDto {
  carrier: Carriers;
  rows: CarrierCoolingRowDto[];
}

export interface GetCarrierCoolingMatrixResponse {
  groups: CarrierGroupDto[];
}

export interface SetCarrierCoolingRequest {
  carrier: Carriers;
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
  coolingText?: string | null;
}

const QUERY_KEYS = {
  matrix: ['carrierCooling', 'matrix'] as const,
};

const getMatrix = async (): Promise<GetCarrierCoolingMatrixResponse> => {
  const client = getAuthenticatedApiClient();
  const response = await client.carrierCooling_GetMatrix();
  return {
    groups: (response.groups ?? []).map((g) => ({
      carrier: g.carrier as unknown as Carriers,
      rows: (g.rows ?? []).map((r) => ({
        deliveryHandling: r.deliveryHandling as unknown as DeliveryHandling,
        cooling: r.cooling as unknown as Cooling,
        coolingText: r.coolingText ?? null,
      })),
    })),
  };
};

const setCooling = async (request: SetCarrierCoolingRequest): Promise<void> => {
  const client = getAuthenticatedApiClient();
  await client.carrierCooling_SetCooling(
    new GeneratedSetCarrierCoolingRequest({
      carrier: request.carrier as unknown as GeneratedCarriers,
      deliveryHandling: request.deliveryHandling as unknown as GeneratedDeliveryHandling,
      cooling: request.cooling as unknown as GeneratedCooling,
      coolingText: request.coolingText ?? undefined,
    }),
  );
};

export const useCarrierCoolingMatrix = () => {
  return useQuery({
    queryKey: QUERY_KEYS.matrix,
    queryFn: getMatrix,
  });
};

export const useSetCarrierCooling = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: setCooling,

    onMutate: async (request) => {
      await queryClient.cancelQueries({ queryKey: QUERY_KEYS.matrix });
      const previousData =
        queryClient.getQueryData<GetCarrierCoolingMatrixResponse>(QUERY_KEYS.matrix);

      queryClient.setQueryData<GetCarrierCoolingMatrixResponse>(
        QUERY_KEYS.matrix,
        (old) => {
          if (!old) return old;
          return {
            ...old,
            groups: old.groups.map((group) => {
              if (group.carrier !== request.carrier) return group;
              return {
                ...group,
                rows: group.rows.map((row) =>
                  row.deliveryHandling !== request.deliveryHandling
                    ? row
                    : { ...row, cooling: request.cooling, coolingText: request.coolingText ?? null }
                ),
              };
            }),
          };
        }
      );

      return { previousData };
    },

    onError: (_err, _request, context) => {
      if (context?.previousData) {
        queryClient.setQueryData(QUERY_KEYS.matrix, context.previousData);
      }
    },

    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.matrix });
    },
  });
};
