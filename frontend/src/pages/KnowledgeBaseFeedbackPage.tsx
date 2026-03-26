import React, { useState } from 'react';
import { MessageSquare } from 'lucide-react';
import {
  FeedbackLogSummary,
  GetFeedbackListParams,
  useKnowledgeBaseFeedbackListQuery,
} from '../api/hooks/useKnowledgeBase';
import FeedbackStatsBar from '../components/knowledge-base/FeedbackStatsBar';
import FeedbackFilters from '../components/knowledge-base/FeedbackFilters';
import FeedbackTable from '../components/knowledge-base/FeedbackTable';
import FeedbackDetailModal from '../components/knowledge-base/FeedbackDetailModal';

const defaultParams: GetFeedbackListParams = {
  pageNumber: 1,
  pageSize: 20,
  sortBy: 'CreatedAt',
  sortDescending: true,
};

const KnowledgeBaseFeedbackPage: React.FC = () => {
  const [params, setParams] = useState<GetFeedbackListParams>(defaultParams);
  const [selectedLog, setSelectedLog] = useState<FeedbackLogSummary | null>(null);

  const { data, isLoading, isError } = useKnowledgeBaseFeedbackListQuery(params);

  const handleParamsChange = (next: GetFeedbackListParams) => {
    setParams(next);
    setSelectedLog(null);
  };

  const handlePageChange = (page: number) => {
    setParams((prev) => ({ ...prev, pageNumber: page }));
    setSelectedLog(null);
  };

  const handleSelect = (log: FeedbackLogSummary) => {
    setSelectedLog((prev) => (prev?.id === log.id ? null : log));
  };

  const handleClosePanel = () => setSelectedLog(null);

  return (
    <div className="flex flex-col h-full">
      {/* Page header */}
      <div className="px-6 py-4 border-b border-gray-200 flex items-center gap-3 flex-shrink-0">
        <MessageSquare className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Feedback</h1>
      </div>

      {/* Main content */}
      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        {/* Stats */}
        {data?.stats && <FeedbackStatsBar stats={data.stats} />}

        {/* Filters */}
        <FeedbackFilters params={params} onParamsChange={handleParamsChange} />

        {/* Loading / error / table */}
        {isLoading && (
          <div className="flex items-center justify-center h-32 text-sm text-gray-500">
            Načítám…
          </div>
        )}

        {isError && (
          <div className="flex items-center justify-center h-32 text-sm text-red-600">
            Nepodařilo se načíst záznamy. Zkuste to znovu.
          </div>
        )}

        {data && !isLoading && (
          <FeedbackTable
            logs={data.logs}
            totalCount={data.totalCount}
            pageNumber={data.pageNumber}
            pageSize={data.pageSize}
            totalPages={data.totalPages}
            onSelect={handleSelect}
            onPageChange={handlePageChange}
          />
        )}
      </div>

      {/* Detail modal */}
      {selectedLog && (
        <FeedbackDetailModal log={selectedLog} onClose={handleClosePanel} />
      )}
    </div>
  );
};

export default KnowledgeBaseFeedbackPage;
