import React from 'react';
import { X } from 'lucide-react';
import type { FeedbackDetail } from './types';
import { formatDateTime } from '../../utils/formatters';

interface Props {
  detail: FeedbackDetail;
  onClose: () => void;
  primaryLabel: string;
  secondaryLabel: string;
}

const ScoreDots: React.FC<{ score: number | null | undefined; max?: number }> = ({ score, max = 5 }) => {
  if (score == null) return <span className="text-gray-400 text-sm">–</span>;
  return (
    <span className="flex gap-1 items-center">
      {Array.from({ length: max }, (_, i) => (
        <span key={i} className={`inline-block w-3 h-3 rounded-full ${i < score ? 'bg-blue-500' : 'bg-gray-200'}`} />
      ))}
      <span className="ml-1 text-sm text-gray-700">{score}/{max}</span>
    </span>
  );
};

const GenericFeedbackDetailModal: React.FC<Props> = ({ detail, onClose, primaryLabel, secondaryLabel }) => {
  React.useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl w-[75vw] max-h-[90vh] overflow-hidden flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 flex-shrink-0">
          <h2 className="text-base font-semibold text-gray-800">Detail záznamu</h2>
          <button onClick={onClose} className="p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100" aria-label="Zavřít">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-6 space-y-5 text-sm">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Datum</p>
              <p className="text-gray-900">{formatDateTime(detail.createdAt)}</p>
            </div>
            {(detail.userName ?? detail.userId) && (
              <div>
                <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Uživatel</p>
                <p className="text-gray-900 break-all">{detail.userName ?? detail.userId}</p>
              </div>
            )}
          </div>

          <div>
            <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">{primaryLabel}</p>
            <p className="text-gray-900 whitespace-pre-wrap">{detail.primaryText}</p>
          </div>

          {detail.secondaryText && (
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">{secondaryLabel}</p>
              <p className="text-gray-700 whitespace-pre-wrap leading-relaxed">{detail.secondaryText}</p>
            </div>
          )}

          {detail.extra}

          {detail.hasFeedback && (
            <div className="border-t border-gray-100 pt-5 space-y-4">
              <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Feedback</p>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <p className="text-xs text-gray-500 mb-1">Přesnost</p>
                  <ScoreDots score={detail.precisionScore} />
                </div>
                <div>
                  <p className="text-xs text-gray-500 mb-1">Styl</p>
                  <ScoreDots score={detail.styleScore} />
                </div>
              </div>
              {detail.feedbackComment && (
                <div>
                  <p className="text-xs text-gray-500 mb-1">Komentář</p>
                  <p className="text-gray-700 whitespace-pre-wrap">{detail.feedbackComment}</p>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default GenericFeedbackDetailModal;
