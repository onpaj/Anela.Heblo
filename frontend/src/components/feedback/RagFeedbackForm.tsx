import { useState } from 'react';

const SCORES = [1, 2, 3, 4, 5];

interface ScoreRowProps {
  label: string;
  value: number | null;
  onChange: (v: number) => void;
}

const ScoreRow: React.FC<ScoreRowProps> = ({ label, value, onChange }) => (
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

interface SubmitPayload {
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

interface RagFeedbackFormProps {
  onSubmit: (payload: SubmitPayload) => void;
  isSubmitting: boolean;
  alreadySubmitted: boolean;
  isSuccess: boolean;
}

export default function RagFeedbackForm({
  onSubmit,
  isSubmitting,
  alreadySubmitted,
  isSuccess,
}: RagFeedbackFormProps) {
  const [precisionScore, setPrecisionScore] = useState<number | null>(null);
  const [styleScore, setStyleScore] = useState<number | null>(null);
  const [comment, setComment] = useState('');

  if (isSuccess) {
    return (
      <div className="border border-gray-200 rounded-lg p-4 text-sm text-green-700 bg-green-50">
        Děkujeme za vaši zpětnou vazbu.
      </div>
    );
  }

  if (alreadySubmitted) {
    return (
      <div className="border border-gray-200 rounded-lg p-4 text-sm text-gray-600 bg-gray-50">
        Zpětná vazba již byla odeslána.
      </div>
    );
  }

  const canSubmit = precisionScore !== null && styleScore !== null;

  const handleSubmit = () => {
    if (!canSubmit) return;
    onSubmit({
      precisionScore: precisionScore!,
      styleScore: styleScore!,
      comment: comment.trim() || undefined,
    });
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
