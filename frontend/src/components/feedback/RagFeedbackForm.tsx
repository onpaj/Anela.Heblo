import React, { useState } from 'react';
import ScoreRow from './ScoreRow';

interface RagFeedbackFormSubmitData {
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

interface RagFeedbackFormProps {
  onSubmit: (data: RagFeedbackFormSubmitData) => void;
  isSubmitting: boolean;
  isSuccess: boolean;
  alreadySubmitted: boolean;
}

const COMMENT_MAX_LENGTH = 1000;

const RagFeedbackForm: React.FC<RagFeedbackFormProps> = ({
  onSubmit,
  isSubmitting,
  isSuccess,
  alreadySubmitted,
}) => {
  const [precisionScore, setPrecisionScore] = useState<number | null>(null);
  const [styleScore, setStyleScore] = useState<number | null>(null);
  const [comment, setComment] = useState('');

  if (alreadySubmitted) {
    return (
      <div className="border border-gray-200 dark:border-graphite-border rounded-lg p-4 text-sm text-gray-600 dark:text-graphite-muted bg-gray-50 dark:bg-graphite-surface-2">
        Zpětná vazba již byla odeslána.
      </div>
    );
  }

  if (isSuccess) {
    return (
      <div className="border border-gray-200 dark:border-graphite-border rounded-lg p-4 text-sm text-green-700 dark:text-emerald-300 bg-green-50 dark:bg-emerald-900/30">
        Děkujeme za vaši zpětnou vazbu.
      </div>
    );
  }

  const canSubmit = precisionScore !== null && styleScore !== null && !isSubmitting;

  const handleSubmit = () => {
    if (!canSubmit) return;
    onSubmit({
      precisionScore: precisionScore!,
      styleScore: styleScore!,
      comment: comment.trim() || undefined,
    });
  };

  return (
    <div className="border border-gray-200 dark:border-graphite-border rounded-lg p-4 space-y-3">
      <p className="text-sm font-medium text-gray-700 dark:text-graphite-muted">Ohodnoťte odpověď</p>
      <ScoreRow label="Přesnost" value={precisionScore} onChange={setPrecisionScore} />
      <ScoreRow label="Styl" value={styleScore} onChange={setStyleScore} />
      <textarea
        value={comment}
        onChange={(e) => setComment(e.target.value.slice(0, COMMENT_MAX_LENGTH))}
        placeholder="Volitelný komentář..."
        rows={2}
        maxLength={COMMENT_MAX_LENGTH}
        className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
      />
      <button
        type="button"
        onClick={handleSubmit}
        disabled={!canSubmit}
        className="px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
      >
        Odeslat zpětnou vazbu
      </button>
    </div>
  );
};

export default RagFeedbackForm;
