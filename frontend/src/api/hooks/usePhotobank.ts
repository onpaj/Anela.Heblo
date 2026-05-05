import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

// ---- Types ----------------------------------------------------------------

export interface TagDto {
  id: number;
  name: string;
  source: string;
}

export interface PhotoDto {
  id: number;
  sharePointFileId: string;
  driveId: string | null;
  name: string;
  folderPath: string;
  sharePointWebUrl: string | null;
  fileSizeBytes: number | null;
  lastModifiedAt: string;
  tags: TagDto[];
}

export interface TagWithCountDto {
  id: number;
  name: string;
  count: number;
}

export interface GetPhotosResponse {
  items: PhotoDto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface GetPhotosParams {
  tags?: string[];
  search?: string;
  page?: number;
  pageSize?: number;
}

// ---- Helpers --------------------------------------------------------------

function getClientAndBaseUrl(): { apiClient: ReturnType<typeof getAuthenticatedApiClient>; baseUrl: string } {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl as string;
  return { apiClient, baseUrl };
}

async function apiFetch(apiClient: ReturnType<typeof getAuthenticatedApiClient>, url: string): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, { method: "GET" });
  if (!response.ok) {
    throw new Error(`Photobank API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

async function apiPost(apiClient: ReturnType<typeof getAuthenticatedApiClient>, url: string, body: unknown): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`Photobank API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

async function apiDelete(apiClient: ReturnType<typeof getAuthenticatedApiClient>, url: string): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, { method: "DELETE" });
  if (!response.ok) {
    throw new Error(`Photobank API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

function buildPhotosUrl(baseUrl: string, params: GetPhotosParams): string {
  const qs = new URLSearchParams();
  if (params.search) qs.set("search", params.search);
  if (params.page != null) qs.set("page", String(params.page));
  if (params.pageSize != null) qs.set("pageSize", String(params.pageSize));
  (params.tags ?? []).forEach((t) => qs.append("tags", String(t)));
  const query = qs.toString();
  return `${baseUrl}/api/photobank/photos${query ? `?${query}` : ""}`;
}

// ---- Hooks ----------------------------------------------------------------

export const usePhotos = (params: GetPhotosParams = {}) => {
  return useQuery<GetPhotosResponse>({
    queryKey: [...QUERY_KEYS.photobank, "photos", params],
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const url = buildPhotosUrl(baseUrl, params);
      const response = await apiFetch(apiClient, url);
      return response.json();
    },
  });
};

export const usePhotoTags = () => {
  return useQuery<TagWithCountDto[]>({
    queryKey: [...QUERY_KEYS.photobank, "tags"],
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiFetch(apiClient, `${baseUrl}/api/photobank/tags`);
      const data = await response.json();
      return data.tags ?? [];
    },
  });
};

export const useAddPhotoTag = (photoId: number) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (tagName: string) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      await apiPost(apiClient, `${baseUrl}/api/photobank/photos/${photoId}/tags`, { tagName });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
    },
  });
};

export const useRemovePhotoTag = (photoId: number) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (tagId: number) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      await apiDelete(apiClient, `${baseUrl}/api/photobank/photos/${photoId}/tags/${tagId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
    },
  });
};
