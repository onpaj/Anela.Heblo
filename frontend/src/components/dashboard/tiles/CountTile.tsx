import React from 'react';

interface CountTileProps {
  data: {
    status?: string;
    data?: {
      count?: number;
      date?: string;
    };
    error?: string;
  };
  icon: React.ReactNode;
  iconColor?: string;
}

export const CountTile: React.FC<CountTileProps> = ({ data, icon, iconColor = 'text-indigo-600' }) => {
  // Error state
  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <div className="text-red-500 text-2xl mb-2">⚠️</div>
          <p className="text-red-600 text-sm">{data.error || 'Chyba při načítání dat'}</p>
        </div>
      </div>
    );
  }

  // Extract count from data
  const count = data.data?.count ?? 0;

  return (
    <div className="flex flex-col items-center justify-center">
      <div className={`mb-2 ${iconColor}`}>
        {icon}
      </div>
      <div className="text-3xl font-bold text-gray-900">
        {count}
      </div>
    </div>
  );
};
