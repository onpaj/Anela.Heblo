import { useState } from 'react';
import { RefreshCw, X, Database } from 'lucide-react';
import type { DraftReplySource } from './hooks/useGenerateDraftReply';
import DraftReplySourcesModal from './DraftReplySourcesModal';

interface DraftReplyToolbarProps {
  sources: DraftReplySource[];
  disabled?: boolean;
  onRegenerate: () => void;
  onDiscard: () => void;
}

function DraftReplyToolbar({ sources, disabled, onRegenerate, onDiscard }: DraftReplyToolbarProps) {
  const [showSourcesModal, setShowSourcesModal] = useState(false);

  return (
    <>
      <div className="flex items-center gap-3 text-xs">
        <span className="font-medium text-blue-600">Návrh od AI</span>
        <button
          type="button"
          disabled={disabled}
          onClick={onRegenerate}
          className="inline-flex items-center gap-1 text-gray-600 hover:text-gray-900 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <RefreshCw className="w-3.5 h-3.5" />
          Regenerovat
        </button>
        <button
          type="button"
          onClick={onDiscard}
          className="inline-flex items-center gap-1 text-gray-600 hover:text-gray-900"
        >
          <X className="w-3.5 h-3.5" />
          Zahodit
        </button>
        {sources.length > 0 && (
          <button
            type="button"
            onClick={() => setShowSourcesModal(true)}
            className="inline-flex items-center gap-1 text-gray-600 hover:text-gray-900"
          >
            <Database className="w-3.5 h-3.5" />
            Zdroj dat
          </button>
        )}
      </div>
      {showSourcesModal && (
        <DraftReplySourcesModal
          sources={sources}
          onClose={() => setShowSourcesModal(false)}
        />
      )}
    </>
  );
}

export default DraftReplyToolbar;
