import React from 'react';

interface BackgroundTasksTileProps {
  data: {
    completed?: number;
    total?: number;
    status?: string;
  };
}

export const BackgroundTasksTile: React.FC<BackgroundTasksTileProps> = ({ data }) => {
  const { completed = 0, total = 0, status = '0/0' } = data;

  return (
    <div className="flex flex-col items-center justify-center">
      <div className="text-3xl font-bold text-blue-600 mb-2">
        {status}
      </div>
      <div className="text-sm text-gray-600 text-center">
        Background úlohy
      </div>
      {total > 0 && (
        <div className="w-full bg-gray-200 rounded-full h-2 mt-3">
          <div
            className="bg-blue-600 h-2 rounded-full transition-all"
            style={{ width: `${(completed / total) * 100}%` }}
          ></div>
        </div>
      )}
    </div>
  );
};
