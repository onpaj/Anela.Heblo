import { useEffect, useState } from "react";
import { Loader2, Maximize2, Minimize2, Send } from "lucide-react";
import DraftReplyTriggerBar from "./DraftReplyTriggerBar";
import DraftReplyToolbar from "./DraftReplyToolbar";
import { useGenerateDraftReply, type DraftReplySource } from "./hooks/useGenerateDraftReply";
import { useSendMessage } from "./hooks/useSendMessage";

interface ChatComposerProps {
  conversationId: string | null;
  lastContactMessage: string | null;
  initialDraft?: string;
  onDraftChange?: (draft: string) => void;
}

const MAX_CHARS = 4000;

function ChatComposer({ conversationId, lastContactMessage, initialDraft, onDraftChange }: ChatComposerProps) {
  const [draft, setDraft] = useState(initialDraft ?? "");
  const [isAiDraft, setIsAiDraft] = useState(false);
  const [sources, setSources] = useState<DraftReplySource[]>([]);
  const [lastTopic, setLastTopic] = useState<string | undefined>(undefined);
  const [pendingTopic, setPendingTopic] = useState<{ topic: string | undefined } | null>(null);
  const [isExpanded, setIsExpanded] = useState(false);

  const { generate, isLoading, error, result, reset } = useGenerateDraftReply(conversationId);
  const { send, isPending: isSending, error: sendError, justSent, clearSent } = useSendMessage(conversationId);

  useEffect(() => {
    if (result) {
      const answer = result.answer.slice(0, MAX_CHARS);
      setDraft(answer);
      setSources(result.sources);
      setIsAiDraft(true);
      onDraftChange?.(answer);
      reset();
    }
  }, [result, reset, onDraftChange]);

  useEffect(() => {
    if (justSent) {
      setDraft("");
      setSources([]);
      setIsAiDraft(false);
      setLastTopic(undefined);
      setPendingTopic(null);
      onDraftChange?.("");
      clearSent();
    }
  }, [justSent, clearSent, onDraftChange]);

  const canGenerateWithoutTopic =
    lastContactMessage !== null && lastContactMessage.trim() !== "";

  const requestGeneration = (topic?: string) => {
    if (draft.trim() !== "" && !isAiDraft) {
      setPendingTopic({ topic });
      return;
    }
    setLastTopic(topic);
    generate(topic);
  };

  const confirmOverwrite = () => {
    if (pendingTopic === null) return;
    setLastTopic(pendingTopic.topic);
    generate(pendingTopic.topic);
    setPendingTopic(null);
  };

  const cancelOverwrite = () => setPendingTopic(null);

  const handleDraftChange = (value: string) => {
    const trimmed = value.slice(0, MAX_CHARS);
    setDraft(trimmed);
    onDraftChange?.(trimmed);
    if (isAiDraft) {
      setIsAiDraft(false);
    }
  };

  const handleDiscard = () => {
    setDraft("");
    setSources([]);
    setIsAiDraft(false);
    setLastTopic(undefined);
    setPendingTopic(null);
    onDraftChange?.("");
  };

  const handleSend = () => {
    if (!draft.trim() || isSending) return;
    send(draft);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) {
      e.preventDefault();
      handleSend();
    }
  };

  const isBusy = isLoading || isSending;

  return (
    <div className="flex flex-col">
      <DraftReplyTriggerBar
        disabled={isBusy}
        canGenerateWithoutTopic={canGenerateWithoutTopic}
        error={error}
        onGenerate={requestGeneration}
      />
      {pendingTopic !== null && (
        <div className="flex items-center justify-between border-t border-amber-200 bg-amber-50 px-4 py-2 text-xs">
          <span className="text-amber-800">Přepsat rozepsanou odpověď?</span>
          <div className="flex gap-3">
            <button
              type="button"
              onClick={confirmOverwrite}
              className="font-medium text-amber-800 hover:text-amber-900"
            >
              Přepsat
            </button>
            <button
              type="button"
              onClick={cancelOverwrite}
              className="text-gray-500 hover:text-gray-700"
            >
              Zrušit
            </button>
          </div>
        </div>
      )}
      <div className="flex flex-col gap-2 border-t border-gray-200 bg-white p-3">
        {isAiDraft && (
          <DraftReplyToolbar
            sources={sources}
            disabled={isBusy}
            onRegenerate={() => generate(lastTopic)}
            onDiscard={handleDiscard}
          />
        )}
        <div className="relative">
          <textarea
            value={draft}
            disabled={isBusy}
            onChange={(e) => handleDraftChange(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={isLoading ? "Generuji návrh odpovědi…" : "Napište odpověď..."}
            rows={isExpanded ? 14 : 5}
            className="w-full resize-none rounded-md border border-gray-200 py-2 pl-3 pr-9 text-sm focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:bg-gray-50"
          />
          <button
            type="button"
            onClick={() => setIsExpanded((v) => !v)}
            aria-label={isExpanded ? "Zmenšit" : "Zvětšit"}
            title={isExpanded ? "Zmenšit" : "Zvětšit"}
            className="absolute right-2 top-2 rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
          >
            {isExpanded ? (
              <Minimize2 className="h-4 w-4" />
            ) : (
              <Maximize2 className="h-4 w-4" />
            )}
          </button>
        </div>
        {sendError && (
          <p className="text-xs text-red-500">{sendError}</p>
        )}
        <div className="flex items-center justify-between">
          <span className="text-xs text-gray-400">
            {draft.length} / {MAX_CHARS}
          </span>
          <button
            type="button"
            onClick={handleSend}
            disabled={!draft.trim() || isBusy}
            aria-label="Odeslat"
            className="inline-flex items-center gap-2 rounded-md bg-blue-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-600 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isSending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Send className="h-4 w-4" />
            )}
            {isSending ? "Odesílám…" : "Odeslat"}
          </button>
        </div>
      </div>
    </div>
  );
}

export default ChatComposer;
