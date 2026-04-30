import React, { useState } from 'react';
import { Play, Loader2, ChevronDown, ChevronUp, CheckCircle, AlertCircle } from 'lucide-react';
import { useRunDqt } from '../../api/hooks/useDataQuality';

const formatDate = (date: Date): string => {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
};

const getDefaultDates = () => {
  const today = new Date();
  const thirtyDaysAgo = new Date(today);
  thirtyDaysAgo.setDate(today.getDate() - 30);
  return {
    dateFrom: formatDate(thirtyDaysAgo),
    dateTo: formatDate(today),
  };
};

const RunDqtButton: React.FC = () => {
  const defaults = getDefaultDates();
  const [showDatePicker, setShowDatePicker] = useState(false);
  const [dateFrom, setDateFrom] = useState(defaults.dateFrom);
  const [dateTo, setDateTo] = useState(defaults.dateTo);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(
    null,
  );

  const { mutate, isPending } = useRunDqt();

  const handleRun = () => {
    setFeedback(null);
    mutate(
      { dateFrom, dateTo },
      {
        onSuccess: () => {
          setFeedback({ type: 'success', message: 'DQT test byl spuštěn.' });
          setTimeout(() => setFeedback(null), 4000);
        },
        onError: (err: Error) => {
          setFeedback({ type: 'error', message: err.message || 'Chyba při spuštění DQT.' });
        },
      },
    );
  };

  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex items-center gap-2">
        {/* Toggle date picker */}
        <button
          type="button"
          onClick={() => setShowDatePicker((v) => !v)}
          className="flex items-center gap-1 text-sm text-gray-600 hover:text-gray-900 border border-gray-300 rounded-md px-3 py-2 hover:bg-gray-50 transition-colors"
        >
          Vlastní období
          {showDatePicker ? (
            <ChevronUp className="h-4 w-4" />
          ) : (
            <ChevronDown className="h-4 w-4" />
          )}
        </button>

        {/* Run button */}
        <button
          type="button"
          onClick={handleRun}
          disabled={isPending}
          className="flex items-center gap-2 bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isPending ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              Spouštím...
            </>
          ) : (
            <>
              <Play className="h-4 w-4" />
              Spustit DQT
            </>
          )}
        </button>
      </div>

      {/* Date picker */}
      {showDatePicker && (
        <div className="flex items-center gap-3 bg-white border border-gray-200 rounded-lg p-3 shadow-sm">
          <label className="text-sm text-gray-700 font-medium">Od</label>
          <input
            type="date"
            value={dateFrom}
            onChange={(e) => setDateFrom(e.target.value)}
            className="border border-gray-300 rounded-md px-2 py-1 text-sm focus:ring-indigo-500 focus:border-indigo-500"
          />
          <label className="text-sm text-gray-700 font-medium">Do</label>
          <input
            type="date"
            value={dateTo}
            onChange={(e) => setDateTo(e.target.value)}
            className="border border-gray-300 rounded-md px-2 py-1 text-sm focus:ring-indigo-500 focus:border-indigo-500"
          />
        </div>
      )}

      {/* Feedback */}
      {feedback && (
        <div
          className={`flex items-center gap-2 text-sm px-3 py-2 rounded-md ${
            feedback.type === 'success'
              ? 'bg-green-50 text-green-700 border border-green-200'
              : 'bg-red-50 text-red-700 border border-red-200'
          }`}
        >
          {feedback.type === 'success' ? (
            <CheckCircle className="h-4 w-4" />
          ) : (
            <AlertCircle className="h-4 w-4" />
          )}
          {feedback.message}
        </div>
      )}
    </div>
  );
};

export default RunDqtButton;
