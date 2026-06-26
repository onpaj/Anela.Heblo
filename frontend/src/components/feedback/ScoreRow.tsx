import React from 'react';

const SCORES = [1, 2, 3, 4, 5];

interface ScoreRowProps {
  label: string;
  value: number | null;
  onChange: (v: number) => void;
}

const ScoreRow: React.FC<ScoreRowProps> = ({ label, value, onChange }) => (
  <div className="space-y-1">
    <span className="text-sm font-medium text-gray-700 dark:text-graphite-muted">{label}</span>
    <div className="flex gap-1 flex-wrap">
      {SCORES.map((s) => (
        <label key={s} className="cursor-pointer">
          <input
            type="radio"
            name={label}
            value={s}
            checked={value === s}
            onChange={() => onChange(s)}
            className="sr-only"
          />
          <span
            className={`inline-flex items-center justify-center w-8 h-8 rounded text-sm font-medium border ${
              value === s
                ? 'bg-blue-600 text-white border-blue-600'
                : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50 dark:bg-graphite-surface-2 dark:text-graphite-muted dark:border-graphite-border dark:hover:bg-white/5'
            }`}
          >
            {s}
          </span>
        </label>
      ))}
    </div>
  </div>
);

export default ScoreRow;
