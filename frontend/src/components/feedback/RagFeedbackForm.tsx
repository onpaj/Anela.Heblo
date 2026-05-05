import React, { useState } from 'react';

export type FeedbackState = 'idle' | 'submitted' | 'alreadySubmitted';

interface RagFeedbackFormProps {
  onSubmit: (data: { precisionScore: number; styleScore: number; comment: string }) => void;
  isSubmitting: boolean;
  isError: boolean;
  feedbackState: FeedbackState;
}

const SCORES = [1, 2, 3, 4, 5];

const ScoreRow: React.FC<{
  label: string;
  value: number | null;
  onChange: (v: number) => void;
}> = ({ label, value, onChange }) => (
  <div className="space-y-1">
    <span className="text-sm font-medium text-gray-700">{label}</span>
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
                : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50'
            }`}
          >
            {s}
          </span>
        </label>
      ))}
    </div>
  </div>
);

export default function RagFeedbackForm({
  onSubmit,
  isSubmitting,
  isError,
  feedbackState,
}: RagFeedbackFormProps) {
  const [precisionScore, setPrecisionScore] = useState<number | null>(null);
  const [styleScore, setStyleScore] = useState<number | null>(null);
  const [comment, setComment] = useState('');

  if (feedbackState === 'submitted') {
    return (
      <div className="border border-gray-200 rounded-lg p-4 text-sm text-green-700 bg-green-50">
        Děkujeme za vaši zpětnou vazbu.
      </div>
    );
  }

  if (feedbackState === 'alreadySubmitted') {
    return (
      <div className="border border-gray-200 rounded-lg p-4 text-sm text-gray-600 bg-gray-50">
        Zpětná vazba již byla odeslána.
      </div>
    );
  }

  const canSubmit = precisionScore !== null && styleScore !== null;

  const handleSubmit = () => {
    if (!canSubmit) return;
    onSubmit({ precisionScore: precisionScore!, styleScore: styleScore!, comment: comment.trim() });
  };

  return (
    <div className="border border-gray-200 rounded-lg p-4 space-y-3">
      <p className="text-sm font-medium text-gray-700">Ohodnoťte odpověď</p>
      <ScoreRow label="Přesnost" value={precisionScore} onChange={setPrecisionScore} />
      <ScoreRow label="Styl" value={styleScore} onChange={setStyleScore} />
      <textarea
        value={comment}
        onChange={(e) => setComment(e.target.value)}
        placeholder="Volitelný komentář..."
        rows={2}
        className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
      />
      {isError && (
        <p className="text-red-600 text-sm">Odeslání selhalo. Zkuste to znovu.</p>
      )}
      <button
        onClick={handleSubmit}
        disabled={!canSubmit || isSubmitting}
        className="px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
      >
        Odeslat zpětnou vazbu
      </button>
    </div>
  );
}
