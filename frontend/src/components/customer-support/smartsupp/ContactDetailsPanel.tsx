import React from "react";
import { ConversationDto } from "../../../api/hooks/useSmartsupp";
import StatusPill from "./StatusPill";
import AgentBadge from "./AgentBadge";
import { Star } from "lucide-react";

interface ContactDetailsPanelProps {
  conversation: ConversationDto;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="px-4 py-3 border-b border-gray-100">
      <div className="text-[11px] uppercase tracking-wide text-gray-400 font-medium mb-1.5">{title}</div>
      {children}
    </div>
  );
}

function formatLocation(c: ConversationDto): string | null {
  const parts = [c.locationCity, c.locationCountry].filter(Boolean);
  return parts.length ? parts.join(", ") : null;
}

const ContactDetailsPanel: React.FC<ContactDetailsPanelProps> = ({ conversation }) => {
  const displayName = conversation.contactName ?? conversation.contactEmail ?? "Neznámý";
  const location = formatLocation(conversation);
  const hasRating = typeof conversation.rating === "number";

  return (
    <aside className="h-full w-full overflow-y-auto bg-white border-l border-gray-200">
      <div className="px-4 py-4 border-b border-gray-200 flex items-center gap-3">
        <div className="flex-shrink-0 w-10 h-10 rounded-full bg-blue-500 text-white flex items-center justify-center text-sm font-medium">
          {displayName.slice(0, 2).toUpperCase()}
        </div>
        <div className="min-w-0">
          <div className="font-semibold text-sm text-gray-900 truncate">{displayName}</div>
          {conversation.contactEmail && (
            <div className="text-xs text-gray-500 truncate">{conversation.contactEmail}</div>
          )}
        </div>
      </div>

      <Section title="Stav">
        <StatusPill status={conversation.status} />
        {conversation.closeType && (
          <div className="text-xs text-gray-500 mt-1.5">Uzavřeno: {conversation.closeType}</div>
        )}
      </Section>

      {hasRating && (
        <Section title="Hodnocení">
          <div data-testid="rating" className="flex items-center gap-1 text-amber-500">
            <Star className="w-4 h-4 fill-amber-400" />
            <span className="text-sm font-medium text-gray-900">{conversation.rating}</span>
            <span className="text-xs text-gray-400">/ 5</span>
          </div>
          {conversation.ratingText && (
            <p className="text-xs text-gray-600 mt-1.5 italic">"{conversation.ratingText}"</p>
          )}
        </Section>
      )}

      {conversation.assignedAgentIds.length > 0 && (
        <Section title="Přiřazení operátoři">
          <div className="flex flex-wrap gap-1.5">
            {conversation.assignedAgentIds.map((id) => (
              <AgentBadge key={id} agentId={id} name={id} />
            ))}
          </div>
        </Section>
      )}

      {(location || conversation.locationCode) && (
        <Section title="Lokalita">
          <div className="text-sm text-gray-700">{location ?? conversation.locationCode}</div>
        </Section>
      )}

      {(conversation.channel || conversation.domain || conversation.referer) && (
        <Section title="Zdroj">
          {conversation.channel && <div className="text-sm text-gray-700">{conversation.channel}</div>}
          {conversation.domain && <div className="text-xs text-gray-500">{conversation.domain}</div>}
          {conversation.referer && (
            <a
              href={conversation.referer}
              target="_blank"
              rel="noopener noreferrer"
              className="text-xs text-blue-600 hover:underline truncate block"
            >
              {conversation.referer}
            </a>
          )}
        </Section>
      )}

      {conversation.tags.length > 0 && (
        <Section title="Štítky">
          <div className="flex flex-wrap gap-1.5">
            {conversation.tags.map((t) => (
              <span key={t} className="inline-flex items-center rounded-full px-2 py-0.5 text-xs bg-gray-100 text-gray-700">
                {t}
              </span>
            ))}
          </div>
        </Section>
      )}
    </aside>
  );
};

export default ContactDetailsPanel;
