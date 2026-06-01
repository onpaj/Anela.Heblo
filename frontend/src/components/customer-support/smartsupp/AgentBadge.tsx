import React from "react";
import { getAgentColor } from "./utils/agentColor";

interface AgentBadgeProps {
  agentId?: string | null;
  name?: string | null;
  agentNames?: Record<string, string>;
  showInitials?: boolean;
}

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/);
  return parts.length >= 2
    ? `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase()
    : name.slice(0, 2).toUpperCase();
}

const AgentBadge: React.FC<AgentBadgeProps> = ({ agentId, name, agentNames, showInitials = true }) => {
  const color = getAgentColor(agentId);
  const resolvedName = name?.trim() || (agentId ? agentNames?.[agentId] : null) || null;
  let initials: string;
  let label: string;

  if (resolvedName) {
    initials = getInitials(resolvedName);
    label = resolvedName;
  } else {
    initials = agentId ? agentId.slice(0, 2).toUpperCase() : "?";
    label = "Agent";
  }

  return (
    <span
      data-testid="agent-badge"
      className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ${color.bg} ${color.text}`}
    >
      {showInitials && (
        <span className={`inline-flex items-center justify-center w-4 h-4 rounded-full ring-1 ${color.ring} bg-white text-[10px] font-semibold`}>
          {initials}
        </span>
      )}
      <span className="truncate max-w-[10rem]">{label}</span>
    </span>
  );
};

export default AgentBadge;
