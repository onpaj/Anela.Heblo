import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
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
  confidence: number | null;
  excerpt: string | null;
  validationNote: string | null;
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

export interface SubmitArticleFeedbackPayload {
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export interface SubmitArticleFeedbackResult {
  alreadySubmitted?: true;
  precisionScore?: number | null;
  styleScore?: number | null;
  feedbackComment?: string | null;
}

export interface ArticleFeedbackListParams {
  hasFeedback?: boolean;
  requestedBy?: string;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ArticleFeedbackSummary {
  id: string;
  topic: string;
  title: string | null;
  requestedBy: string;
  generatedAt: string | null;
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
  hasFeedback: boolean;
}

export interface ArticleFeedbackStats {
  totalArticles: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}

export interface ArticleFeedbackListResponse {
  articles: ArticleFeedbackSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  stats: ArticleFeedbackStats;
}

export const IN_PROGRESS_STATUSES = new Set<ArticleStatus>([
  ArticleStatus.Queued,
  ArticleStatus.Researching,
  ArticleStatus.Writing,
]);

// ---- Query key factory ----

export const articleKeys = {
  all: [...QUERY_KEYS.articles] as const,
  list: (params?: ListArticlesParams) =>
    [...QUERY_KEYS.articles, 'list', params ?? {}] as const,
  detail: (id: string) => [...QUERY_KEYS.articles, 'detail', id] as const,
  feedbackList: (params?: ArticleFeedbackListParams) =>
    [...QUERY_KEYS.articles, 'feedback-list', params ?? {}] as const,
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
        sources: (response.sources ?? []).map((s) => {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const raw = s as any;
          return {
            title: s.title ?? '',
            url: s.url ?? null,
            type: s.type ?? '',
            knowledgeBaseChunkId: (raw.knowledgeBaseChunkId as string | null) ?? null,
            confidence: (raw.confidence as number | null) ?? null,
            excerpt: (raw.excerpt as string | null) ?? null,
            validationNote: (raw.validationNote as string | null) ?? null,
          };
        }),
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        precisionScore: ((response as any).precisionScore as number | null) ?? null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        styleScore: ((response as any).styleScore as number | null) ?? null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        feedbackComment: ((response as any).feedbackComment as string | null) ?? null,
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

export const useSubmitArticleFeedbackMutation = (articleId: string) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: SubmitArticleFeedbackPayload): Promise<SubmitArticleFeedbackResult> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/articles/${articleId}/feedback`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({
          articleId,
          precisionScore: payload.precisionScore,
          styleScore: payload.styleScore,
          comment: payload.comment,
        }),
      });

      if (response.status === 409) {
        return { alreadySubmitted: true };
      }

      if (!response.ok) {
        throw new Error(`Submit article feedback failed: ${response.status}`);
      }

      const data = await response.json();
      return {
        precisionScore: data.precisionScore ?? null,
        styleScore: data.styleScore ?? null,
        feedbackComment: data.feedbackComment ?? null,
      };
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: articleKeys.detail(articleId) });
    },
  });
};

export const useArticleFeedbackListQuery = (params: ArticleFeedbackListParams = {}) => {
  return useQuery({
    queryKey: articleKeys.feedbackList(params),
    queryFn: async (): Promise<ArticleFeedbackListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();

      if (params.hasFeedback !== undefined)
        searchParams.append('hasFeedback', params.hasFeedback.toString());
      if (params.requestedBy) searchParams.append('requestedBy', params.requestedBy);
      if (params.sortBy) searchParams.append('sortBy', params.sortBy);
      if (params.sortDescending !== undefined)
        searchParams.append('sortDescending', params.sortDescending.toString());
      if (params.page !== undefined)
        searchParams.append('page', params.page.toString());
      if (params.pageSize !== undefined)
        searchParams.append('pageSize', params.pageSize.toString());

      const query = searchParams.toString();
      const relativeUrl = `/api/articles/feedback/list${query ? `?${query}` : ''}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch article feedback list: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 30_000,
    gcTime: 5 * 60 * 1000,
  });
};
