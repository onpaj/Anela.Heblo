import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  InventoryChangeType,
  IManufacturedProductInventoryItemDto,
  IManufacturedProductInventoryLogDto,
  CreateManufacturedInventoryItemRequest,
  UpdateManufacturedInventoryItemBody,
} from "../generated/api-client";

export { InventoryChangeType };
export type ManufacturedProductInventoryItem = IManufacturedProductInventoryItemDto;
export type ManufacturedProductInventoryLog = IManufacturedProductInventoryLogDto;

export interface ManufacturedInventoryFilters {
  search?: string;
  onlyWithStock?: boolean;
  manufactureOrderId?: number;
  page?: number;
  pageSize?: number;
}

export interface CreateManufacturedInventoryItemInput {
  productCode: string;
  productName: string;
  amount: number;
  lotNumber?: string;
  expirationDate?: string;
  manufactureOrderId?: number;
}

export interface UpdateManufacturedInventoryItemInput {
  id: number;
  newAmount: number;
  note?: string;
}

export const useManufacturedProductInventoryQuery = (filters: ManufacturedInventoryFilters = {}) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.manufacturedProductInventory, filters],
    queryFn: () =>
      getAuthenticatedApiClient().manufacturedProductInventory_GetInventory(
        filters.search,
        filters.onlyWithStock,
        filters.manufactureOrderId,
        filters.page,
        filters.pageSize,
      ),
    staleTime: 1000 * 60 * 5,
  });
};

export const useCreateManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (input: CreateManufacturedInventoryItemInput): Promise<ManufacturedProductInventoryItem> => {
      const result = await getAuthenticatedApiClient().manufacturedProductInventory_CreateItem(
        new CreateManufacturedInventoryItemRequest({
          ...input,
          expirationDate: input.expirationDate ? new Date(input.expirationDate) : undefined,
        }),
      );
      return result.item!;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};

export const useUpdateManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (input: UpdateManufacturedInventoryItemInput): Promise<ManufacturedProductInventoryItem> => {
      const result = await getAuthenticatedApiClient().manufacturedProductInventory_UpdateItem(
        input.id,
        new UpdateManufacturedInventoryItemBody({ newAmount: input.newAmount, note: input.note }),
      );
      return result.item!;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};

export const useDeleteManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, note }: { id: number; note?: string }): Promise<void> => {
      await getAuthenticatedApiClient().manufacturedProductInventory_DeleteItem(id, note);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};
