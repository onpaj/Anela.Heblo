import React from 'react';
import { X } from 'lucide-react';
import { FeedbackLogSummary } from '../../api/hooks/useKnowledgeBase';

interface FeedbackDetailPanelProps {
  log: FeedbackLogSummary;
  onClose: () => void;
}

const ScoreDots: React.FC<{ score: number | null; max?: number }> = ({ score, max = 5 }) => {
  if (score === null) return <span className="text-gray-400 text-sm">–</span>;
  return (
    <span className="flex gap-1">
      {Array.from({ length: max }, (_, i) => (
        <span
          key={i}
          className={`inline-block w-3 h-3 rounded-full ${i < score ? 'bg-blue-500' : 'bg-gray-200'}`}
        />
      ))}
      <span className="ml-1 text-sm text-gray-700">{score}/{max}</span>
    </span>
  );
};

const FeedbackDetailPanel: React.FC<FeedbackDetailPanelProps> = ({ log, onClose }) => {
  const formatDate = (iso: string) =>
    new Date(iso).toLocaleString('cs-CZ', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });

  return (
    <div className="h-full flex flex-col bg-white border-l border-gray-200">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200">
        <h2 className="text-sm font-semibold text-gray-800">Detail záznamu</h2>
        <button
          onClick={onClose}
          className="p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100"
          aria-label="Zavřít"
        >
          <X className="w-4 h-4" />
        </button>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4 text-sm">
        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Datum</p>
          <p className="text-gray-900">{formatDate(log.createdAt)}</p>
        </div>

        {log.userId && (
          <div>
            <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Uživatel</p>
            <p className="text-gray-900 break-all">{log.userId}</p>
          </div>
        )}

        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Dotaz</p>
          <p className="text-gray-900 whitespace-pre-wrap">{log.question}</p>
        </div>

        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Odpověď</p>
          <p className="text-gray-700 whitespace-pre-wrap leading-relaxed">{log.answer}</p>
        </div>

        <div className="flex gap-6">
          <div>
            <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">TopK</p>
            <p className="text-gray-900">{log.topK}</p>
          </div>
          <div>
            <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Zdrojů</p>
            <p className="text-gray-900">{log.sourceCount}</p>
          </div>
          <div>
            <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Doba odezvy</p>
            <p className="text-gray-900">{log.durationMs} ms</p>
          </div>
        </div>

        {log.hasFeedback && (
          <div className="border-t border-gray-100 pt-4 space-y-3">
            <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Feedback</p>

            <div>
              <p className="text-xs text-gray-500 mb-1">Přesnost</p>
              <ScoreDots score={log.precisionScore} />
            </div>

            <div>
              <p className="text-xs text-gray-500 mb-1">Styl</p>
              <ScoreDots score={log.styleScore} />
            </div>

            {log.feedbackComment && (
              <div>
                <p className="text-xs text-gray-500 mb-1">Komentář</p>
                <p className="text-gray-700 whitespace-pre-wrap">{log.feedbackComment}</p>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};

export default FeedbackDetailPanel;
