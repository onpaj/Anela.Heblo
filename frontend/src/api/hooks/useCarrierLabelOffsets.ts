import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import {
  Carriers,
  CarrierLabelOffsetDto,
  SetCarrierLabelOffsetRequest,
} from '../generated/api-client';

export { Carriers };
export type { CarrierLabelOffsetDto };

/** Largest move allowed in either direction; mirrors CarrierLabelOffset.MaxOffsetMm on the backend. */
export const MAX_LABEL_OFFSET_MM = 30;

export interface SetCarrierLabelOffsetInput {
  carrier: Carriers;
  offsetXMm: number;
  offsetYMm: number;
}

const QUERY_KEYS = {
  offsets: ['carrierLabelOffsets'] as const,
};

const getOffsets = async (): Promise<CarrierLabelOffsetDto[]> => {
  const client = getAuthenticatedApiClient();
  const response = await client.carrierLabelOffset_GetOffsets();
  return response.offsets ?? [];
};

const setOffset = async (input: SetCarrierLabelOffsetInput): Promise<void> => {
  const client = getAuthenticatedApiClient();
  await client.carrierLabelOffset_SetOffset(new SetCarrierLabelOffsetRequest(input));
};

export const useCarrierLabelOffsets = () =>
  useQuery({
    queryKey: QUERY_KEYS.offsets,
    queryFn: getOffsets,
  });

export const useSetCarrierLabelOffset = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: setOffset,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.offsets });
    },
  });
};
