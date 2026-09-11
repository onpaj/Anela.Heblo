import React from 'react';

interface CampaignSummaryCardProps {
  title: string;
  value: string;
  isLoading: boolean;
}

export const CampaignSummaryCard: React.FC<CampaignSummaryCardProps> = ({
  title,
  value,
  isLoading,
}) => {
  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 flex flex-col gap-1">
      <span className="text-sm text-gray-500 font-medium">{title}</span>
      {isLoading ? (
        <div className="h-8 bg-gray-100 rounded animate-pulse" />
      ) : (
        <span className="text-2xl font-bold text-gray-900">{value}</span>
      )}
    </div>
  );
};
