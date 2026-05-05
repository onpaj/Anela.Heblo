import React from 'react';
import { MessageSquare } from 'lucide-react';

const MarketingFeedbackPage: React.FC = () => {
  return (
    <div className="flex flex-col h-full">
      {/* Page header */}
      <div className="px-6 py-4 border-b border-gray-200 flex items-center gap-3 flex-shrink-0">
        <MessageSquare className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Feedback</h1>
      </div>

      {/* Main content */}
      <div className="flex-1 overflow-y-auto p-6">
        <p className="text-sm text-gray-600">
          Přehled zpětné vazby bude dostupný po dokončení integrace.
        </p>
      </div>
    </div>
  );
};

export default MarketingFeedbackPage;
