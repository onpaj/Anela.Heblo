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
  <div className="flex flex-col h-full border-r border-gray-200">
    <div className="p-4 border-b border-gray-200">
      <h2 className="font-semibold text-gray-800 mb-3">Všechny konverzace</h2>
      <div className="flex rounded-lg overflow-hidden border border-gray-200 text-sm">
        {(["Open", "Resolved"] as const).map((s) => (
          <button
            key={s}
            type="button"
            onClick={() => onStatusChange(s)}
            className={`flex-1 py-1.5 font-medium transition-colors ${
              status === s ? "bg-blue-500 text-white" : "bg-white text-gray-600 hover:bg-gray-50"
            }`}
          >
            {s === "Open" ? "Aktivní" : "Vyřešené"}
          </button>
        ))}
      </div>
    </div>

    <div className="flex-1 overflow-y-auto">
      {isLoading && (
        <div className="p-4 text-sm text-gray-400 text-center">Načítání...</div>
      )}
      {!isLoading && conversations.length === 0 && (
        <div className="p-4 text-sm text-gray-400 text-center">Žádné konverzace</div>
      )}
      {[...conversations]
        .sort((a, b) => {
          const aTime = a.lastMessageAt ?? a.createdAt;
          const bTime = b.lastMessageAt ?? b.createdAt;
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
