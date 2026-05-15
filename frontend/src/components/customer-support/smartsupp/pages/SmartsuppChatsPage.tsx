import React, { useState } from "react";
import { useSmartsuppConversations, useTriggerSmartsuppSync } from "../../../../api/hooks/useSmartsupp";
import { useToast } from "../../../../contexts/ToastContext";
import ConversationList from "../ConversationList";
import ConversationDetail from "../ConversationDetail";
import ContactDetailsPanel from "../ContactDetailsPanel";

const CONTACT_PANEL_KEY = "smartsupp.contactPanel.open";

function readContactPanelOpen(): boolean {
  if (typeof window === "undefined") return true;
  const stored = window.localStorage.getItem(CONTACT_PANEL_KEY);
  if (stored === null) return true;
  return stored === "true";
}

const SmartsuppChatsPage: React.FC = () => {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [status, setStatus] = useState<"Open" | "Resolved">("Open");
  const [contactPanelOpen, setContactPanelOpen] = useState<boolean>(readContactPanelOpen());

  const { data, isLoading } = useSmartsuppConversations(status);
  const { showSuccess, showError } = useToast();
  const syncMutation = useTriggerSmartsuppSync();

  const conversations = data?.items ?? [];
  const selectedConversation = conversations.find((c) => c.id === selectedId) ?? null;

  const handleSyncClick = () => {
    syncMutation.mutate(undefined, {
      onSuccess: (result) => {
        showSuccess(
          "Synchronizace dokončena",
          `Konverzace: ${result.conversationsProcessed} • zprávy: ${result.messagesProcessed}`,
        );
      },
      onError: (error) => {
        showError("Synchronizace selhala", error instanceof Error ? error.message : "Neznámá chyba");
      },
    });
  };

  const toggleContactPanel = () => {
    setContactPanelOpen((open) => {
      const next = !open;
      window.localStorage.setItem(CONTACT_PANEL_KEY, String(next));
      return next;
    });
  };

  return (
    <div className="flex flex-col h-full overflow-hidden">
      <div className="flex items-center justify-end px-4 py-2 border-b border-gray-200 bg-white">
        <button
          type="button"
          onClick={handleSyncClick}
          disabled={syncMutation.isPending}
          className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium rounded-md border border-gray-300 bg-white hover:bg-gray-50 disabled:opacity-60 disabled:cursor-not-allowed"
        >
          {syncMutation.isPending ? "Synchronizuji…" : "Sync now"}
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden bg-white rounded-lg shadow-sm border border-gray-200">
        <div className="w-96 flex-shrink-0 overflow-hidden">
          <ConversationList
            conversations={conversations}
            selectedId={selectedId}
            status={status}
            isLoading={isLoading}
            onSelect={setSelectedId}
            onStatusChange={(s) => {
              setStatus(s);
              setSelectedId(null);
            }}
          />
        </div>

        <div className="flex-1 overflow-hidden min-w-0">
          {selectedConversation ? (
            <ConversationDetail
              conversationId={selectedId!}
              conversation={selectedConversation}
              onToggleContactPanel={toggleContactPanel}
            />
          ) : (
            <div className="flex items-center justify-center h-full text-gray-400 text-sm">
              Vyberte konverzaci
            </div>
          )}
        </div>

        {contactPanelOpen && selectedConversation && (
          <div className="hidden lg:block w-80 flex-shrink-0 overflow-hidden">
            <ContactDetailsPanel conversation={selectedConversation} />
          </div>
        )}
      </div>
    </div>
  );
};

export default SmartsuppChatsPage;
