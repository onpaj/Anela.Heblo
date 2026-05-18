import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

// String literal unions — values must match the backend string serialization
export type Carriers = 'Zasilkovna' | 'PPL' | 'GLS' | 'Osobak';
export type DeliveryHandling = 'NaRuky' | 'Box';
export type Cooling = 'None' | 'L1' | 'L2';

export interface CarrierCoolingRowDto {
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
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
}

const QUERY_KEYS = {
  matrix: ['carrierCooling', 'matrix'] as const,
};

const getMatrix = async (): Promise<GetCarrierCoolingMatrixResponse> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/carrier-cooling`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`Failed to fetch cooling matrix: ${response.status}`);
  }
  return response.json();
};

const setCooling = async (request: SetCarrierCoolingRequest): Promise<void> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/carrier-cooling`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    throw new Error(`Failed to set cooling: ${response.status}`);
  }
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
                    : { ...row, cooling: request.cooling }
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
