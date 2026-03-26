import React from 'react';
import { GetFeedbackListParams } from '../../api/hooks/useKnowledgeBase';

interface FeedbackFiltersProps {
  params: GetFeedbackListParams;
  onParamsChange: (params: GetFeedbackListParams) => void;
}

const FeedbackFilters: React.FC<FeedbackFiltersProps> = ({ params, onParamsChange }) => {
  const handleHasFeedbackChange = (value: string) => {
    onParamsChange({
      ...params,
      pageNumber: 1,
      hasFeedback: value === '' ? undefined : value === 'true',
    });
  };

  const handleSortByChange = (value: string) => {
    onParamsChange({ ...params, pageNumber: 1, sortBy: value });
  };

  const handleSortDescendingChange = (value: string) => {
    onParamsChange({ ...params, pageNumber: 1, sortDescending: value === 'true' });
  };

  const handlePageSizeChange = (value: string) => {
    onParamsChange({ ...params, pageNumber: 1, pageSize: parseInt(value, 10) });
  };

  return (
    <div className="flex flex-wrap gap-3 items-center">
      <div className="flex items-center gap-2">
        <label className="text-sm text-gray-600 whitespace-nowrap">Feedback:</label>
        <select
          value={params.hasFeedback === undefined ? '' : String(params.hasFeedback)}
          onChange={(e) => handleHasFeedbackChange(e.target.value)}
          className="border border-gray-300 rounded-md text-sm px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="">Vše</option>
          <option value="true">Pouze s feedbackem</option>
          <option value="false">Pouze bez feedbacku</option>
        </select>
      </div>

      <div className="flex items-center gap-2">
        <label className="text-sm text-gray-600 whitespace-nowrap">Řadit dle:</label>
        <select
          value={params.sortBy ?? 'CreatedAt'}
          onChange={(e) => handleSortByChange(e.target.value)}
          className="border border-gray-300 rounded-md text-sm px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="CreatedAt">Datum</option>
          <option value="PrecisionScore">Přesnost</option>
          <option value="StyleScore">Styl</option>
        </select>
      </div>

      <div className="flex items-center gap-2">
        <label className="text-sm text-gray-600 whitespace-nowrap">Pořadí:</label>
        <select
          value={params.sortDescending === false ? 'false' : 'true'}
          onChange={(e) => handleSortDescendingChange(e.target.value)}
          className="border border-gray-300 rounded-md text-sm px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="true">Sestupně</option>
          <option value="false">Vzestupně</option>
        </select>
      </div>

      <div className="flex items-center gap-2">
        <label className="text-sm text-gray-600 whitespace-nowrap">Na stránce:</label>
        <select
          value={String(params.pageSize ?? 20)}
          onChange={(e) => handlePageSizeChange(e.target.value)}
          className="border border-gray-300 rounded-md text-sm px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="10">10</option>
          <option value="20">20</option>
          <option value="50">50</option>
        </select>
      </div>
    </div>
  );
};

export default FeedbackFilters;
