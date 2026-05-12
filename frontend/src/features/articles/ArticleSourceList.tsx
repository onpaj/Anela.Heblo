import React, { useState } from 'react';
import { ExternalLink, BookOpen, Globe } from 'lucide-react';
import { ArticleSource } from '../../api/hooks/useArticles';
import ChunkDetailModal from '../../components/knowledge-base/ChunkDetailModal';

interface ArticleSourceListProps {
  sources: ArticleSource[];
}

function SourceIcon({ type }: { type: string }) {
  if (type === 'Web') return <Globe className="w-4 h-4 text-blue-500 shrink-0" />;
  return <BookOpen className="w-4 h-4 text-green-600 shrink-0" />;
}

const ArticleSourceList: React.FC<ArticleSourceListProps> = ({ sources }) => {
  const [selectedChunkId, setSelectedChunkId] = useState<string | null>(null);

  if (sources.length === 0) return null;

  return (
    <>
      <div className="mt-6 border-t pt-4">
        <h3 className="text-sm font-semibold text-gray-700 mb-2">Zdroje</h3>
        <ul className="space-y-1">
          {sources.map((source, index) => {
            const key = `${source.type}:${source.url ?? source.knowledgeBaseChunkId ?? source.title}:${index}`;
            return (
              <li key={key} className="flex items-start gap-2 text-sm">
                <SourceIcon type={source.type} />
                {source.url ? (
                  <a
                    href={source.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-blue-600 hover:underline flex items-center gap-1"
                  >
                    {source.title}
                    <ExternalLink className="w-3 h-3" />
                  </a>
                ) : source.knowledgeBaseChunkId ? (
                  <button
                    type="button"
                    onClick={() => setSelectedChunkId(source.knowledgeBaseChunkId)}
                    className="text-green-700 hover:underline text-left"
                  >
                    {source.title}
                  </button>
                ) : (
                  <span className="text-gray-700">{source.title}</span>
                )}
              </li>
            );
          })}
        </ul>
      </div>
      {selectedChunkId && (
        <ChunkDetailModal
          chunkId={selectedChunkId}
          onClose={() => setSelectedChunkId(null)}
        />
      )}
    </>
  );
};

export default ArticleSourceList;
