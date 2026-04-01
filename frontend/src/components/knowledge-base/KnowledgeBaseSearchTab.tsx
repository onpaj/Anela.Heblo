import React, { useState } from 'react';
import { Search, ExternalLink } from 'lucide-react';
import {
  useKnowledgeBaseSearchMutation,
  ChunkResult,
} from '../../api/hooks/useKnowledgeBase';
import ChunkDetailModal from './ChunkDetailModal';

const ScoreBadge: React.FC<{ score: number }> = ({ score }) => {
  const pct = Math.round(score * 100);
  const color =
    pct >= 80
      ? 'bg-green-100 text-green-800'
      : pct >= 60
      ? 'bg-yellow-100 text-yellow-800'
      : 'bg-gray-100 text-gray-600';
  return (
    <span className={`text-xs px-1.5 py-0.5 rounded font-medium ${color}`}>
      {pct}%
    </span>
  );
};

interface ChunkCardProps {
  chunk: ChunkResult;
  onViewSource: (chunkId: string, score: number) => void;
}

const ChunkCard: React.FC<ChunkCardProps> = ({ chunk, onViewSource }) => (
  <div className="border border-gray-200 rounded-lg p-4 space-y-2">
    <div className="flex items-center justify-between">
      <span className="text-sm font-medium text-gray-700">{chunk.sourceFilename}</span>
      <div className="flex items-center gap-2">
        <ScoreBadge score={chunk.score} />
        <button
          onClick={() => onViewSource(chunk.chunkId, chunk.score)}
          className="flex items-center gap-1 text-xs text-blue-600 hover:text-blue-800"
          aria-label={`Zobrazit zdroj ${chunk.sourceFilename}`}
        >
          <ExternalLink className="w-3 h-3" />
          Zobrazit zdroj
        </button>
      </div>
    </div>
    <p className="text-sm text-gray-600 whitespace-pre-wrap line-clamp-5">{chunk.content}</p>
  </div>
);

const KnowledgeBaseSearchTab: React.FC = () => {
  const [query, setQuery] = useState('');
  const [selectedChunkId, setSelectedChunkId] = useState<string | null>(null);
  const [selectedScore, setSelectedScore] = useState<number | undefined>(undefined);
  const search = useKnowledgeBaseSearchMutation();

  const handleSearch = () => {
    if (query.trim()) {
      search.mutate({ query: query.trim() });
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleSearch();
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
      <div className="flex gap-2">
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Hledat v znalostní bázi..."
          className="flex-1 border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
        <button
          onClick={handleSearch}
          disabled={search.isPending || !query.trim()}
          className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
        >
          <Search className="w-4 h-4" />
          Hledat
        </button>
      </div>

      {search.isPending && (
        <div className="space-y-2 animate-pulse">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-24 bg-gray-100 rounded-lg" />
          ))}
        </div>
      )}

      {search.isError && (
        <div className="text-red-600 text-sm">Vyhledávání se nezdařilo. Zkuste to znovu.</div>
      )}

      {search.data && (
        <div className="space-y-3">
          {search.data.chunks.length === 0 ? (
            <p className="text-sm text-gray-500 text-center py-6">
              Žádné výsledky pro „{query}".
            </p>
          ) : (
            search.data.chunks.map((chunk) => (
              <ChunkCard key={chunk.chunkId} chunk={chunk} onViewSource={handleViewSource} />
            ))
          )}
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

export default KnowledgeBaseSearchTab;
