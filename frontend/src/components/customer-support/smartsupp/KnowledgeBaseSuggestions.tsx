import React, { useState } from "react";
import { ChevronDown, ChevronUp, Sparkles } from "lucide-react";
import { useKnowledgeBaseSuggestions } from "./hooks/useKnowledgeBaseSuggestions";

interface KnowledgeBaseSuggestionsProps {
  conversationId: string | null;
  lastContactMessage: string | null;
  onSelect: (content: string) => void;
}

const STORAGE_KEY = "smartsupp.kbSuggestions.collapsed";

function readCollapsed(): boolean {
  if (typeof window === "undefined") return false;
  return window.localStorage.getItem(STORAGE_KEY) === "true";
}

const KnowledgeBaseSuggestions: React.FC<KnowledgeBaseSuggestionsProps> = ({
  conversationId,
  lastContactMessage,
  onSelect,
}) => {
  const [collapsed, setCollapsed] = useState<boolean>(readCollapsed());
  const { suggestions, isLoading } = useKnowledgeBaseSuggestions(conversationId, lastContactMessage);

  if (!conversationId) return null;
  if (suggestions.length === 0 && !isLoading) return null;

  const toggle = () => {
    const next = !collapsed;
    setCollapsed(next);
    window.localStorage.setItem(STORAGE_KEY, String(next));
  };

  return (
    <div className="border-t border-gray-100 bg-gray-50">
      <button
        type="button"
        onClick={toggle}
        data-testid="kb-toggle"
        className="w-full flex items-center justify-between px-4 py-2 text-xs font-medium text-gray-600 hover:bg-gray-100"
      >
        <span className="inline-flex items-center gap-1.5">
          <Sparkles className="w-3.5 h-3.5 text-blue-500" />
          Návrhy odpovědí z databáze znalostí
        </span>
        {collapsed ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronUp className="w-3.5 h-3.5" />}
      </button>
      {!collapsed && (
        <div className="px-4 pb-3 flex flex-wrap gap-2">
          {suggestions.map((s) => (
            <button
              key={s.id}
              type="button"
              onClick={() => onSelect(s.content)}
              className="inline-flex items-center rounded-full px-3 py-1 text-xs bg-white border border-gray-200 text-gray-700 hover:bg-blue-50 hover:border-blue-300 transition-colors"
            >
              {s.title}
            </button>
          ))}
        </div>
      )}
    </div>
  );
};

export default KnowledgeBaseSuggestions;
