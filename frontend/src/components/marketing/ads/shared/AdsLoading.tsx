import React from 'react';

const AdsLoading: React.FC = () => (
  <div className="flex items-center justify-center py-8" role="status">
    <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-indigo-600" />
    <span className="ml-2 text-sm text-gray-600 dark:text-graphite-muted">Načítám…</span>
  </div>
);

export default AdsLoading;
