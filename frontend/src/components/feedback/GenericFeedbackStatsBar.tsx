import React from 'react';
import type { GenericFeedbackStats } from './types';

interface Props {
  stats: GenericFeedbackStats | undefined;
  isLoading: boolean;
  itemLabel: string;
}

const SkeletonCard: React.FC = () => (
  <div data-testid="skeleton-card" className="bg-white border border-gray-200 rounded-lg p-4 animate-pulse">
    <div className="h-3 bg-gray-200 rounded w-24 mb-3" />
    <div className="h-7 bg-gray-200 rounded w-16" />
  </div>
);

const StatCard: React.FC<{ label: string; value: React.ReactNode }> = ({ label, value }) => (
  <div className="bg-white border border-gray-200 rounded-lg p-4">
    <p className="text-xs text-gray-500 uppercase tracking-wide">{label}</p>
    <p className="text-2xl font-semibold text-gray-900 mt-1">{value}</p>
  </div>
);

const GenericFeedbackStatsBar: React.FC<Props> = ({ stats, isLoading, itemLabel }) => {
  if (isLoading || !stats) {
    return (
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <SkeletonCard /><SkeletonCard /><SkeletonCard /><SkeletonCard />
      </div>
    );
  }

  const feedbackPct =
    stats.totalItems > 0
      ? Math.round((stats.totalWithFeedback / stats.totalItems) * 100)
      : 0;

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatCard
        label={`Celkem ${itemLabel}`}
        value={stats.totalItems}
      />
      <StatCard
        label="S feedbackem"
        value={
          <>
            {stats.totalWithFeedback}
            <span className="text-sm font-normal text-gray-500 ml-1">({feedbackPct} %)</span>
          </>
        }
      />
      <StatCard
        label="Ø Přesnost"
        value={
          stats.avgPrecisionScore !== null ? (
            <>{stats.avgPrecisionScore}<span className="text-sm font-normal text-gray-500 ml-1">/ 5</span></>
          ) : '–'
        }
      />
      <StatCard
        label="Ø Styl"
        value={
          stats.avgStyleScore !== null ? (
            <>{stats.avgStyleScore}<span className="text-sm font-normal text-gray-500 ml-1">/ 5</span></>
          ) : '–'
        }
      />
    </div>
  );
};

export default GenericFeedbackStatsBar;
