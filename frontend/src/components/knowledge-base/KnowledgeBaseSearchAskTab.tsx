import React, { useState } from 'react';
import ReactMarkdown from 'react-markdown';
import { Search, ChevronDown, ChevronUp } from 'lucide-react';
import {
  useKnowledgeBaseAskMutation,
  useSubmitFeedbackMutation,
  SourceReference,
} from '../../api/hooks/useKnowledgeBase';
import ChunkDetailModal from './ChunkDetailModal';
import RagFeedbackForm from '../feedback/RagFeedbackForm';

interface SourceAccordionProps {
  sources: SourceReference[];
  onViewSource: (chunkId: string, score: number) => void;
}

const SourceAccordion: React.FC<SourceAccordionProps> = ({ sources, onViewSource }) => {
  const [open, setOpen] = useState(true);
  if (sources.length === 0) return null;

  return (
    <div className="border border-gray-200 rounded-lg overflow-hidden">
      <button
        onClick={() => setOpen((v) => !v)}
        className="w-full flex items-center justify-between px-4 py-2 bg-gray-50 text-sm font-medium text-gray-700 hover:bg-gray-100"
      >
        <span>Zdroje ({sources.length})</span>
        {open ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
      </button>
      {open && (
        <div className="divide-y divide-gray-100">
          {sources.map((src) => (
            <div
              key={src.chunkId}
              className="px-4 py-3 space-y-1 cursor-pointer hover:bg-gray-50"
              onClick={() => onViewSource(src.chunkId, src.score)}
              role="button"
              tabIndex={0}
              onKeyDown={(e) => e.key === 'Enter' && onViewSource(src.chunkId, src.score)}
              aria-label={`Zobrazit zdroj ${src.filename}`}
            >
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-gray-700">{src.filename}</span>
                <span className="text-xs px-1.5 py-0.5 rounded font-medium bg-gray-100 text-gray-600">
                  {Math.round(src.score * 100)}%
                </span>
              </div>
              <p className="text-xs text-gray-500 italic line-clamp-3">{src.excerpt}</p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

const KnowledgeBaseFeedback: React.FC<{ logId: string }> = ({ logId }) => {
  const submitFeedback = useSubmitFeedbackMutation();
  const [alreadySubmitted, setAlreadySubmitted] = useState(false);

  return (
    <RagFeedbackForm
      onSubmit={(data) =>
        submitFeedback.mutate(
          {
            logId,
            precisionScore: data.precisionScore,
            styleScore: data.styleScore,
            comment: data.comment,
          },
          {
            onSuccess: (result) => {
              if (result.alreadySubmitted) setAlreadySubmitted(true);
            },
          },
        )
      }
      isSubmitting={submitFeedback.isPending}
      isSuccess={submitFeedback.isSuccess && !submitFeedback.data?.alreadySubmitted}
      alreadySubmitted={alreadySubmitted}
    />
  );
};

const KnowledgeBaseSearchAskTab: React.FC = () => {
  const [query, setQuery] = useState('');
  const [selectedChunkId, setSelectedChunkId] = useState<string | null>(null);
  const [selectedScore, setSelectedScore] = useState<number | undefined>(undefined);
  const ask = useKnowledgeBaseAskMutation();

  const handleViewSource = (chunkId: string, score: number) => {
    setSelectedChunkId(chunkId);
    setSelectedScore(score);
  };

  const handleCloseModal = () => {
    setSelectedChunkId(null);
    setSelectedScore(undefined);
  };

  const handleSubmit = () => {
    if (query.trim()) ask.mutate({ question: query.trim() });
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  };

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <textarea
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Zadejte otázku nebo hledaný výraz... (Enter pro odeslání)"
          rows={3}
          className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
        />
        <button
          onClick={handleSubmit}
          disabled={ask.isPending || !query.trim()}
          className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
        >
          <Search className="w-4 h-4" />
          Hledat
        </button>
      </div>

      {ask.isPending && (
        <div className="space-y-2 animate-pulse">
          <div className="h-4 bg-gray-100 rounded w-3/4" />
          <div className="h-4 bg-gray-100 rounded w-full" />
          <div className="h-4 bg-gray-100 rounded w-2/3" />
        </div>
      )}

      {ask.isError && (
        <div className="text-red-600 text-sm">Dotaz se nezdařil. Zkuste to znovu.</div>
      )}

      {ask.data && (
        <div className="space-y-4">
          <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 prose prose-sm prose-blue max-w-none">
            <ReactMarkdown>{ask.data.answer}</ReactMarkdown>
          </div>
          <SourceAccordion sources={ask.data.sources} onViewSource={handleViewSource} />
          {ask.data.id && <KnowledgeBaseFeedback logId={ask.data.id} />}
        </div>
      )}

      {selectedChunkId && (
        <ChunkDetailModal
          chunkId={selectedChunkId}
          score={selectedScore}
          onClose={handleCloseModal}
        />
      )}
    </div>
  );
};

export default KnowledgeBaseSearchAskTab;
