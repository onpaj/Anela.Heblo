import React, { useState } from 'react';
import { MessageSquare, ChevronDown, ChevronUp, ExternalLink } from 'lucide-react';
import {
  useKnowledgeBaseAskMutation,
  SourceReference,
} from '../../api/hooks/useKnowledgeBase';
import ChunkDetailModal from './ChunkDetailModal';

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
            <div key={src.chunkId} className="px-4 py-3 space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-gray-700">{src.filename}</span>
                <div className="flex items-center gap-2">
                  <span className="text-xs text-gray-400">
                    {Math.round(src.score * 100)}%
                  </span>
                  <button
                    onClick={() => onViewSource(src.chunkId, src.score)}
                    className="flex items-center gap-1 text-xs text-blue-600 hover:text-blue-800"
                    aria-label={`Zobrazit zdroj ${src.filename}`}
                  >
                    <ExternalLink className="w-3 h-3" />
                    Zobrazit zdroj
                  </button>
                </div>
              </div>
              <p className="text-xs text-gray-500 italic line-clamp-3">{src.excerpt}</p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

const KnowledgeBaseAskTab: React.FC = () => {
  const [question, setQuestion] = useState('');
  const [selectedChunkId, setSelectedChunkId] = useState<string | null>(null);
  const [selectedScore, setSelectedScore] = useState<number | undefined>(undefined);
  const ask = useKnowledgeBaseAskMutation();

  const handleAsk = () => {
    if (question.trim()) {
      ask.mutate({ question: question.trim() });
    }
  };

  const handleViewSource = (chunkId: string, score: number) => {
    setSelectedChunkId(chunkId);
    setSelectedScore(score);
  };

  const handleCloseModal = () => {
    setSelectedChunkId(null);
    setSelectedScore(undefined);
  };

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <textarea
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Zadejte otázku k firemním dokumentům..."
          rows={3}
          className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
        />
        <button
          onClick={handleAsk}
          disabled={ask.isPending || !question.trim()}
          className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
        >
          <MessageSquare className="w-4 h-4" />
          Zeptat se
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
          <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
            <p className="text-sm text-gray-800 whitespace-pre-wrap">{ask.data.answer}</p>
          </div>
          <SourceAccordion sources={ask.data.sources} onViewSource={handleViewSource} />
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

export default KnowledgeBaseAskTab;
