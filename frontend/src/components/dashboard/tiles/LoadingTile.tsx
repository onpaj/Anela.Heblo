import React from 'react';

export const LoadingTile: React.FC = () => {
  return (
    <div className="flex items-center justify-center h-full">
      <div className="animate-pulse">
        <div className="h-4 bg-gray-200 rounded w-3/4 mb-2"></div>
        <div className="h-4 bg-gray-200 rounded w-1/2"></div>
      </div>
    </div>
  );
};
