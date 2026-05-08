import React, { useEffect, useRef } from "react";
import { ConversationDto, useSmartsuppConversation } from "../../../api/hooks/useSmartsupp";
import MessageBubble from "./MessageBubble";

interface ConversationDetailProps {
  conversationId: string;
  conversation: ConversationDto;
}

function formatLastActivity(dateStr?: string | null): string {
  if (!dateStr) return "";
  const diff = Date.now() - new Date(dateStr).getTime();
  const minutes = Math.floor(diff / 60_000);
  if (minutes < 1) return "právě teď";
  if (minutes < 60) return `před ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `před ${hours} h`;
  return `před ${Math.floor(hours / 24)} dny`;
}

const ConversationDetail: React.FC<ConversationDetailProps> = ({ conversationId, conversation }) => {
  const { data, isLoading } = useSmartsuppConversation(conversationId);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [data?.messages?.length, conversationId]);

  return (
    <div className="flex flex-col h-full">
      <div className="px-6 py-4 border-b border-gray-200 flex items-center gap-3">
        <div>
          <h3 className="font-semibold text-gray-900">{conversation.contactName ?? "Neznámý"}</h3>
          <p className="text-xs text-gray-500">
            Poslední aktivita{" "}
            {formatLastActivity(conversation.lastMessageAt ?? conversation.updatedAt)}
          </p>
        </div>
        {conversation.contactEmail && (
          <span className="ml-auto text-sm text-gray-400">{conversation.contactEmail}</span>
        )}
      </div>

      <div className="flex-1 overflow-y-auto px-6 py-4">
        {isLoading && (
          <div className="text-sm text-gray-400 text-center py-8">Načítání zpráv...</div>
        )}
        {!isLoading && (!data?.messages || data.messages.length === 0) && (
          <div className="text-sm text-gray-400 text-center py-8">Žádné zprávy</div>
        )}
        {data?.messages?.map((message) => (
          <MessageBubble key={message.id} message={message} />
        ))}
        <div ref={bottomRef} />
      </div>
    </div>
  );
};

export default ConversationDetail;
