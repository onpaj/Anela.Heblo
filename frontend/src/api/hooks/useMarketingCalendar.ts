import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

interface GetMarketingActionsParams {
  pageNumber?: number;
  pageSize?: number;
  searchTerm?: string;
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

interface CreateMarketingActionPayload {
  title: string;
  description?: string;
  actionType: number;
  startDate: Date;
  endDate?: Date;
  associatedProducts?: string[];
  folderLinks?: Array<{ folderKey: string; folderType: number }>;
}

interface UpdateMarketingActionPayload {
  id: number;
  title: string;
  description?: string;
  actionType: number;
  startDate: Date;
  endDate?: Date;
  associatedProducts?: string[];
  folderLinks?: Array<{ folderKey: string; folderType: number }>;
}

export const useMarketingActions = (
  params: GetMarketingActionsParams = {},
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.marketingCalendar, "actions", params],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await (client as any).marketingCalendar_GetMarketingActions(
        params.pageNumber,
        params.pageSize,
        params.searchTerm,
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
      return await (client as any).marketingCalendar_GetMarketingAction(id);
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
      return await (client as any).marketingCalendar_GetCalendar(
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
      return await (client as any).marketingCalendar_CreateMarketingAction(
        request,
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
      return await (client as any).marketingCalendar_UpdateMarketingAction(id, {
        ...request,
        id,
      });
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
      return await (client as any).marketingCalendar_DeleteMarketingAction(id);
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

interface ImportFromOutlookPayload {
  fromUtc: Date;
  toUtc: Date;
  dryRun?: boolean;
}

export interface ImportFromOutlookResult {
  created: number;
  skipped: number;
  failed: number;
}

export const useImportFromOutlook = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: ImportFromOutlookPayload) => {
      const client = await getAuthenticatedApiClient();
      return await (client as any).marketingCalendar_ImportFromOutlook(payload);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, 'actions'],
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.marketingCalendar, 'calendar'],
      });
    },
  });
};
