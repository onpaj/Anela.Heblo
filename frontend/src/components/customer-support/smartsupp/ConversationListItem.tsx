import React from "react";
import { ConversationDto } from "../../../api/hooks/useSmartsupp";
import StatusPill from "./StatusPill";

interface ConversationListItemProps {
  conversation: ConversationDto;
  isSelected: boolean;
  onClick: () => void;
}

function getInitials(name?: string | null): string {
  if (!name) return "?";
  const parts = name.trim().split(/\s+/);
  return parts.length >= 2
    ? `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase()
    : name.slice(0, 2).toUpperCase();
}

function formatRelativeTime(dateStr?: string | null): string {
  if (!dateStr) return "";
  const diff = Date.now() - new Date(dateStr).getTime();
  const minutes = Math.floor(diff / 60_000);
  if (minutes < 1) return "právě teď";
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h`;
  return `${Math.floor(hours / 24)} d`;
}

const ConversationListItem: React.FC<ConversationListItemProps> = ({
  conversation,
  isSelected,
  onClick,
}) => {
  const displayName = conversation.contactName ?? conversation.contactEmail ?? null;
  const initials = getInitials(displayName);
  const relativeTime = formatRelativeTime(conversation.lastMessageAt ?? conversation.updatedAt);
  const isUnread = conversation.isUnread;

  const containerClasses = [
    "w-full text-left px-4 py-3 flex items-start gap-3 border-b border-gray-100 transition-colors",
    isSelected ? "bg-blue-50" : "hover:bg-gray-50",
    isUnread ? "border-l-4 border-l-blue-500" : "",
  ].join(" ");

  return (
    <button type="button" onClick={onClick} className={containerClasses}>
      <div className="flex-shrink-0 w-9 h-9 rounded-full bg-blue-500 text-white flex items-center justify-center text-sm font-medium">
        {conversation.contactAvatarUrl ? (
          <img
            src={conversation.contactAvatarUrl}
            alt={displayName ?? ""}
            className="w-9 h-9 rounded-full object-cover"
          />
        ) : (
          initials
        )}
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center justify-between gap-2">
          <span className={`text-sm text-gray-900 truncate ${isUnread ? "font-semibold" : "font-medium"}`}>
            {displayName ?? "Neznámý"}
          </span>
          <span className="text-xs text-gray-400 flex-shrink-0">{relativeTime}</span>
        </div>
        <div className="flex items-center gap-2 mt-1">
          <StatusPill status={conversation.status} />
          <span className="text-xs text-gray-500 truncate flex-1">
            {conversation.lastMessagePreview ?? ""}
          </span>
        </div>
      </div>
    </button>
  );
};

export default ConversationListItem;
