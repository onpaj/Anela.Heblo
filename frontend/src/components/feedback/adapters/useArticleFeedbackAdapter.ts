// frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts
import { useArticleFeedbackListQuery } from '../../../api/hooks/useArticles';
import type { FeedbackDetail, GenericFeedbackParams, GenericFeedbackStats } from '../types';

export function useArticleFeedbackAdapter(params: GenericFeedbackParams) {
  const query = useArticleFeedbackListQuery({
    page: params.pageNumber,
    pageSize: params.pageSize,
    sortBy: params.sortBy,
    sortDescending: params.sortDescending,
    hasFeedback: params.hasFeedback,
    requestedBy: params.userId,
  });

  const rows: FeedbackDetail[] = (query.data?.articles ?? []).map((article) => ({
    id: article.id,
    primaryText: article.title ?? article.topic,
    secondaryText: article.topic,
    createdAt: article.createdAt ?? '',
    userId: article.requestedBy,
    precisionScore: article.precisionScore,
    styleScore: article.styleScore,
    hasFeedback: article.hasComment,
    feedbackComment: null,
  }));

  const stats: GenericFeedbackStats | undefined = query.data?.stats
    ? {
        totalItems: query.data.stats.totalArticles,
        totalWithFeedback: query.data.stats.totalWithFeedback,
        avgPrecisionScore: query.data.stats.avgPrecisionScore,
        avgStyleScore: query.data.stats.avgStyleScore,
      }
    : undefined;

  return {
    rows,
    stats,
    totalCount: query.data?.totalCount ?? 0,
    totalPages: query.data?.totalPages ?? 0,
    pageNumber: query.data?.page ?? params.pageNumber,
    isLoading: query.isLoading,
    isError: query.isError,
  };
}
