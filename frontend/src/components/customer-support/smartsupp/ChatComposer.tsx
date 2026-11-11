import { useEffect, useRef, useState } from "react";
import { Loader2, Maximize2, Minimize2, Paperclip, Send } from "lucide-react";
import DraftReplyTriggerBar from "./DraftReplyTriggerBar";
import DraftReplyToolbar from "./DraftReplyToolbar";
import DraftReplyFeedback from "./DraftReplyFeedback";
import { useGenerateDraftReply, type DraftReplySource } from "./hooks/useGenerateDraftReply";
import { useSendMessage } from "./hooks/useSendMessage";
import EmojiPicker from "./EmojiPicker";
import PendingAttachments from "./PendingAttachments";
import { useComposerAttachments } from "./hooks/useComposerAttachments";
import { ATTACHMENT_ACCEPT } from "./utils/attachmentPolicy";
import { insertAtCursor } from "./utils/insertAtCursor";

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
  const [draftLogId, setDraftLogId] = useState<string | null>(null);
  const [sources, setSources] = useState<DraftReplySource[]>([]);
  const [lastTopic, setLastTopic] = useState<string | undefined>(undefined);
  const [pendingTopic, setPendingTopic] = useState<{ topic: string | undefined } | null>(null);
  const [isExpanded, setIsExpanded] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const { generate, isLoading, error, result, reset } = useGenerateDraftReply(conversationId);
  const { send, isPending: isSending, error: sendError, justSent, clearSent } = useSendMessage(conversationId);
  const attachments = useComposerAttachments(conversationId);
  const { clear: clearAttachments } = attachments;

  useEffect(() => {
    if (result) {
      const answer = result.answer.slice(0, MAX_CHARS);
      setDraft(answer);
      setSources(result.sources);
      setDraftLogId(result.id);
      setIsAiDraft(true);
      onDraftChange?.(answer);
      reset();
    }
  }, [result, reset, onDraftChange]);

  useEffect(() => {
    if (justSent) {
      setDraft("");
      setSources([]);
      setDraftLogId(null);
      setIsAiDraft(false);
      setLastTopic(undefined);
      setPendingTopic(null);
      onDraftChange?.("");
      clearAttachments();
      clearSent();
    }
  }, [justSent, clearSent, clearAttachments, onDraftChange]);

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

  const handleEmojiPick = (emoji: string) => {
    const textarea = textareaRef.current;
    const start = textarea?.selectionStart ?? draft.length;
    const end = textarea?.selectionEnd ?? draft.length;
    const { value, caret } = insertAtCursor(draft, start, end, emoji);
    handleDraftChange(value);
    requestAnimationFrame(() => {
      textareaRef.current?.focus();
      textareaRef.current?.setSelectionRange(caret, caret);
    });
  };

  const handleDiscard = () => {
    setDraft("");
    setSources([]);
    setDraftLogId(null);
    setIsAiDraft(false);
    setLastTopic(undefined);
    setPendingTopic(null);
    onDraftChange?.("");
  };

  const hasFiles = attachments.files.length > 0;
  const canSend = (draft.trim() !== "" || hasFiles) && !isSending;

  const handleSend = () => {
    if (!canSend) return;
    if (hasFiles) {
      send(draft, draftLogId, attachments.files);
      return;
    }
    send(draft, draftLogId);
  };

  const handleFilePick = (e: React.ChangeEvent<HTMLInputElement>) => {
    attachments.add(Array.from(e.target.files ?? []));
    e.target.value = "";
  };

  const handlePaste = (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const clipboard = e.clipboardData;
    // Office apps put text and a rendered image on the clipboard together; keep the text paste.
    if (Array.from(clipboard?.types ?? []).includes("text/plain") || clipboard?.getData?.("text/plain")) return;
    const pasted = Array.from(clipboard?.files ?? []);
    if (pasted.length === 0) return;
    e.preventDefault();
    attachments.add(pasted);
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    const dropped = Array.from(e.dataTransfer?.files ?? []);
    if (dropped.length === 0) return;
    e.preventDefault();
    if (isBusy) return;
    attachments.add(dropped);
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
        <div className="flex items-center justify-between border-t border-amber-200 dark:border-amber-900/40 bg-amber-50 dark:bg-amber-900/30 px-4 py-2 text-xs">
          <span className="text-amber-800 dark:text-amber-300">Přepsat rozepsanou odpověď?</span>
          <div className="flex gap-3">
            <button
              type="button"
              onClick={confirmOverwrite}
              className="font-medium text-amber-800 dark:text-amber-300 hover:text-amber-900"
            >
              Přepsat
            </button>
            <button
              type="button"
              onClick={cancelOverwrite}
              className="text-gray-500 dark:text-graphite-muted hover:text-gray-700"
            >
              Zrušit
            </button>
          </div>
        </div>
      )}
      <div
        data-testid="composer-box"
        onDragOver={(e) => e.preventDefault()}
        onDrop={handleDrop}
        className="flex flex-col gap-2 border-t border-gray-200 dark:border-graphite-border bg-white dark:bg-graphite-surface p-3"
      >
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
            ref={textareaRef}
            value={draft}
            disabled={isBusy}
            onChange={(e) => handleDraftChange(e.target.value)}
            onKeyDown={handleKeyDown}
            onPaste={handlePaste}
            placeholder={isLoading ? "Generuji návrh odpovědi…" : "Napište odpověď..."}
            rows={isExpanded ? 14 : 5}
            className="w-full resize-none rounded-md border border-gray-200 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint py-2 pl-3 pr-9 text-sm focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:bg-gray-50 dark:disabled:bg-graphite-surface-2"
          />
          <button
            type="button"
            onClick={() => setIsExpanded((v) => !v)}
            aria-label={isExpanded ? "Zmenšit" : "Zvětšit"}
            title={isExpanded ? "Zmenšit" : "Zvětšit"}
            className="absolute right-2 top-2 rounded p-1 text-gray-400 dark:text-graphite-faint hover:bg-gray-100 dark:hover:bg-white/5 hover:text-gray-600"
          >
            {isExpanded ? (
              <Minimize2 className="h-4 w-4" />
            ) : (
              <Maximize2 className="h-4 w-4" />
            )}
          </button>
        </div>
        <PendingAttachments files={attachments.files} onRemove={attachments.remove} disabled={isBusy} />
        {attachments.error && (
          <p className="text-xs text-red-500 dark:text-red-400">{attachments.error}</p>
        )}
        {sendError && (
          <p className="text-xs text-red-500 dark:text-red-400">{sendError}</p>
        )}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <EmojiPicker onPick={handleEmojiPick} disabled={isBusy} />
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              disabled={isBusy}
              aria-label="Přiložit soubor"
              title="Přiložit soubor"
              className="rounded p-1 text-gray-400 dark:text-graphite-faint hover:bg-gray-100 dark:hover:bg-white/5 hover:text-gray-600 disabled:opacity-50"
            >
              <Paperclip className="h-4 w-4" />
            </button>
            <input
              ref={fileInputRef}
              type="file"
              multiple
              accept={ATTACHMENT_ACCEPT}
              onChange={handleFilePick}
              className="hidden"
              data-testid="composer-file-input"
            />
            <span className="text-xs text-gray-400 dark:text-graphite-faint">
              {draft.length} / {MAX_CHARS}
            </span>
          </div>
          <button
            type="button"
            onClick={handleSend}
            disabled={!canSend || isBusy}
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
        {draftLogId && <DraftReplyFeedback key={draftLogId} logId={draftLogId} />}
      </div>
    </div>
  );
}

export default ChatComposer;
