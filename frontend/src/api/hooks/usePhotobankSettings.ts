import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { AddRootBody, AddRuleBody, IndexRootDto, TagRuleDto } from "../generated/api-client";

export type { IndexRootDto, TagRuleDto };

// ---- Types ----------------------------------------------------------------

export interface AddIndexRootInput {
  sharePointPath: string;
  displayName: string | null;
  driveId: string;
}

export interface AddTagRuleInput {
  pathPattern: string;
  tagName: string;
  sortOrder: number;
}

const ROOTS_QUERY_KEY = [...QUERY_KEYS.photobank, "settings", "roots"] as const;
const RULES_QUERY_KEY = [...QUERY_KEYS.photobank, "settings", "rules"] as const;

// ---- Index Roots Hooks -------------------------------------------------------

export const useIndexRoots = () => {
  return useQuery<IndexRootDto[]>({
    queryKey: ROOTS_QUERY_KEY,
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const response = await apiClient.photobank_GetRoots();
      return response.roots ?? [];
    },
  });
};

export const useAddIndexRoot = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddIndexRootInput) => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.photobank_AddRoot(
        new AddRootBody({
          sharePointPath: input.sharePointPath,
          displayName: input.displayName ?? undefined,
          driveId: input.driveId,
        }),
      );
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
      const apiClient = getAuthenticatedApiClient();
      await apiClient.photobank_DeleteRoot(id);
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
      const apiClient = getAuthenticatedApiClient();
      const response = await apiClient.photobank_GetRules();
      return response.rules ?? [];
    },
  });
};

export const useAddTagRule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddTagRuleInput) => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.photobank_AddRule(
        new AddRuleBody({
          pathPattern: input.pathPattern,
          tagName: input.tagName,
          sortOrder: input.sortOrder,
        }),
      );
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
      const apiClient = getAuthenticatedApiClient();
      await apiClient.photobank_DeleteRule(id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: RULES_QUERY_KEY });
    },
  });
};

export const useReapplyTagRules = () => {
  return useMutation({
    mutationFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.photobank_ReapplyRules();
    },
  });
};
