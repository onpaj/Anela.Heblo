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

type TestTypeOption = {
  value: string;
  label: string;
};

const TEST_TYPE_OPTIONS: TestTypeOption[] = [
  { value: 'IssuedInvoiceComparison', label: 'Porovnání faktur' },
  { value: 'ProductPairing', label: 'Párování produktů' },
  { value: 'StockWriteBackReconciliation', label: 'Zpětný zápis skladu' },
];

const RunDqtButton: React.FC = () => {
  const defaults = getDefaultDates();
  const [showDatePicker, setShowDatePicker] = useState(false);
  const [dateFrom, setDateFrom] = useState(defaults.dateFrom);
  const [dateTo, setDateTo] = useState(defaults.dateTo);
  const [testType, setTestType] = useState('IssuedInvoiceComparison');
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(
    null,
  );

  const { mutate, isPending } = useRunDqt();

  const handleRun = () => {
    setFeedback(null);
    mutate(
      { testType, dateFrom, dateTo },
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
        {/* Test type selector */}
        <select
          value={testType}
          onChange={(e) => setTestType(e.target.value)}
          className="border border-gray-300 dark:border-graphite-border rounded-md px-3 py-2 text-sm focus:ring-indigo-500 focus:border-indigo-500 bg-white dark:bg-graphite-surface-2 dark:text-graphite-text"
        >
          {TEST_TYPE_OPTIONS.map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))}
        </select>

        {/* Toggle date picker */}
        <button
          type="button"
          onClick={() => setShowDatePicker((v) => !v)}
          className="flex items-center gap-1 text-sm text-gray-600 dark:text-graphite-muted hover:text-gray-900 dark:hover:text-graphite-text border border-gray-300 dark:border-graphite-border rounded-md px-3 py-2 hover:bg-gray-50 dark:hover:bg-white/5 transition-colors"
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
        <div className="flex items-center gap-3 bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg p-3 shadow-sm dark:shadow-soft-dark">
          <label className="text-sm text-gray-700 dark:text-graphite-muted font-medium">Od</label>
          <input
            type="date"
            value={dateFrom}
            onChange={(e) => setDateFrom(e.target.value)}
            className="border border-gray-300 dark:border-graphite-border rounded-md px-2 py-1 text-sm focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text"
          />
          <label className="text-sm text-gray-700 dark:text-graphite-muted font-medium">Do</label>
          <input
            type="date"
            value={dateTo}
            onChange={(e) => setDateTo(e.target.value)}
            className="border border-gray-300 dark:border-graphite-border rounded-md px-2 py-1 text-sm focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:text-graphite-text"
          />
        </div>
      )}

      {/* Feedback */}
      {feedback && (
        <div
          className={`flex items-center gap-2 text-sm px-3 py-2 rounded-md ${
            feedback.type === 'success'
              ? 'bg-green-50 dark:bg-emerald-900/30 text-green-700 dark:text-emerald-300 border border-green-200 dark:border-emerald-900/40'
              : 'bg-red-50 dark:bg-red-900/30 text-red-700 dark:text-red-300 border border-red-200 dark:border-red-900/40'
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
