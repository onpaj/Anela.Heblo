import React, { useState } from 'react';
import { MessageSquare, ChevronDown, ChevronRight } from 'lucide-react';
import { useKnowledgeBaseAskMutation } from '../../api/hooks/useKnowledgeBase';

const KnowledgeBaseAskTab: React.FC = () => {
  const [question, setQuestion] = useState('');
  const [topK, setTopK] = useState(5);
  const [sourcesExpanded, setSourcesExpanded] = useState(false);
  const askMutation = useKnowledgeBaseAskMutation();

  const handleAsk = () => {
    if (question.trim()) {
      askMutation.mutate({ question: question.trim(), topK });
      setSourcesExpanded(false);
    }
  };

  return (
    <div className="space-y-4 max-w-3xl">
      <div className="space-y-2">
        <textarea
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Zadejte svůj dotaz na firemní dokumenty..."
          rows={3}
          className="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
        />
        <div className="flex justify-between items-center">
          <div className="flex items-center gap-2 text-sm text-gray-600">
            <label htmlFor="topk-ask">Podkladové části:</label>
            <input
              id="topk-ask"
              type="number"
              min={1}
              max={20}
              value={topK}
              onChange={(e) => setTopK(Number(e.target.value))}
              className="w-16 px-2 py-1 border border-gray-300 rounded text-sm"
            />
          </div>
          <button
            onClick={handleAsk}
            disabled={askMutation.isPending || !question.trim()}
            className="px-4 py-2 bg-blue-600 text-white text-sm rounded hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2"
          >
            <MessageSquare size={16} />
            Zeptat se AI
          </button>
        </div>
      </div>

      {askMutation.isPending && (
        <div className="text-center py-8 text-gray-500">
          <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600 mx-auto mb-2" />
          AI generuje odpověď...
        </div>
      )}

      {askMutation.isError && (
        <p className="text-red-600 text-sm">Dotaz selhal. Zkuste to prosím znovu.</p>
      )}

      {askMutation.data && (
        <div className="space-y-4">
          <div className="bg-blue-50 border border-blue-200 rounded p-4">
            <p className="text-sm text-gray-900 leading-relaxed whitespace-pre-wrap">
              {askMutation.data.answer}
            </p>
          </div>

          {askMutation.data.sources.length > 0 && (
            <div className="border border-gray-200 rounded">
              <button
                onClick={() => setSourcesExpanded(!sourcesExpanded)}
                className="w-full flex items-center justify-between px-4 py-3 text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                <span>Zdroje ({askMutation.data.sources.length})</span>
                {sourcesExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
              </button>
              {sourcesExpanded && (
                <div className="border-t border-gray-200 divide-y divide-gray-100">
                  {askMutation.data.sources.map((source, idx) => (
                    <div key={idx} className="px-4 py-3">
                      <div className="flex justify-between items-start mb-1">
                        <span className="text-xs font-medium text-gray-700">{source.filename}</span>
                        <span className="text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">
                          {Math.round(source.score * 100)}%
                        </span>
                      </div>
                      <p className="text-xs text-gray-500 line-clamp-2">{source.excerpt}</p>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {!askMutation.data && !askMutation.isPending && !askMutation.isError && (
        <p className="text-gray-400 text-sm py-8 text-center">
          Zadejte dotaz a AI vyhledá odpověď ve firemních dokumentech.
        </p>
      )}
    </div>
  );
};

export default KnowledgeBaseAskTab;
