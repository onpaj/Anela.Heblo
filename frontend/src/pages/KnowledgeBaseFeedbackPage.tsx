// frontend/src/pages/KnowledgeBaseFeedbackPage.tsx
import React, { useState } from 'react';
import { MessageSquare } from 'lucide-react';
import {
  FeedbackLogSummary,
  GetFeedbackListParams,
  useKnowledgeBaseFeedbackListQuery,
} from '../api/hooks/useKnowledgeBase';
import GenericFeedbackStatsBar from '../components/feedback/GenericFeedbackStatsBar';
import GenericFeedbackFilters from '../components/feedback/GenericFeedbackFilters';
import GenericFeedbackTable from '../components/feedback/GenericFeedbackTable';
import GenericFeedbackDetailModal from '../components/feedback/GenericFeedbackDetailModal';
import type { FeedbackDetail } from '../components/feedback/types';
import { SORT_COLUMNS } from '../components/feedback/types';
import { useScreenView } from '../telemetry/useScreenView';

const defaultParams: GetFeedbackListParams = {
  pageNumber: 1,
  pageSize: 20,
  sortBy: 'CreatedAt',
  sortDescending: true,
};

function mapLogToDetail(log: FeedbackLogSummary): FeedbackDetail {
  return {
    id: log.id,
    primaryText: log.question,
    secondaryText: log.answer ?? '',
    createdAt: log.createdAt,
    userId: log.userId ?? undefined,
    precisionScore: log.precisionScore,
    styleScore: log.styleScore,
    hasFeedback: log.hasFeedback,
    feedbackComment: log.feedbackComment,
    extra: (
      <div className="grid grid-cols-3 gap-4 text-sm">
        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">TopK</p>
          <p className="text-gray-900">{log.topK}</p>
        </div>
        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Zdrojů</p>
          <p className="text-gray-900">{log.sourceCount}</p>
        </div>
        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Doba odezvy</p>
          <p className="text-gray-900">{log.durationMs} ms</p>
        </div>
      </div>
    ),
  };
}

const KnowledgeBaseFeedbackPage: React.FC = () => {
  const [params, setParams] = useState<GetFeedbackListParams>(defaultParams);
  const [selectedRow, setSelectedRow] = useState<FeedbackDetail | null>(null);

  useScreenView('Knowledge', 'KnowledgeBaseFeedback');

  const { data, isLoading, isError } = useKnowledgeBaseFeedbackListQuery(params);

  const rows: FeedbackDetail[] = (data?.logs ?? []).map(mapLogToDetail);

  const stats = data?.stats
    ? {
        totalItems: data.stats.totalQuestions,
        totalWithFeedback: data.stats.totalWithFeedback,
        avgPrecisionScore: data.stats.avgPrecisionScore,
        avgStyleScore: data.stats.avgStyleScore,
      }
    : undefined;

  return (
    <div className="flex flex-col h-full">
      <div className="px-6 py-4 border-b border-gray-200 flex items-center gap-3 flex-shrink-0">
        <MessageSquare className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Feedback</h1>
      </div>

      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        <GenericFeedbackStatsBar stats={stats} isLoading={isLoading} itemLabel="dotazů" />

        <GenericFeedbackFilters
          hasFeedback={params.hasFeedback}
          sortBy={params.sortBy ?? 'CreatedAt'}
          sortDescending={params.sortDescending ?? true}
          pageSize={params.pageSize ?? 20}
          allowedSortColumns={SORT_COLUMNS}
          onHasFeedbackChange={(v) => setParams((p) => ({ ...p, pageNumber: 1, hasFeedback: v }))}
          onSortByChange={(v) => setParams((p) => ({ ...p, pageNumber: 1, sortBy: v }))}
          onSortDescendingChange={(v) => setParams((p) => ({ ...p, pageNumber: 1, sortDescending: v }))}
          onPageSizeChange={(v) => setParams((p) => ({ ...p, pageNumber: 1, pageSize: v }))}
        />

        {isError && (
          <div className="flex items-center justify-center h-32 text-sm text-red-600">
            Nepodařilo se načíst záznamy. Zkuste to znovu.
          </div>
        )}

        {!isError && (
          <GenericFeedbackTable
            rows={rows}
            isLoading={isLoading}
            totalCount={data?.totalCount ?? 0}
            pageNumber={data?.pageNumber ?? 1}
            pageSize={params.pageSize ?? 20}
            totalPages={data?.totalPages ?? 0}
            onPageChange={(page) => {
              setParams((p) => ({ ...p, pageNumber: page }));
              setSelectedRow(null);
            }}
            onRowClick={(id) =>
              setSelectedRow((prev) => (prev?.id === id ? null : rows.find((r) => r.id === id) ?? null))
            }
            primaryLabel="Dotaz"
          />
        )}
      </div>

      {selectedRow && (
        <GenericFeedbackDetailModal
          detail={selectedRow}
          onClose={() => setSelectedRow(null)}
          primaryLabel="Dotaz"
          secondaryLabel="Odpověď"
        />
      )}
    </div>
  );
};

export default KnowledgeBaseFeedbackPage;
