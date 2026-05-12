import React, { useState } from 'react';
import { PAGE_CONTAINER_HEIGHT } from '../../constants/layout';
import { useDqtRuns } from '../../api/hooks/useDataQuality';
import DqtSummaryCards from '../../components/data-quality/DqtSummaryCards';
import RunDqtButton from '../../components/data-quality/RunDqtButton';
import DqtRunsTable from '../../components/data-quality/DqtRunsTable';
import DqtRunDetail from '../../components/data-quality/DqtRunDetail';

const DataQualityPage: React.FC = () => {
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);

  // Fetch the latest run for the summary cards
  const { data: latestRunData, isLoading: latestRunLoading } = useDqtRuns({ pageSize: 1 });
  const latestRun = latestRunData?.items?.[0] ?? null;

  const handleRunSelect = (runId: string) => {
    setSelectedRunId((prev) => (prev === runId ? null : runId));
  };

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header */}
      <div className="flex-shrink-0 flex items-start justify-between mb-4">
        <h1 className="text-lg font-semibold text-gray-900">Kvalita dat</h1>
        <RunDqtButton />
      </div>

      {/* Summary cards */}
      <div className="flex-shrink-0 mb-4">
        <DqtSummaryCards run={latestRun} isLoading={latestRunLoading} />
      </div>

      {/* Main content: runs table + detail */}
      <div className="flex-1 flex flex-col lg:flex-row gap-4 min-h-0">
        {/* Runs table */}
        <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="flex-shrink-0 px-4 py-3 border-b border-gray-200">
            <h2 className="text-sm font-semibold text-gray-700">Historie testů</h2>
          </div>
          <div className="flex-1 min-h-0 overflow-hidden flex flex-col">
            <DqtRunsTable onRunSelect={handleRunSelect} selectedRunId={selectedRunId} />
          </div>
        </div>

        {/* Detail panel */}
        <div
          className={`flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0 transition-all ${
            selectedRunId ? 'lg:flex' : 'hidden lg:flex'
          }`}
        >
          <div className="flex-shrink-0 px-4 py-3 border-b border-gray-200">
            <h2 className="text-sm font-semibold text-gray-700">Detail výsledků</h2>
          </div>
          <div className="flex-1 min-h-0 overflow-auto">
            <DqtRunDetail runId={selectedRunId} />
          </div>
        </div>
      </div>
    </div>
  );
};

export default DataQualityPage;
