import React from "react";
import { ConversationDto } from "../../../api/hooks/useSmartsupp";
import ConversationListItem from "./ConversationListItem";

interface ConversationListProps {
  conversations: ConversationDto[];
  selectedId: string | null;
  status: "Open" | "Resolved";
  isLoading: boolean;
  onSelect: (id: string) => void;
  onStatusChange: (status: "Open" | "Resolved") => void;
}

const ConversationList: React.FC<ConversationListProps> = ({
  conversations,
  selectedId,
  status,
  isLoading,
  onSelect,
  onStatusChange,
}) => (
  <div className="flex flex-col h-full border-r border-gray-200 dark:border-graphite-border">
    <div className="p-4 border-b border-gray-200 dark:border-graphite-border">
      <h2 className="font-semibold text-gray-800 dark:text-graphite-text mb-3">Všechny konverzace</h2>
      <div className="flex rounded-lg overflow-hidden border border-gray-200 dark:border-graphite-border text-sm">
        {(["Open", "Resolved"] as const).map((s) => (
          <button
            key={s}
            type="button"
            onClick={() => onStatusChange(s)}
            className={`flex-1 py-1.5 font-medium transition-colors ${
              status === s ? "bg-blue-500 text-white" : "bg-white dark:bg-graphite-surface text-gray-600 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5"
            }`}
          >
            {s === "Open" ? "Aktivní" : "Vyřešené"}
          </button>
        ))}
      </div>
    </div>

    <div className="flex-1 overflow-y-auto min-h-0">
      {isLoading && (
        <div className="p-4 text-sm text-gray-400 dark:text-graphite-faint text-center">Načítání...</div>
      )}
      {!isLoading && conversations.length === 0 && (
        <div className="p-4 text-sm text-gray-400 dark:text-graphite-faint text-center">Žádné konverzace</div>
      )}
      {[...conversations]
        .sort((a, b) => {
          const aTime = a.lastMessageAt ?? a.updatedAt;
          const bTime = b.lastMessageAt ?? b.updatedAt;
          return bTime < aTime ? -1 : bTime > aTime ? 1 : 0;
        })
        .map((c) => (
          <ConversationListItem
            key={c.id}
            conversation={c}
            isSelected={c.id === selectedId}
            onClick={() => onSelect(c.id)}
          />
        ))}
    </div>
  </div>
);

export default ConversationList;
