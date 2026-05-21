import React, { useState } from "react";
import { ArrowLeft } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { useSmartsuppConversations } from "../../../../api/hooks/useSmartsupp";
import { QUERY_KEYS } from "../../../../api/client";
import ConversationList from "../ConversationList";
import ConversationDetail from "../ConversationDetail";
import ContactDetailsPanel from "../ContactDetailsPanel";
import CollapsibleRail from "../CollapsibleRail";

const LIST_PANEL_KEY = "smartsupp.listPanel.open";
const CONTACT_PANEL_KEY = "smartsupp.contactPanel.open";

function readPanelOpen(key: string, defaultOpen: boolean): boolean {
  if (typeof window === "undefined") return defaultOpen;
  const stored = window.localStorage.getItem(key);
  if (stored === null) return defaultOpen;
  return stored === "true";
}

type MobileView = "list" | "chat" | "contact";

const SmartsuppChatsPage: React.FC = () => {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [status, setStatus] = useState<"Open" | "Resolved">("Open");
  const [mobileView, setMobileView] = useState<MobileView>("list");
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [listPanelOpen, setListPanelOpen] = useState<boolean>(() =>
    readPanelOpen(LIST_PANEL_KEY, true),
  );
  const [contactPanelOpen, setContactPanelOpen] = useState<boolean>(() =>
    readPanelOpen(CONTACT_PANEL_KEY, false),
  );

  const { data, isLoading, isFetching } = useSmartsuppConversations(status);
  const queryClient = useQueryClient();

  const conversations = data?.items ?? [];
  const selectedConversation = conversations.find((c) => c.id === selectedId) ?? null;

  const handleDraftChange = (id: string, text: string) =>
    setDrafts((prev) => ({ ...prev, [id]: text }));

  const togglePanel = (
    key: string,
    setter: React.Dispatch<React.SetStateAction<boolean>>,
  ) => {
    setter((open) => {
      const next = !open;
      window.localStorage.setItem(key, String(next));
      return next;
    });
  };

  return (
    <div className="flex flex-col flex-1 min-h-0 overflow-hidden">
      <div
        className={`${mobileView !== "list" ? "hidden md:flex" : "flex"} items-center justify-end px-4 py-2 border-b border-gray-200 bg-white`}
      >
        <button
          type="button"
          onClick={() => queryClient.invalidateQueries({ queryKey: QUERY_KEYS.smartsupp })}
          disabled={isFetching}
          className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium rounded-md border border-gray-300 bg-white hover:bg-gray-50 disabled:opacity-60 disabled:cursor-not-allowed"
        >
          {isFetching ? "Načítám…" : "Obnovit"}
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden bg-white rounded-lg shadow-sm border border-gray-200">
        <div
          className={`${mobileView === "list" ? "flex" : "hidden"} ${listPanelOpen ? "md:flex" : "md:hidden"} flex-col w-full md:w-96 flex-shrink-0 overflow-hidden`}
        >
          <ConversationList
            conversations={conversations}
            selectedId={selectedId}
            status={status}
            isLoading={isLoading}
            onSelect={(id) => {
              setSelectedId(id);
              setMobileView("chat");
            }}
            onStatusChange={(s) => {
              setStatus(s);
              setSelectedId(null);
              setMobileView("list");
            }}
          />
        </div>

        <div className="hidden md:flex">
          <CollapsibleRail
            side="left"
            isOpen={listPanelOpen}
            label="Seznam konverzací"
            onToggle={() => togglePanel(LIST_PANEL_KEY, setListPanelOpen)}
          />
        </div>

        <div
          className={`${mobileView === "chat" ? "flex" : "hidden"} md:flex flex-col flex-1 overflow-hidden min-w-0`}
        >
          {selectedConversation ? (
            <ConversationDetail
              conversationId={selectedId!}
              conversation={selectedConversation}
              onBack={() => setMobileView("list")}
              onOpenContactDetails={() => setMobileView("contact")}
              initialDraft={drafts[selectedId!] ?? ""}
              onDraftChange={(text) => handleDraftChange(selectedId!, text)}
            />
          ) : (
            <div className="flex items-center justify-center h-full text-gray-400 text-sm">
              Vyberte konverzaci
            </div>
          )}
        </div>

        {selectedConversation && (
          <div className="hidden md:flex">
            <CollapsibleRail
              side="right"
              isOpen={contactPanelOpen}
              label="Detail kontaktu"
              onToggle={() => togglePanel(CONTACT_PANEL_KEY, setContactPanelOpen)}
            />
            {contactPanelOpen && (
              <div className="w-80 flex-shrink-0 overflow-hidden">
                <ContactDetailsPanel conversation={selectedConversation} />
              </div>
            )}
          </div>
        )}

        {mobileView === "contact" && selectedConversation && (
          <div
            data-testid="mobile-contact-subpage"
            className="flex flex-col w-full md:hidden overflow-hidden"
          >
            <div className="flex items-center gap-2 px-4 py-3 border-b border-gray-200 bg-white">
              <button
                type="button"
                data-testid="back-to-chat-btn"
                onClick={() => setMobileView("chat")}
                aria-label="Zpět na konverzaci"
                className="flex items-center justify-center min-h-[40px] min-w-[40px] p-1 -ml-1 text-gray-600 flex-shrink-0"
              >
                <ArrowLeft className="w-5 h-5" />
              </button>
              <span className="font-semibold text-gray-900">Detail kontaktu</span>
            </div>
            <div className="flex-1 overflow-y-auto">
              <ContactDetailsPanel conversation={selectedConversation} />
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default SmartsuppChatsPage;
