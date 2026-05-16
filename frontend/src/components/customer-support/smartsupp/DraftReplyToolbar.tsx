import { useState } from "react";
import { RefreshCw, X, Info } from "lucide-react";
import type { DraftReplySource } from "./hooks/useGenerateDraftReply";

interface DraftReplyToolbarProps {
  sources: DraftReplySource[];
  disabled?: boolean;
  onRegenerate: () => void;
  onDiscard: () => void;
}

function DraftReplyToolbar({ sources, disabled, onRegenerate, onDiscard }: DraftReplyToolbarProps) {
  const [showSources, setShowSources] = useState(false);

  return (
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
        <div className="relative">
          <button
            type="button"
            aria-label="Zdroje"
            onMouseEnter={() => setShowSources(true)}
            onMouseLeave={() => setShowSources(false)}
            onFocus={() => setShowSources(true)}
            onBlur={() => setShowSources(false)}
            className="inline-flex items-center text-gray-400 hover:text-gray-600"
          >
            <Info className="w-3.5 h-3.5" />
          </button>
          {showSources && (
            <div
              role="tooltip"
              className="absolute bottom-full left-0 mb-1 w-64 rounded-md bg-gray-800 p-2 text-white shadow-lg z-10"
            >
              <p className="mb-1 font-medium">Zdroje z databáze znalostí:</p>
              <ul className="list-inside list-disc space-y-0.5">
                {sources.map((source, index) => (
                  <li key={`${source.documentId}-${index}`}>{source.filename}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default DraftReplyToolbar;
