import React from "react";
import { ConversationDto, ConversationSummaryDto } from "../../../api/hooks/useSmartsupp";
import StatusPill from "./StatusPill";
import AgentBadge from "./AgentBadge";
import { Star } from "lucide-react";
import { countryCodeToFlag } from "./utils/countryCodeToFlag";

interface ContactDetailsPanelProps {
  conversation: ConversationDto;
  onSelectConversation?: (id: string) => void;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="px-4 py-3 border-b border-gray-100">
      <div className="text-[11px] uppercase tracking-wide text-gray-400 font-medium mb-1.5">{title}</div>
      {children}
    </div>
  );
}

const SHOPTET_KEYS = new Set(["shoptet_guid", "shoptet_shop", "shoptet_user_guid", "shoptet_cart_updated_at"]);

function mergedInfoEntries(
  variables: Record<string, string>,
  contactProperties: Record<string, string>
): [string, string][] {
  // variables win on key collision — they reflect live conversation state, contactProperties are static contact defaults
  const merged: Record<string, string> = { ...contactProperties, ...variables };
  const entries = Object.entries(merged);
  const shoptet = entries.filter(([k]) => SHOPTET_KEYS.has(k));
  const rest = entries.filter(([k]) => !SHOPTET_KEYS.has(k));
  return [...shoptet, ...rest];
}

function OtherConversationRow({
  conv,
  onSelect,
}: {
  conv: ConversationSummaryDto;
  onSelect?: (id: string) => void;
}) {
  const date = conv.lastMessageAt
    ? new Date(conv.lastMessageAt).toLocaleDateString("cs-CZ")
    : "—";
  return (
    <button
      data-testid={`other-conversation-${conv.id}`}
      onClick={() => onSelect?.(conv.id)}
      className="w-full text-left py-1.5 border-b border-gray-50 last:border-0"
    >
      <div className="flex items-center justify-between mb-0.5">
        <span className="text-xs text-gray-500">{date}</span>
        <span className="text-xs text-gray-500">{conv.status}</span>
      </div>
      {conv.lastMessagePreview && (
        <div className="text-xs text-gray-700 truncate">{conv.lastMessagePreview}</div>
      )}
    </button>
  );
}

const ContactDetailsPanel: React.FC<ContactDetailsPanelProps> = ({ conversation, onSelectConversation }) => {
  const displayName = conversation.contactName ?? conversation.contactEmail ?? "Neznámý";
  const hasRating = typeof conversation.rating === "number";

  const locationParts = [conversation.locationCity, conversation.locationCountry].filter(Boolean);
  const locationText = locationParts.length ? locationParts.join(", ") : null;
  const flag = conversation.locationCountry ? countryCodeToFlag(conversation.locationCountry) : "";

  const hasKontakt =
    conversation.contactPhone ||
    conversation.locationIp ||
    locationText ||
    conversation.domain ||
    conversation.referer ||
    conversation.channel;

  const infoEntries = mergedInfoEntries(conversation.variables, conversation.contactProperties);

  return (
    <aside className="h-full w-full overflow-y-auto bg-white border-l border-gray-200">
      {/* Header */}
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

      {/* Status */}
      <Section title="Stav">
        <StatusPill status={conversation.status} />
        {conversation.closeType && (
          <div className="text-xs text-gray-500 mt-1.5">Uzavřeno: {conversation.closeType}</div>
        )}
      </Section>

      {/* Rating */}
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

      {/* Assigned agents */}
      {conversation.assignedAgentIds.length > 0 && (
        <Section title="Přiřazení operátoři">
          <div className="flex flex-wrap gap-1.5">
            {conversation.assignedAgentIds.map((id) => (
              <AgentBadge key={id} agentId={id} name={id} />
            ))}
          </div>
        </Section>
      )}

      {/* Kontakt — phone, IP, location+flag, channel, domain, referer */}
      {hasKontakt && (
        <Section title="Kontakt">
          <div className="space-y-1">
            {conversation.contactPhone && (
              <div className="text-sm text-gray-700">{conversation.contactPhone}</div>
            )}
            {conversation.locationIp && (
              <div className="text-xs text-gray-500">{conversation.locationIp}</div>
            )}
            {locationText && (
              <div className="text-sm text-gray-700">
                {flag ? `${flag} ${locationText}` : locationText}
              </div>
            )}
            {conversation.channel && (
              <div className="text-sm text-gray-700">{conversation.channel}</div>
            )}
            {conversation.domain && (
              <div className="text-xs text-gray-500">{conversation.domain}</div>
            )}
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
          </div>
        </Section>
      )}

      {/* Note */}
      {conversation.contactNote && (
        <Section title="Poznámka">
          <p className="text-sm text-gray-700 whitespace-pre-wrap">{conversation.contactNote}</p>
        </Section>
      )}

      {/* Contact tags */}
      {conversation.contactTags.length > 0 && (
        <Section title="Štítky kontaktu">
          <div className="flex flex-wrap gap-1.5">
            {conversation.contactTags.map((t) => (
              <span
                key={t}
                className="inline-flex items-center rounded-full px-2 py-0.5 text-xs bg-blue-50 text-blue-700"
              >
                {t}
              </span>
            ))}
          </div>
        </Section>
      )}

      {/* Conversation tags */}
      {conversation.tags.length > 0 && (
        <Section title="Štítky">
          <div className="flex flex-wrap gap-1.5">
            {conversation.tags.map((t) => (
              <span
                key={t}
                className="inline-flex items-center rounded-full px-2 py-0.5 text-xs bg-gray-100 text-gray-700"
              >
                {t}
              </span>
            ))}
          </div>
        </Section>
      )}

      {/* Other conversations */}
      {conversation.otherConversations.length > 0 && (
        <Section title={`Jiné konverzace (${conversation.otherConversations.length})`}>
          {conversation.otherConversations.map((c) => (
            <OtherConversationRow key={c.id} conv={c} onSelect={onSelectConversation} />
          ))}
        </Section>
      )}

      {/* Informace o kontaktu — merged variables + contactProperties, Shoptet keys first */}
      {infoEntries.length > 0 && (
        <Section title="Informace o kontaktu">
          <div className="space-y-1">
            {infoEntries.map(([key, value]) => (
              <div key={key} className="flex gap-2 text-xs">
                <span className="text-gray-500 shrink-0 w-1/2 truncate">{key}</span>
                <span className="text-gray-800 truncate">{value}</span>
              </div>
            ))}
          </div>
        </Section>
      )}
    </aside>
  );
};

export default ContactDetailsPanel;
