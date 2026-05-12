import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface ArticleGenerationStep {
  id: string;
  stepName: string;
  sequence: number;
  status: string;
  startedAt: string;
  finishedAt: string | null;
  durationMs: number | null;
  model: string | null;
  inputJson: string | null;
  outputJson: string | null;
  errorMessage: string | null;
}

export interface ArticleTrace {
  articleId: string;
  steps: ArticleGenerationStep[];
}

export const useArticleTraceQuery = (id: string, enabled: boolean) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.articleTrace, id],
    queryFn: async (): Promise<ArticleTrace> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/articles/${id}/trace`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch article trace: ${response.status}`);
      }

      const data = await response.json();
      return {
        articleId: data.articleId ?? id,
        steps: (data.steps ?? []) as ArticleGenerationStep[],
      };
    },
    enabled,
    staleTime: 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};
