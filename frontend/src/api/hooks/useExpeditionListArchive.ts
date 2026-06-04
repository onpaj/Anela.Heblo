import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, getApiBaseUrl, QUERY_KEYS } from "../client";
import { ReprintExpeditionListRequest } from "../generated/api-client";

// --- Types ---

export interface ExpeditionListItemDto {
  blobPath: string;
  fileName: string;
  listId: string;
  createdOn: string | null;
  contentLength: number | null;
}

export interface GetExpeditionDatesResponse {
  dates: string[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface GetExpeditionListsByDateResponse {
  items: ExpeditionListItemDto[];
}

export interface ReprintExpeditionListResponse {
  success: boolean;
  errorMessage: string | null;
}

export interface RunExpeditionListPrintFixResult {
  totalCount: number;
  errorMessage: string | null;
}

// --- Query Keys ---

const expeditionArchiveKeys = {
  all: QUERY_KEYS.expeditionListArchive,
  dates: (page: number, pageSize: number) =>
    [...QUERY_KEYS.expeditionListArchive, "dates", page, pageSize] as const,
  itemsByDate: (date: string) =>
    [...QUERY_KEYS.expeditionListArchive, "items", date] as const,
};

// --- Hooks ---

export const useExpeditionDates = (page: number = 1, pageSize: number = 20) => {
  return useQuery<GetExpeditionDatesResponse>({
    queryKey: expeditionArchiveKeys.dates(page, pageSize),
    queryFn: async (): Promise<GetExpeditionDatesResponse> => {
      const client = getAuthenticatedApiClient();
      const response = await client.expeditionListArchive_GetDates(page, pageSize);
      return {
        dates: response.dates ?? [],
        totalCount: response.totalCount ?? 0,
        page: response.page ?? page,
        pageSize: response.pageSize ?? pageSize,
      };
    },
    staleTime: 1000 * 60 * 5,
  });
};

export const useExpeditionListsByDate = (date: string) => {
  return useQuery<GetExpeditionListsByDateResponse>({
    queryKey: expeditionArchiveKeys.itemsByDate(date),
    queryFn: async (): Promise<GetExpeditionListsByDateResponse> => {
      const client = getAuthenticatedApiClient();
      const response = await client.expeditionListArchive_GetByDate(date);
      return {
        items: (response.items ?? []).map((item) => ({
          blobPath: item.blobPath ?? '',
          fileName: item.fileName ?? '',
          listId: item.listId ?? '',
          createdOn: item.createdOn ? item.createdOn.toISOString() : null,
          contentLength: item.contentLength ?? null,
        })),
      };
    },
    enabled: !!date,
    staleTime: 1000 * 60 * 5,
  });
};

export const useReprintExpeditionList = () => {
  const queryClient = useQueryClient();

  return useMutation<ReprintExpeditionListResponse, Error, { blobPath: string }>({
    mutationFn: async (input): Promise<ReprintExpeditionListResponse> => {
      const client = getAuthenticatedApiClient();
      const request = new ReprintExpeditionListRequest({ blobPath: input.blobPath });
      const response = await client.expeditionListArchive_Reprint(request);
      return {
        success: response.success ?? true,
        errorMessage: response.errorMessage ?? null,
      };
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive });
    },
  });
};

export const useRunExpeditionListPrintFix = () => {
  return useMutation<RunExpeditionListPrintFixResult, Error, void>({
    mutationFn: async (): Promise<RunExpeditionListPrintFixResult> => {
      const client = getAuthenticatedApiClient();
      const response = await client.expeditionList_RunFix();
      return {
        totalCount: response.totalCount ?? 0,
        errorMessage: response.errorMessage ?? null,
      };
    },
  });
};

export const getExpeditionListDownloadUrl = (blobPath: string): string => {
  const encodedPath = blobPath.split("/").map(encodeURIComponent).join("/");
  return `${getApiBaseUrl()}/api/expedition-list-archive/download/${encodedPath}`;
};
