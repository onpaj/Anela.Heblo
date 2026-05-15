import { useEffect, useState } from "react";
import { Send } from "lucide-react";
import DraftReplyTriggerBar from "./DraftReplyTriggerBar";
import DraftReplyToolbar from "./DraftReplyToolbar";
import { useGenerateDraftReply, type DraftReplySource } from "./hooks/useGenerateDraftReply";

interface ChatComposerProps {
  conversationId: string | null;
  lastContactMessage: string | null;
}

const MAX_CHARS = 4000;

function ChatComposer({ conversationId, lastContactMessage }: ChatComposerProps) {
  const [draft, setDraft] = useState("");
  const [isAiDraft, setIsAiDraft] = useState(false);
  const [sources, setSources] = useState<DraftReplySource[]>([]);
  const [lastTopic, setLastTopic] = useState<string | undefined>(undefined);

  const { generate, isLoading, error, result, reset } = useGenerateDraftReply(conversationId);

  // Move a freshly generated answer into the composer as an editable AI draft.
  useEffect(() => {
    if (result) {
      setDraft(result.answer.slice(0, MAX_CHARS));
      setSources(result.sources);
      setIsAiDraft(true);
      reset();
    }
  }, [result, reset]);

  const canGenerateWithoutTopic =
    lastContactMessage !== null && lastContactMessage.trim() !== "";

  const requestGeneration = (topic?: string) => {
    if (draft.trim() !== "" && !isAiDraft) {
      const confirmed = window.confirm(
        "Přepsat rozepsanou odpověď vygenerovaným návrhem?",
      );
      if (!confirmed) {
        return;
      }
    }
    setLastTopic(topic);
    generate(topic);
  };

  const handleDraftChange = (value: string) => {
    setDraft(value.slice(0, MAX_CHARS));
    if (isAiDraft) {
      setIsAiDraft(false);
    }
  };

  const handleDiscard = () => {
    setDraft("");
    setSources([]);
    setIsAiDraft(false);
    setLastTopic(undefined);
  };

  return (
    <div className="flex flex-col">
      <DraftReplyTriggerBar
        disabled={isLoading}
        canGenerateWithoutTopic={canGenerateWithoutTopic}
        error={error}
        onGenerate={requestGeneration}
      />
      <div className="flex flex-col gap-2 border-t border-gray-200 bg-white p-3">
        {isAiDraft && (
          <DraftReplyToolbar
            sources={sources}
            onRegenerate={() => generate(lastTopic)}
            onDiscard={handleDiscard}
          />
        )}
        <textarea
          value={draft}
          disabled={isLoading}
          onChange={(e) => handleDraftChange(e.target.value)}
          placeholder={isLoading ? "Generuji návrh odpovědi…" : "Napište odpověď..."}
          rows={3}
          className="w-full resize-none rounded-md border border-gray-200 px-3 py-2 text-sm focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:bg-gray-50"
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
            className="inline-flex cursor-not-allowed items-center gap-2 rounded-md bg-blue-500 px-3 py-1.5 text-sm font-medium text-white opacity-50"
          >
            <Send className="h-4 w-4" />
            Odeslat
          </button>
        </div>
      </div>
    </div>
  );
}

export default ChatComposer;
