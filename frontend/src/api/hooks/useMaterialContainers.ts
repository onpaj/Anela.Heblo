import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  CreateMaterialContainersRequest,
  CreateMaterialContainersResponse,
  GetMaterialContainerByCodeResponse,
  GetLastUsedLotForMaterialResponse,
  CreateMaterialContainerItem,
} from '../generated/api-client';

export const useCreateMaterialContainers = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { items: CreateMaterialContainerItem[] }): Promise<CreateMaterialContainersResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const request = new CreateMaterialContainersRequest({ items: input.items });
      return apiClient.materialContainers_Create(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.materialContainers });
    },
  });
};

export const useMaterialContainerByCode = (code: string | null) =>
  useQuery({
    enabled: !!code,
    queryKey: ['materialContainers', 'by-code', code],
    queryFn: (): Promise<GetMaterialContainerByCodeResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.materialContainers_GetByCode(code!);
    },
  });

export const useLastUsedLotForMaterial = (materialCode: string | null) =>
  useQuery({
    enabled: !!materialCode,
    queryKey: ['materialContainers', 'last-used-lot', materialCode],
    queryFn: (): Promise<GetLastUsedLotForMaterialResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.materialContainers_GetLastUsedLot(materialCode!);
    },
  });

export type {
  CreateMaterialContainerItem,
  CreateMaterialContainersResponse,
  GetMaterialContainerByCodeResponse,
  GetLastUsedLotForMaterialResponse,
} from '../generated/api-client';
