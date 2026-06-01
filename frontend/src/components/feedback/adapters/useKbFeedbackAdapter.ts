import { useKnowledgeBaseFeedbackListQuery } from '../../../api/hooks/useKnowledgeBase';
import type { FeedbackDetail, GenericFeedbackParams, GenericFeedbackStats } from '../types';

export function useKbFeedbackAdapter(params: GenericFeedbackParams) {
  const query = useKnowledgeBaseFeedbackListQuery({
    pageNumber: params.pageNumber,
    pageSize: params.pageSize,
    sortBy: params.sortBy,
    sortDescending: params.sortDescending,
    hasFeedback: params.hasFeedback,
    userId: params.userId,
  });

  const rows: FeedbackDetail[] = (query.data?.logs ?? []).map((log) => ({
    id: log.id,
    primaryText: log.question,
    secondaryText: log.answer ?? '',
    createdAt: log.createdAt,
    userId: log.userId ?? undefined,
    precisionScore: log.precisionScore,
    styleScore: log.styleScore,
    hasFeedback: log.hasFeedback,
    feedbackComment: log.feedbackComment,
  }));

  const stats: GenericFeedbackStats | undefined = query.data?.stats
    ? {
        totalItems: query.data.stats.totalQuestions,
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
    pageNumber: query.data?.pageNumber ?? params.pageNumber,
    isLoading: query.isLoading,
    isError: query.isError,
  };
}
