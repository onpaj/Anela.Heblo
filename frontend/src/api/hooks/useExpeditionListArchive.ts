import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

// --- Types ---

export interface ExpeditionListItemDto {
  blobPath: string;
  fileName: string;
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

export interface ReprintExpeditionListRequest {
  blobPath: string;
}

export interface ReprintExpeditionListResponse {
  success: boolean;
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
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list-archive/dates`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const params = new URLSearchParams();
      params.append("page", page.toString());
      params.append("pageSize", pageSize.toString());

      const response = await (apiClient as any).http.fetch(
        `${fullUrl}?${params.toString()}`,
        {
          method: "GET",
          headers: { "Content-Type": "application/json" },
        }
      );

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return await response.json();
    },
    staleTime: 1000 * 60 * 5,
  });
};

export const useExpeditionListsByDate = (date: string) => {
  return useQuery<GetExpeditionListsByDateResponse>({
    queryKey: expeditionArchiveKeys.itemsByDate(date),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list-archive/${encodeURIComponent(date)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: { "Content-Type": "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return await response.json();
    },
    enabled: !!date,
    staleTime: 1000 * 60 * 5,
  });
};

export const useReprintExpeditionList = () => {
  const queryClient = useQueryClient();

  return useMutation<ReprintExpeditionListResponse, Error, ReprintExpeditionListRequest>({
    mutationFn: async (request: ReprintExpeditionListRequest) => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list-archive/reprint`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ blobPath: request.blobPath }),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
        );
      }

      return await response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive });
    },
  });
};

export const useRunExpeditionListPrintFix = () => {
  return useMutation({
    mutationFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list-archive/run-fix`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
        );
      }

      return await response.json();
    },
  });
};

export const getExpeditionListDownloadUrl = (blobPath: string): string => {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl;
  const encodedPath = blobPath.split("/").map(encodeURIComponent).join("/");
  return `${baseUrl}/api/expedition-list-archive/download/${encodedPath}`;
};
