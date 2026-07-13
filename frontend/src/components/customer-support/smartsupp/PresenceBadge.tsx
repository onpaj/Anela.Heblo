import React from "react";
import { Eye } from "lucide-react";
import { ConversationPresenceDto } from "../../../api/hooks/useSmartsupp";

interface PresenceBadgeProps {
  viewer: ConversationPresenceDto;
}

/**
 * "Viewing now" pill for an operator currently active in a conversation. Amber accent + live dot
 * distinguish it from the neutral assigned-agent badges so a collision is visually obvious.
 */
const PresenceBadge: React.FC<PresenceBadgeProps> = ({ viewer }) => (
  <span
    data-testid="presence-badge"
    title={`${viewer.displayName} právě pracuje na konverzaci`}
    className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-amber-100 text-amber-800 dark:bg-amber-500/15 dark:text-amber-300"
  >
    <span className="relative flex h-2 w-2">
      <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-amber-500 opacity-75" />
      <span className="relative inline-flex h-2 w-2 rounded-full bg-amber-500" />
    </span>
    <Eye className="w-3 h-3" />
    <span className="truncate max-w-[10rem]">{viewer.displayName}</span>
  </span>
);

export default PresenceBadge;
