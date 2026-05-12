import { useLeafletFeedbackListQuery } from '../../../api/hooks/useLeaflet';
import type { FeedbackDetail, GenericFeedbackParams, GenericFeedbackStats } from '../types';

export function useLeafletFeedbackAdapter(params: GenericFeedbackParams) {
  const query = useLeafletFeedbackListQuery({
    pageNumber: params.pageNumber,
    pageSize: params.pageSize,
    sortBy: params.sortBy,
    sortDescending: params.sortDescending,
    hasFeedback: params.hasFeedback,
    userId: params.userId,
  });

  const rows: FeedbackDetail[] = (query.data?.items ?? []).map((item) => ({
    id: item.id,
    primaryText: item.topic,
    secondaryText: item.finalMarkdown ?? '',
    createdAt: item.createdAt,
    userId: item.userId ?? undefined,
    precisionScore: item.precisionScore,
    styleScore: item.styleScore,
    hasFeedback: item.hasFeedback,
    feedbackComment: item.feedbackComment,
  }));

  const stats: GenericFeedbackStats | undefined = query.data?.stats
    ? {
        totalItems: query.data.stats.totalGenerations,
        totalWithFeedback: query.data.stats.totalWithFeedback,
        avgPrecisionScore: query.data.stats.avgPrecisionScore,
        avgStyleScore: query.data.stats.avgStyleScore,
      }
    : undefined;

  const totalCount = query.data?.totalCount ?? 0;
  const totalPages = query.data?.totalPages ?? 0;

  return {
    rows,
    stats,
    totalCount,
    totalPages,
    pageNumber: query.data?.pageNumber ?? params.pageNumber,
    isLoading: query.isLoading,
    isError: query.isError,
  };
}
