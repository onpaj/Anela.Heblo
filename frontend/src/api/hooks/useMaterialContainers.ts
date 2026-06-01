import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  CreateMaterialContainersRequest,
  CreateMaterialContainersResponse,
  GetMaterialContainerByCodeResponse,
  GetLastUsedLotForMaterialResponse,
  CreateMaterialContainerItem,
  ListMaterialContainersResponse,
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

export interface MaterialContainersListRequest {
  materialCode?: string;
  lotCode?: string;
  code?: string;
  page?: number;
  pageSize?: number;
}

export const useMaterialContainersList = (request: MaterialContainersListRequest) =>
  useQuery({
    queryKey: ['materialContainers', 'list', request],
    queryFn: (): Promise<ListMaterialContainersResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.materialContainers_GetMaterialContainers(
        request.materialCode || undefined,
        request.lotCode || undefined,
        request.code || undefined,
        request.page ?? 1,
        request.pageSize ?? 20,
      );
    },
  });

export type {
  CreateMaterialContainerItem,
  CreateMaterialContainersResponse,
  GetMaterialContainerByCodeResponse,
  GetLastUsedLotForMaterialResponse,
  ListMaterialContainersResponse,
} from '../generated/api-client';
