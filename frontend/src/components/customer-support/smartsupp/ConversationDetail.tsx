import React, { useEffect, useRef } from "react";
import { ArrowLeft, Info, Loader2 } from "lucide-react";
import { ConversationDto, MessageDto, useSmartsuppConversation, useCloseConversation } from "../../../api/hooks/useSmartsupp";
import { toast } from "react-hot-toast";
import MessageBubble from "./MessageBubble";
import StatusPill from "./StatusPill";
import AgentBadge from "./AgentBadge";
import DaySeparator from "./DaySeparator";
import ChatComposer from "./ChatComposer";

interface ConversationDetailProps {
  conversationId: string;
  conversation: ConversationDto;
  onBack?: () => void;
  onOpenContactDetails?: () => void;
  initialDraft?: string;
  onDraftChange?: (draft: string) => void;
}

// Returns the most recent customer message that actually carries text.
// SmartSupp emits page-visit events as authorType "Visitor"/subType "system"
// with empty content — those are skipped so they don't mask the real message.
export function lastContactMessage(messages: MessageDto[]): string | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    const authorType = m.authorType.toLowerCase();
    const isContact = authorType === "visitor" || authorType === "contact";
    const isSystem =
      authorType === "system" || (m.subType ?? "").toLowerCase() === "system";
    if (isContact && !isSystem) {
      const content = m.content?.trim();
      if (content) return content;
    }
  }
  return null;
}

function groupByDay(messages: MessageDto[]): Array<{ day: string; items: MessageDto[] }> {
  const groups: Array<{ day: string; items: MessageDto[] }> = [];
  for (const m of messages) {
    const day = new Date(m.createdAt).toISOString().slice(0, 10);
    const last = groups[groups.length - 1];
    if (last && last.day === day) {
      last.items.push(m);
    } else {
      groups.push({ day, items: [m] });
    }
  }
  return groups;
}

const ConversationDetail: React.FC<ConversationDetailProps> = ({
  conversationId,
  conversation,
  onBack,
  onOpenContactDetails,
  initialDraft,
  onDraftChange,
}) => {
  const { data, isLoading } = useSmartsuppConversation(conversationId);
  const { mutate: closeConversation, isPending: isClosing } = useCloseConversation();
  const liveStatus = data?.conversation?.status ?? conversation.status;

  const handleClose = () => {
    closeConversation(conversationId, {
      onSuccess: () => toast.success("Konverzace byla uzavřena"),
      onError: (err) => toast.error(err.message),
    });
  };

  const bottomRef = useRef<HTMLDivElement>(null);
  const messages = data?.messages ?? [];
  const agentNames = data?.agentNames ?? {};

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages.length, conversationId]);

  const displayName = conversation.contactName ?? conversation.contactEmail ?? "Neznámý";
  const grouped = groupByDay(messages);

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="px-4 py-3 border-b border-gray-200 dark:border-graphite-border flex items-center gap-3">
        {onBack && (
          <button
            type="button"
            data-testid="back-to-list-btn"
            onClick={onBack}
            aria-label="Zpět"
            className="md:hidden flex items-center justify-center min-h-[40px] min-w-[40px] p-1 -ml-1 text-gray-600 dark:text-graphite-muted flex-shrink-0"
          >
            <ArrowLeft className="w-5 h-5" />
          </button>
        )}
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-gray-900 dark:text-graphite-text truncate">{displayName}</h3>
            <StatusPill status={liveStatus} />
          </div>
          {conversation.contactEmail && (
            <p className="text-xs text-gray-500 dark:text-graphite-muted truncate">{conversation.contactEmail}</p>
          )}
        </div>
        <div className="ml-auto flex items-center gap-2">
          {conversation.assignedAgentIds.map((id) => (
            <AgentBadge key={id} agentId={id} name={agentNames[id] ?? id} />
          ))}
          {liveStatus.toLowerCase() === 'open' && (
            <button
              type="button"
              data-testid="close-conversation-btn"
              onClick={handleClose}
              disabled={isClosing}
              aria-label="Uzavřít konverzaci"
              className="inline-flex items-center gap-1.5 rounded-md border border-gray-300 dark:border-graphite-border px-2.5 py-1.5 text-sm font-medium text-gray-700 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {isClosing && <Loader2 className="h-4 w-4 animate-spin" />}
              Uzavřít konverzaci
            </button>
          )}
          {onOpenContactDetails && (
            <button
              type="button"
              data-testid="open-contact-details-btn"
              onClick={onOpenContactDetails}
              aria-label="Detail kontaktu"
              className="md:hidden flex items-center justify-center min-h-[40px] min-w-[40px] p-1 text-gray-600 dark:text-graphite-muted"
            >
              <Info className="w-5 h-5" />
            </button>
          )}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto min-h-0 px-6 py-4">
        {isLoading && (
          <div className="text-sm text-gray-400 dark:text-graphite-faint text-center py-8">Načítání zpráv...</div>
        )}
        {!isLoading && messages.length === 0 && (
          <div className="text-sm text-gray-400 dark:text-graphite-faint text-center py-8">Žádné zprávy</div>
        )}
        {grouped.map((g) => (
          <div key={g.day}>
            <DaySeparator date={g.items[0].createdAt} />
            {g.items.map((m) => (
              <MessageBubble key={m.id} message={m} agentNames={agentNames} />
            ))}
          </div>
        ))}
        <div ref={bottomRef} />
      </div>

      <ChatComposer
        key={conversationId}
        conversationId={conversationId}
        lastContactMessage={lastContactMessage(messages)}
        initialDraft={initialDraft}
        onDraftChange={onDraftChange}
      />
    </div>
  );
};

export default ConversationDetail;
