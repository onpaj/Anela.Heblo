import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useMsal } from '@azure/msal-react';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import { shouldUseMockAuth } from '../../config/runtimeConfig';
import { mockAuthService } from '../../auth/mockAuth';
import {
  ArticleStatus,
  GenerateArticleRequest,
} from '../generated/api-client';

// ---- Types ----

export interface ArticleListItem {
  id: string;
  topic: string;
  title: string | null;
  status: ArticleStatus;
  createdAt: string;
  generatedAt: string | null;
}

export interface ArticleSource {
  title: string;
  url: string | null;
  type: string;
}

export interface ArticleDetail {
  id: string;
  topic: string;
  scope: string;
  audience: string | null;
  angle: string | null;
  length: string;
  title: string | null;
  htmlContent: string | null;
  status: ArticleStatus;
  errorMessage: string | null;
  createdAt: string;
  generatedAt: string | null;
  useKnowledgeBase: boolean;
  useWebSearch: boolean;
  sources: ArticleSource[];
}

export interface ListArticlesParams {
  status?: ArticleStatus;
  page?: number;
  pageSize?: number;
}

const POLLING_STATUSES = new Set<ArticleStatus>([
  ArticleStatus.Queued,
  ArticleStatus.Researching,
  ArticleStatus.Writing,
]);

// ---- Permission hook ----

export const useArticleGeneratorPermission = (): boolean => {
  const { accounts } = useMsal();

  if (shouldUseMockAuth()) {
    const user = mockAuthService.getUser();
    return !!(Array.isArray(user?.roles) && user?.roles.includes('article_generator'));
  }

  const account = accounts[0];
  if (!account) return false;
  const claims = account.idTokenClaims as Record<string, unknown> | undefined;
  const roles = claims?.['roles'];
  return Array.isArray(roles) && roles.includes('article_generator');
};

// ---- Query key factory ----

export const articleKeys = {
  all: [...QUERY_KEYS.articles] as const,
  list: (params?: ListArticlesParams) =>
    [...QUERY_KEYS.articles, 'list', params ?? {}] as const,
  detail: (id: string) => [...QUERY_KEYS.articles, 'detail', id] as const,
};

// ---- Hooks ----

export const useListArticlesQuery = (params: ListArticlesParams = {}) => {
  return useQuery({
    queryKey: articleKeys.list(params),
    queryFn: async (): Promise<ArticleListItem[]> => {
      const client = getAuthenticatedApiClient();
      const response = await client.articles_List(
        params.status ?? null,
        params.page ?? 1,
        params.pageSize ?? 20,
      );
      return (response.items ?? []).map((item) => ({
        id: item.id?.toString() ?? '',
        topic: item.topic ?? '',
        title: item.title ?? null,
        status: (item.status as ArticleStatus) ?? ArticleStatus.Queued,
        createdAt: item.createdAt?.toISOString() ?? '',
        generatedAt: item.generatedAt?.toISOString() ?? null,
      }));
    },
    staleTime: 30 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};

export const useGetArticleQuery = (id: string | null) => {
  return useQuery({
    queryKey: articleKeys.detail(id ?? ''),
    queryFn: async (): Promise<ArticleDetail> => {
      const client = getAuthenticatedApiClient();
      const response = await client.articles_GetById(id!);
      return {
        id: response.id?.toString() ?? '',
        topic: response.topic ?? '',
        scope: response.scope ?? '',
        audience: response.audience ?? null,
        angle: response.angle ?? null,
        length: response.length ?? '',
        title: response.title ?? null,
        htmlContent: response.htmlContent ?? null,
        status: (response.status as ArticleStatus) ?? ArticleStatus.Queued,
        errorMessage: response.errorMessage ?? null,
        createdAt: response.createdAt?.toISOString() ?? '',
        generatedAt: response.generatedAt?.toISOString() ?? null,
        useKnowledgeBase: response.useKnowledgeBase ?? false,
        useWebSearch: response.useWebSearch ?? false,
        sources: (response.sources ?? []).map((s) => ({
          title: s.title ?? '',
          url: s.url ?? null,
          type: s.type ?? '',
        })),
      };
    },
    enabled: !!id,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status && POLLING_STATUSES.has(status) ? 3000 : false;
    },
    staleTime: 0,
    gcTime: 5 * 60 * 1000,
  });
};

export const useGenerateArticleMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: GenerateArticleRequest): Promise<string> => {
      const client = getAuthenticatedApiClient();
      const response = await client.articles_Generate(request);
      return response.articleId?.toString() ?? '';
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: articleKeys.all });
    },
  });
};
