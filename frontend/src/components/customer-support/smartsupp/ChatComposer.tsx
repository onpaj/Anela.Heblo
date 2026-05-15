import React, { useState } from "react";
import { Send } from "lucide-react";
import KnowledgeBaseSuggestions from "./KnowledgeBaseSuggestions";

interface ChatComposerProps {
  conversationId: string | null;
  lastContactMessage: string | null;
}

const MAX_CHARS = 4000;

const ChatComposer: React.FC<ChatComposerProps> = ({ conversationId, lastContactMessage }) => {
  const [draft, setDraft] = useState<string>("");

  const insertSuggestion = (content: string) => {
    setDraft((prev) => (prev.length === 0 ? content : `${prev}\n\n${content}`));
  };

  return (
    <div className="flex flex-col">
      <KnowledgeBaseSuggestions
        conversationId={conversationId}
        lastContactMessage={lastContactMessage}
        onSelect={insertSuggestion}
      />
      <div className="border-t border-gray-200 p-3 flex flex-col gap-2 bg-white">
        <textarea
          value={draft}
          onChange={(e) => setDraft(e.target.value.slice(0, MAX_CHARS))}
          placeholder="Napište odpověď..."
          rows={3}
          className="w-full resize-none rounded-md border border-gray-200 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-200 focus:border-blue-400"
        />
        <div className="flex items-center justify-between">
          <span className="text-xs text-gray-400">
            {draft.length} / {MAX_CHARS}
          </span>
          <button
            type="button"
            disabled
            title="Odpovídání bude přidáno později"
            aria-label="Odeslat"
            className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium rounded-md bg-blue-500 text-white opacity-50 cursor-not-allowed"
          >
            <Send className="w-4 h-4" />
            Odeslat
          </button>
        </div>
      </div>
    </div>
  );
};

export default ChatComposer;
