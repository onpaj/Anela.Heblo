import React, { useState } from "react";
import { useSmartsuppConversations } from "../../../../api/hooks/useSmartsupp";
import ConversationList from "../ConversationList";
import ConversationDetail from "../ConversationDetail";

const SmartsuppChatsPage: React.FC = () => {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [status, setStatus] = useState<"Open" | "Resolved">("Open");

  const { data, isLoading } = useSmartsuppConversations(status);
  const conversations = data?.items ?? [];
  const selectedConversation = conversations.find((c) => c.id === selectedId) ?? null;

  return (
    <div className="flex h-full overflow-hidden bg-white rounded-lg shadow-sm border border-gray-200">
      <div className="w-96 flex-shrink-0 overflow-hidden">
        <ConversationList
          conversations={conversations}
          selectedId={selectedId}
          status={status}
          isLoading={isLoading}
          onSelect={setSelectedId}
          onStatusChange={(s) => { setStatus(s); setSelectedId(null); }}
        />
      </div>

      <div className="flex-1 overflow-hidden">
        {selectedConversation ? (
          <ConversationDetail
            conversationId={selectedId!}
            conversation={selectedConversation}
          />
        ) : (
          <div className="flex items-center justify-center h-full text-gray-400 text-sm">
            Vyberte konverzaci
          </div>
        )}
      </div>
    </div>
  );
};

export default SmartsuppChatsPage;
