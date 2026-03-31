import React from 'react';
import { X } from 'lucide-react';
import { useChunkDetailQuery } from '../../api/hooks/useKnowledgeBase';
import { formatDateTime } from '../../utils/formatters';

interface ChunkDetailModalProps {
  chunkId: string;
  score?: number;
  onClose: () => void;
}

const ChunkDetailModal: React.FC<ChunkDetailModalProps> = ({ chunkId, score, onClose }) => {
  const { data, isLoading, isError } = useChunkDetailQuery(chunkId);

  React.useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  return (
    <div
      role='dialog'
      aria-modal='true'
      className='fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50'
    >
      <div className='bg-white rounded-lg shadow-xl w-[75vw] max-h-[90vh] overflow-hidden flex flex-col'>
        {/* Header */}
        <div className='flex items-center justify-between px-6 py-4 border-b border-gray-200 flex-shrink-0'>
          <h2 className='text-base font-semibold text-gray-800 truncate'>
            {data?.filename ?? 'Zdroj'}
          </h2>
          <button
            onClick={onClose}
            className='p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 flex-shrink-0'
            aria-label='Zavřít'
          >
            <X className='w-5 h-5' />
          </button>
        </div>

        {/* Body */}
        <div className='flex-1 overflow-y-auto p-6 space-y-5 text-sm'>
          {isLoading && (
            <div className='space-y-3 animate-pulse'>
              <div className='h-4 bg-gray-100 rounded w-1/3' />
              <div className='h-20 bg-gray-100 rounded' />
              <div className='h-4 bg-gray-100 rounded w-1/4' />
              <div className='h-40 bg-gray-100 rounded' />
            </div>
          )}

          {isError && (
            <p className='text-red-600'>Zdroj se nepodařilo načíst.</p>
          )}

          {data && (
            <>
              {/* Meta row */}
              <div className='flex items-center gap-3 flex-wrap text-xs text-gray-500'>
                <span className='px-2 py-0.5 rounded-full bg-blue-100 text-blue-700 font-medium'>
                  {data.documentType === 'Conversation' ? 'Konverzace' : 'Dokument'}
                </span>
                {data.indexedAt && (
                  <span>Indexováno: {formatDateTime(data.indexedAt)}</span>
                )}
                {score !== undefined && (
                  <span className='font-medium text-gray-700'>
                    {Math.round(score * 100)}%
                  </span>
                )}
              </div>

              {/* Summary */}
              <div>
                <p className='text-xs text-gray-500 uppercase tracking-wide mb-2'>Shrnutí</p>
                <div className='bg-blue-50 border border-blue-200 rounded-lg px-4 py-3'>
                  <p className='text-gray-800 whitespace-pre-wrap leading-relaxed'>
                    {data.summary}
                  </p>
                </div>
              </div>

              {/* Content */}
              <div>
                <p className='text-xs text-gray-500 uppercase tracking-wide mb-2'>Obsah</p>
                <p className='text-gray-700 whitespace-pre-wrap leading-relaxed'>
                  {data.content}
                </p>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
};

export default ChunkDetailModal;
