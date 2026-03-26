import { useQuery, useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export const getExpeditionListDownloadUrl = (blobPath: string): string => {
  const apiClient = getAuthenticatedApiClient();
  return `${(apiClient as any).baseUrl}/api/expedition-list-archive/download/${blobPath}`;
};

export interface ExpeditionListItemDto {
  fileName: string;
  blobPath: string;
  uploadedAt: string | null;
  sizeBytes: number | null;
}

export interface GetExpeditionDatesResponse {
  dates: string[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface GetExpeditionListsByDateResponse {
  items: ExpeditionListItemDto[];
  date: string;
}

export interface ReprintRequest {
  blobPath: string;
}

export interface ReprintResponse {
  success: boolean;
  message: string;
}

const EXPEDITION_ARCHIVE_QUERY_KEY = "expedition-list-archive";

export const useExpeditionDates = (page: number, pageSize: number) => {
  const apiClient = getAuthenticatedApiClient();

  return useQuery<GetExpeditionDatesResponse>({
    queryKey: [EXPEDITION_ARCHIVE_QUERY_KEY, "dates", page, pageSize],
    queryFn: async () => {
      const searchParams = new URLSearchParams();
      searchParams.append("page", page.toString());
      searchParams.append("pageSize", pageSize.toString());

      const relativeUrl = `/api/expedition-list-archive/dates?${searchParams.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
      });

      if (!response.ok) {
        throw new Error(
          `Failed to fetch expedition dates: ${response.status} ${response.statusText}`,
        );
      }

      return response.json();
    },
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};

export const useExpeditionListsByDate = (date: string | null) => {
  const apiClient = getAuthenticatedApiClient();

  return useQuery<GetExpeditionListsByDateResponse>({
    queryKey: [EXPEDITION_ARCHIVE_QUERY_KEY, "by-date", date],
    queryFn: async () => {
      const relativeUrl = `/api/expedition-list-archive/${encodeURIComponent(date!)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
      });

      if (!response.ok) {
        throw new Error(
          `Failed to fetch expedition lists for date ${date}: ${response.status} ${response.statusText}`,
        );
      }

      return response.json();
    },
    enabled: date !== null,
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};

export const useReprintExpeditionList = () => {
  const apiClient = getAuthenticatedApiClient();

  return useMutation<ReprintResponse, Error, ReprintRequest>({
    mutationFn: async (request: ReprintRequest): Promise<ReprintResponse> => {
      const relativeUrl = `/api/expedition-list-archive/reprint`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(
          `Failed to reprint expedition list: ${response.status} ${response.statusText}`,
        );
      }

      return response.json();
    },
  });
};
