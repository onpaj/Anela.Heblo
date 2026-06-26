import { useState, useEffect } from 'react';
import { X } from 'lucide-react';
import type { DraftReplySource } from './hooks/useGenerateDraftReply';
import ChunkDetailModal from '../../knowledge-base/ChunkDetailModal';

interface DraftReplySourcesModalProps {
  sources: DraftReplySource[];
  onClose: () => void;
}

function DraftReplySourcesModal({ sources, onClose }: DraftReplySourcesModalProps) {
  const [selectedSource, setSelectedSource] = useState<DraftReplySource | null>(null);

  useEffect(() => {
    if (selectedSource) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose, selectedSource]);

  return (
    <>
      <div
        role="dialog"
        aria-modal="true"
        className="fixed inset-0 bg-black/50 flex items-center justify-center z-40"
      >
        <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark w-[600px] max-h-[80vh] overflow-hidden flex flex-col">
          <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 dark:border-graphite-border flex-shrink-0">
            <h2 className="text-base font-semibold text-gray-800 dark:text-graphite-text">Zdroj dat</h2>
            <button
              onClick={onClose}
              className="p-1 rounded-md text-gray-400 dark:text-graphite-faint hover:text-gray-600 hover:bg-gray-100 dark:hover:bg-white/5"
              aria-label="Zavřít"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
          <div className="flex-1 overflow-y-auto divide-y divide-gray-100 dark:divide-graphite-border">
            {sources.map((source) => (
              <div
                key={source.chunkId}
                className="px-6 py-4 space-y-1 cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5"
                onClick={() => setSelectedSource(source)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && setSelectedSource(source)}
                aria-label={`Zobrazit zdroj ${source.filename}`}
              >
                <div className="flex items-center justify-between">
                  <span className="text-sm font-medium text-gray-700 dark:text-graphite-muted">{source.filename}</span>
                  <span className="text-xs px-1.5 py-0.5 rounded font-medium bg-gray-100 dark:bg-graphite-surface-2 text-gray-600 dark:text-graphite-muted">
                    {Math.round(source.score * 100)}%
                  </span>
                </div>
                <p className="text-xs text-gray-500 dark:text-graphite-muted italic line-clamp-3">{source.excerpt}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
      {selectedSource && (
        <ChunkDetailModal
          chunkId={selectedSource.chunkId}
          score={selectedSource.score}
          onClose={() => setSelectedSource(null)}
        />
      )}
    </>
  );
}

export default DraftReplySourcesModal;
