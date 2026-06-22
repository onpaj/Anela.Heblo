import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import {
  PackingMaterialDto,
  PackingMaterialLogDto,
  GetPackingMaterialsListResponse,
  GetPackingMaterialLogsResponse,
  CreatePackingMaterialRequest,
  ICreatePackingMaterialRequest,
  CreatePackingMaterialResponse,
  UpdatePackingMaterialRequest,
  IUpdatePackingMaterialRequest,
  UpdatePackingMaterialResponse,
  UpdatePackingMaterialQuantityResponse,
  ProcessDailyConsumptionRequest,
  ProcessDailyConsumptionResponse,
  GetConsumptionHistoryResponse,
  MaterialConsumptionHistoryItemDto,
  ConsumptionType,
  LogEntryType,
  HistoryRecordType,
  UpdateQuantityRequest as UpdateQuantityRequestGenerated,
} from "../generated/api-client";

export {
  PackingMaterialDto,
  PackingMaterialLogDto,
  GetPackingMaterialsListResponse,
  GetPackingMaterialLogsResponse,
  CreatePackingMaterialRequest,
  CreatePackingMaterialResponse,
  UpdatePackingMaterialRequest,
  UpdatePackingMaterialResponse,
  UpdatePackingMaterialQuantityResponse,
  ProcessDailyConsumptionRequest,
  ProcessDailyConsumptionResponse,
  GetConsumptionHistoryResponse,
  ConsumptionType,
  LogEntryType,
  HistoryRecordType,
};

export type ConsumptionHistoryItemDto = MaterialConsumptionHistoryItemDto;

export interface UpdateQuantityRequest {
  newQuantity: number;
  date: string;
}

export interface ConsumptionHistoryParams {
  dateFrom?: string;
  dateTo?: string;
  packingMaterialId?: number;
  consumptionType?: ConsumptionType;
  productCode?: string;
  invoiceId?: string;
  pageNumber?: number;
  pageSize?: number;
  sortDescending?: boolean;
}

const QUERY_KEYS = {
  packingMaterials: ["packingMaterials"] as const,
  packingMaterialLogs: (id: number, days: number) =>
    ["packingMaterials", id, "logs", days] as const,
  consumptionHistory: (params: ConsumptionHistoryParams) =>
    ["packingMaterials", "consumptionHistory", params] as const,
};

export const usePackingMaterials = () =>
  useQuery({
    queryKey: QUERY_KEYS.packingMaterials,
    queryFn: () => getAuthenticatedApiClient().packingMaterials_GetPackingMaterials(),
  });

export const useCreatePackingMaterial = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: ICreatePackingMaterialRequest) =>
      getAuthenticatedApiClient().packingMaterials_CreatePackingMaterial(request as CreatePackingMaterialRequest),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials }),
  });
};

export const useUpdatePackingMaterial = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...rest }: IUpdatePackingMaterialRequest) =>
      getAuthenticatedApiClient().packingMaterials_UpdatePackingMaterial(id!, rest as UpdatePackingMaterialRequest),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials }),
  });
};

export const useUpdatePackingMaterialQuantity = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, newQuantity, date }: { id: number } & UpdateQuantityRequest) =>
      getAuthenticatedApiClient().packingMaterials_UpdatePackingMaterialQuantity(id, {
        newQuantity,
        date: new Date(date),
      } as UpdateQuantityRequestGenerated),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials }),
  });
};

export const useDeletePackingMaterial = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      getAuthenticatedApiClient().packingMaterials_DeletePackingMaterial(id),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials }),
  });
};

export const useProcessDailyConsumption = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: ProcessDailyConsumptionRequest) =>
      getAuthenticatedApiClient().packingMaterials_ProcessDailyConsumption(request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials }),
  });
};

export const usePackingMaterialLogs = (id: number, days: number = 60) =>
  useQuery({
    queryKey: QUERY_KEYS.packingMaterialLogs(id, days),
    queryFn: () =>
      getAuthenticatedApiClient().packingMaterials_GetPackingMaterialLogs(id, days),
    enabled: !!id,
  });

export const useConsumptionHistory = (params: ConsumptionHistoryParams) =>
  useQuery({
    queryKey: QUERY_KEYS.consumptionHistory(params),
    queryFn: () =>
      getAuthenticatedApiClient().packingMaterials_GetConsumptionHistory(
        params.dateFrom ?? undefined,
        params.dateTo ?? undefined,
        params.packingMaterialId ?? undefined,
        params.consumptionType ?? undefined,
        params.productCode ?? undefined,
        params.invoiceId ?? undefined,
        params.pageNumber ?? undefined,
        params.pageSize ?? undefined,
        params.sortDescending ?? undefined,
      ),
  });
