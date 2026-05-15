import React, { useEffect, useRef } from "react";
import { ConversationDto, MessageDto, useSmartsuppConversation } from "../../../api/hooks/useSmartsupp";
import MessageBubble from "./MessageBubble";
import StatusPill from "./StatusPill";
import AgentBadge from "./AgentBadge";
import DaySeparator from "./DaySeparator";
import ChatComposer from "./ChatComposer";
import { PanelRight } from "lucide-react";

interface ConversationDetailProps {
  conversationId: string;
  conversation: ConversationDto;
  onToggleContactPanel: () => void;
}

function lastContactMessage(messages: MessageDto[]): string | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    const t = m.authorType.toLowerCase();
    if (t === "visitor" || t === "contact") return m.content ?? null;
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
  onToggleContactPanel,
}) => {
  const { data, isLoading } = useSmartsuppConversation(conversationId);
  const bottomRef = useRef<HTMLDivElement>(null);
  const messages = data?.messages ?? [];

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages.length, conversationId]);

  const displayName = conversation.contactName ?? conversation.contactEmail ?? "Neznámý";
  const grouped = groupByDay(messages);

  return (
    <div className="flex flex-col h-full">
      <div className="px-6 py-3 border-b border-gray-200 flex items-center gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-gray-900 truncate">{displayName}</h3>
            <StatusPill status={conversation.status} />
          </div>
          {conversation.contactEmail && (
            <p className="text-xs text-gray-500 truncate">{conversation.contactEmail}</p>
          )}
        </div>
        <div className="ml-auto flex items-center gap-2">
          {conversation.assignedAgentIds.map((id) => (
            <AgentBadge key={id} agentId={id} name={id} />
          ))}
          <button
            type="button"
            onClick={onToggleContactPanel}
            data-testid="toggle-contact-panel"
            aria-label="Detail kontaktu"
            className="inline-flex items-center justify-center w-8 h-8 rounded-md text-gray-500 hover:bg-gray-100"
          >
            <PanelRight className="w-4 h-4" />
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-6 py-4">
        {isLoading && (
          <div className="text-sm text-gray-400 text-center py-8">Načítání zpráv...</div>
        )}
        {!isLoading && messages.length === 0 && (
          <div className="text-sm text-gray-400 text-center py-8">Žádné zprávy</div>
        )}
        {grouped.map((g) => (
          <div key={g.day}>
            <DaySeparator date={g.items[0].createdAt} />
            {g.items.map((m) => (
              <MessageBubble key={m.id} message={m} />
            ))}
          </div>
        ))}
        <div ref={bottomRef} />
      </div>

      <ChatComposer
        conversationId={conversationId}
        lastContactMessage={lastContactMessage(messages)}
      />
    </div>
  );
};

export default ConversationDetail;
