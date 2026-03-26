import React from 'react';
import { FeedbackStatsDto } from '../../api/hooks/useKnowledgeBase';

interface FeedbackStatsBarProps {
  stats: FeedbackStatsDto;
}

const FeedbackStatsBar: React.FC<FeedbackStatsBarProps> = ({ stats }) => {
  const feedbackPct =
    stats.totalQuestions > 0
      ? Math.round((stats.totalWithFeedback / stats.totalQuestions) * 100)
      : 0;

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <div className="bg-white border border-gray-200 rounded-lg p-4">
        <p className="text-xs text-gray-500 uppercase tracking-wide">Celkem dotazů</p>
        <p className="text-2xl font-semibold text-gray-900 mt-1">{stats.totalQuestions}</p>
      </div>

      <div className="bg-white border border-gray-200 rounded-lg p-4">
        <p className="text-xs text-gray-500 uppercase tracking-wide">S feedbackem</p>
        <p className="text-2xl font-semibold text-gray-900 mt-1">
          {stats.totalWithFeedback}
          <span className="text-sm font-normal text-gray-500 ml-1">({feedbackPct} %)</span>
        </p>
      </div>

      <div className="bg-white border border-gray-200 rounded-lg p-4">
        <p className="text-xs text-gray-500 uppercase tracking-wide">Ø Přesnost</p>
        <p className="text-2xl font-semibold text-gray-900 mt-1">
          {stats.avgPrecisionScore !== null ? stats.avgPrecisionScore : '–'}
          {stats.avgPrecisionScore !== null && (
            <span className="text-sm font-normal text-gray-500 ml-1">/ 5</span>
          )}
        </p>
      </div>

      <div className="bg-white border border-gray-200 rounded-lg p-4">
        <p className="text-xs text-gray-500 uppercase tracking-wide">Ø Styl</p>
        <p className="text-2xl font-semibold text-gray-900 mt-1">
          {stats.avgStyleScore !== null ? stats.avgStyleScore : '–'}
          {stats.avgStyleScore !== null && (
            <span className="text-sm font-normal text-gray-500 ml-1">/ 5</span>
          )}
        </p>
      </div>
    </div>
  );
};

export default FeedbackStatsBar;
