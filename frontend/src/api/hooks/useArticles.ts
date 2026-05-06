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
  knowledgeBaseChunkId: string | null;
  excerpt: string | null;
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
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
}

export interface ListArticlesParams {
  status?: ArticleStatus;
  page?: number;
  pageSize?: number;
}

export const IN_PROGRESS_STATUSES = new Set<ArticleStatus>([
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
  feedbackList: (params: object) =>
    [...QUERY_KEYS.articles, 'feedbackList', params] as const,
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
          knowledgeBaseChunkId: (s as any).knowledgeBaseChunkId ?? null,
          excerpt: (s as any).excerpt ?? null,
        })),
        precisionScore: (response as any).precisionScore ?? null,
        styleScore: (response as any).styleScore ?? null,
        feedbackComment: (response as any).feedbackComment ?? null,
      };
    },
    enabled: !!id,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status && IN_PROGRESS_STATUSES.has(status) ? 3000 : false;
    },
    staleTime: 10 * 1000,
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

export interface ArticleFeedbackSubmitData {
  articleId: string;
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export const useSubmitArticleFeedbackMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: ArticleFeedbackSubmitData): Promise<{ alreadySubmitted: boolean }> => {
      const apiClient = getAuthenticatedApiClient(false);
      const fullUrl = `${(apiClient as any).baseUrl}/api/Articles/${data.articleId}/feedback`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({
          precisionScore: data.precisionScore,
          styleScore: data.styleScore,
          comment: data.comment,
        }),
      });

      const text: string = await response.text();
      const json = text ? JSON.parse(text) : {};
      const alreadySubmitted = json?.errorCode === 'ArticleFeedbackAlreadySubmitted';
      return { alreadySubmitted };
    },
    onSuccess: (_result, variables) => {
      queryClient.invalidateQueries({ queryKey: articleKeys.detail(variables.articleId) });
    },
  });
};

export interface ArticleFeedbackListParams {
  hasFeedback?: boolean;
  requestedBy?: string;
  sortBy?: string;
  sortDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export const useArticleFeedbackListQuery = (params: ArticleFeedbackListParams = {}) => {
  return useQuery({
    queryKey: articleKeys.feedbackList(params),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      let url = `${(apiClient as any).baseUrl}/api/Articles/feedback/list?`;
      if (params.hasFeedback !== undefined) url += `hasFeedback=${params.hasFeedback}&`;
      if (params.requestedBy) url += `requestedBy=${encodeURIComponent(params.requestedBy)}&`;
      url += `sortBy=${encodeURIComponent(params.sortBy ?? 'CreatedAt')}&`;
      url += `sortDescending=${params.sortDescending ?? true}&`;
      url += `pageNumber=${params.pageNumber ?? 1}&`;
      url += `pageSize=${params.pageSize ?? 20}`;

      const response = await (apiClient as any).http.fetch(url, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      const text: string = await response.text();
      return text ? JSON.parse(text) : null;
    },
    staleTime: 30_000,
  });
};
