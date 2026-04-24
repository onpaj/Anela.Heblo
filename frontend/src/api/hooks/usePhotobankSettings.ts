import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

// ---- Types ----------------------------------------------------------------

export interface IndexRootDto {
  id: number;
  sharePointPath: string;
  displayName: string | null;
  driveId: string | null;
  rootItemId: string | null;
  isActive: boolean;
  createdAt: string;
  lastIndexedAt: string | null;
}

export interface TagRuleDto {
  id: number;
  pathPattern: string;
  tagName: string;
  isActive: boolean;
  sortOrder: number;
}

export interface ReapplyRulesResult {
  photosUpdated: number;
}

export interface AddIndexRootInput {
  sharePointPath: string;
  displayName: string | null;
  driveId: string;
  rootItemId: string;
}

export interface AddTagRuleInput {
  pathPattern: string;
  tagName: string;
  sortOrder: number;
}

// ---- Helpers ----------------------------------------------------------------

function getClientAndBaseUrl(): {
  apiClient: ReturnType<typeof getAuthenticatedApiClient>;
  baseUrl: string;
} {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl as string;
  return { apiClient, baseUrl };
}

async function apiFetch(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, { method: "GET" });
  if (!response.ok) {
    throw new Error(`Photobank settings API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

async function apiPost(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
  body: unknown,
): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`Photobank settings API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

async function apiDelete(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, { method: "DELETE" });
  if (!response.ok) {
    throw new Error(`Photobank settings API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

const ROOTS_QUERY_KEY = [...QUERY_KEYS.photobank, "settings", "roots"] as const;
const RULES_QUERY_KEY = [...QUERY_KEYS.photobank, "settings", "rules"] as const;

// ---- Index Roots Hooks -------------------------------------------------------

export const useIndexRoots = () => {
  return useQuery<IndexRootDto[]>({
    queryKey: ROOTS_QUERY_KEY,
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiFetch(apiClient, `${baseUrl}/api/photobank/settings/roots`);
      const data = await response.json();
      return data.roots ?? [];
    },
  });
};

export const useAddIndexRoot = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddIndexRootInput) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiPost(
        apiClient,
        `${baseUrl}/api/photobank/settings/roots`,
        input,
      );
      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROOTS_QUERY_KEY });
    },
  });
};

export const useDeleteIndexRoot = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      await apiDelete(apiClient, `${baseUrl}/api/photobank/settings/roots/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROOTS_QUERY_KEY });
    },
  });
};

// ---- Tag Rules Hooks ---------------------------------------------------------

export const useTagRules = () => {
  return useQuery<TagRuleDto[]>({
    queryKey: RULES_QUERY_KEY,
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiFetch(apiClient, `${baseUrl}/api/photobank/settings/rules`);
      const data = await response.json();
      return data.rules ?? [];
    },
  });
};

export const useAddTagRule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddTagRuleInput) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiPost(
        apiClient,
        `${baseUrl}/api/photobank/settings/rules`,
        input,
      );
      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: RULES_QUERY_KEY });
    },
  });
};

export const useDeleteTagRule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      await apiDelete(apiClient, `${baseUrl}/api/photobank/settings/rules/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: RULES_QUERY_KEY });
    },
  });
};

export const useReapplyTagRules = () => {
  return useMutation({
    mutationFn: async (): Promise<ReapplyRulesResult> => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiPost(
        apiClient,
        `${baseUrl}/api/photobank/settings/rules/reapply`,
        {},
      );
      return response.json();
    },
  });
};
