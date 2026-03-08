import React, { useState } from 'react';
import { Search } from 'lucide-react';
import { useKnowledgeBaseSearchMutation } from '../../api/hooks/useKnowledgeBase';

const KnowledgeBaseSearchTab: React.FC = () => {
  const [query, setQuery] = useState('');
  const [topK, setTopK] = useState(5);
  const searchMutation = useKnowledgeBaseSearchMutation();

  const handleSearch = () => {
    if (query.trim()) {
      searchMutation.mutate({ query: query.trim(), topK });
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex gap-3">
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
          placeholder="Hledaný výraz..."
          className="flex-1 px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
        <div className="flex items-center gap-2 text-sm text-gray-600">
          <label htmlFor="topk-search">Výsledky:</label>
          <input
            id="topk-search"
            type="number"
            min={1}
            max={20}
            value={topK}
            onChange={(e) => setTopK(Number(e.target.value))}
            className="w-16 px-2 py-2 border border-gray-300 rounded text-sm"
          />
        </div>
        <button
          onClick={handleSearch}
          disabled={searchMutation.isPending || !query.trim()}
          className="px-4 py-2 bg-blue-600 text-white text-sm rounded hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2"
        >
          <Search size={16} />
          Hledat
        </button>
      </div>

      {searchMutation.isPending && (
        <div className="text-center py-8 text-gray-500">
          <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600 mx-auto mb-2" />
          Vyhledávám...
        </div>
      )}

      {searchMutation.isError && (
        <p className="text-red-600 text-sm">Vyhledávání selhalo. Zkuste to prosím znovu.</p>
      )}

      {searchMutation.data && (
        <div className="space-y-3">
          {searchMutation.data.chunks.length === 0 ? (
            <p className="text-gray-500 text-sm py-4">Žádné výsledky.</p>
          ) : (
            searchMutation.data.chunks.map((chunk) => (
              <div key={chunk.chunkId} className="border border-gray-200 rounded p-4">
                <div className="flex justify-between items-start mb-2">
                  <span className="text-xs text-gray-500">{chunk.sourceFilename}</span>
                  <span className="text-xs bg-blue-100 text-blue-800 px-2 py-0.5 rounded-full font-medium">
                    {Math.round(chunk.score * 100)}% shoda
                  </span>
                </div>
                <p className="text-sm text-gray-800 leading-relaxed">{chunk.content}</p>
              </div>
            ))
          )}
        </div>
      )}

      {!searchMutation.data && !searchMutation.isPending && !searchMutation.isError && (
        <p className="text-gray-400 text-sm py-8 text-center">Zadejte výraz pro zahájení vyhledávání.</p>
      )}
    </div>
  );
};

export default KnowledgeBaseSearchTab;
