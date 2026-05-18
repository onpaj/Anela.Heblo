import React from 'react';
import { ACTION_TYPE_COLORS } from './fullcalendarAdapters';
import type { CalendarEvent } from './fullcalendarAdapters';

const ACTION_TYPE_LABELS: Record<string, string> = {
  SocialMedia: 'Sociální sítě',
  Blog: 'Blog',
  Newsletter: 'Newsletter',
  PR: 'PR',
  Event: 'Akce',
  Meeting: 'Porada',
};

function formatProductCount(count: number): string {
  if (count === 1) return '1 produkt';
  if (count >= 2 && count <= 4) return `${count} produkty`;
  return `${count} produktů`;
}

interface AgendaEventCardProps {
  event: CalendarEvent;
  onClick: () => void;
}

export function AgendaEventCard({ event, onClick }: AgendaEventCardProps) {
  const colors = ACTION_TYPE_COLORS[event.actionType] ?? { bg: '#6b7280', text: '#ffffff' };
  const label = ACTION_TYPE_LABELS[event.actionType] ?? event.actionType;
  const isMultiDay = event.dateFrom !== event.dateTo;

  return (
    <button
      type="button"
      className="agenda-event-card"
      style={{ borderLeftColor: colors.bg }}
      onClick={onClick}
      aria-label={`${event.title}, ${label}`}
    >
      <div className="agenda-event-card__header">
        <span className="agenda-event-card__title">{event.title}</span>
        <span
          className="agenda-event-card__badge"
          style={{ backgroundColor: colors.bg, color: colors.text }}
        >
          {label}
        </span>
      </div>
      <div className="agenda-event-card__meta">
        {event.associatedProducts.length > 0 && (
          <span>{formatProductCount(event.associatedProducts.length)}</span>
        )}
        {isMultiDay && <span>{event.dateFrom} – {event.dateTo}</span>}
      </div>
    </button>
  );
}
