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
      const client = getAuthenticatedApiClient();
      const data = await client.articles_GetTrace(id);
      return {
        articleId: data.articleId ?? id,
        steps: (data.steps ?? []).map((step) => ({
          id: step.id ?? '',
          stepName: step.stepName ?? '',
          sequence: step.sequence ?? 0,
          status: step.status ?? '',
          startedAt: step.startedAt?.toISOString() ?? '',
          finishedAt: step.finishedAt?.toISOString() ?? null,
          durationMs: step.durationMs ?? null,
          model: step.model ?? null,
          inputJson: step.inputJson ?? null,
          outputJson: step.outputJson ?? null,
          errorMessage: step.errorMessage ?? null,
        })),
      };
    },
    enabled,
    staleTime: 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};
