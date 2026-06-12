import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  MarketingActionType,
  CreateMarketingActionRequest,
  UpdateMarketingActionRequest,
  ImportFromOutlookRequest,
  type ICreateMarketingActionRequest,
  type IImportFromOutlookRequest,
  type IUpdateMarketingActionRequest,
} from "../generated/api-client";

interface GetMarketingActionsParams {
  pageNumber?: number;
  pageSize?: number;
  searchTerm?: string;
  actionType?: MarketingActionType;
  productCodePrefix?: string;
  startDateFrom?: Date;
  startDateTo?: Date;
  endDateFrom?: Date;
  endDateTo?: Date;
  includeDeleted?: boolean;
}

interface GetMarketingCalendarParams {
  startDate: Date;
  endDate: Date;
}

export type CreateMarketingActionPayload = ICreateMarketingActionRequest;
export type UpdateMarketingActionPayload = IUpdateMarketingActionRequest;
export type ImportFromOutlookPayload = IImportFromOutlookRequest;

export const useMarketingActions = (
  params: GetMarketingActionsParams = {},
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.marketingCalendar, "actions", params],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.marketingCalendar_GetMarketingActions(
        params.pageNumber,
        params.pageSize,
        params.searchTerm,
        params.actionType,
        params.productCodePrefix,
        params.startDateFrom,
        params.startDateTo,
        params.endDateFrom,
        params.endDateTo,
        params.includeDeleted,
      );
    },
  });
};

export const useMarketingAction = (id: number) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.marketingCalendar, "action", id],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.marketingCalendar_GetMarketingAction(id);
    },
    enabled: id > 0,
  });
};

export const useMarketingCalendar = (params: GetMarketingCalendarParams) => {
  return useQuery({
    queryKey: [
      ...QUERY_KEYS.marketingCalendar,
      "calendar",
      params.startDate,
      params.endDate,
    ],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.marketingCalendar_GetCalendar(
        params.startDate,
        params.endDate,
      );
    },
    enabled: !!params.startDate && !!params.endDate,
  });
};

export const useCreateMarketingAction = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: CreateMarketingActionPayload) => {
      const client = await getAuthenticatedApiClient();
      return await client.marketingCalendar_CreateMarketingAction(
        new CreateMarketingActionRequest(request),
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "actions"],
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "calendar"],
      });
    },
  });
};

export const useUpdateMarketingAction = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: number;
      request: Omit<UpdateMarketingActionPayload, "id">;
    }) => {
      const client = await getAuthenticatedApiClient();
      return await client.marketingCalendar_UpdateMarketingAction(
        id,
        new UpdateMarketingActionRequest({
          ...request,
          id,
        }),
      );
    },
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "actions"],
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "calendar"],
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "action", id],
      });
    },
  });
};

export const useDeleteMarketingAction = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: number) => {
      const client = await getAuthenticatedApiClient();
      return await client.marketingCalendar_DeleteMarketingAction(id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "actions"],
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "calendar"],
      });
    },
  });
};

export interface ImportFromOutlookResult {
  created: number;
  skipped: number;
  failed: number;
  // Always present — normalized from the generated client's optional field via `?? []` in handleImport.
  unmappedCategories: string[];
}

export const useImportFromOutlook = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: ImportFromOutlookPayload) => {
      const client = await getAuthenticatedApiClient();
      return await client.marketingCalendar_ImportFromOutlook(
        new ImportFromOutlookRequest(payload),
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "actions"],
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, "calendar"],
      });
    },
  });
};
