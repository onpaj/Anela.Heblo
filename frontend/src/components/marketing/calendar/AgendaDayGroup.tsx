import React from 'react';
import { AgendaEventCard } from './AgendaEventCard';
import type { AgendaDay } from './agendaGrouping';

const CZECH_WEEKDAYS = ['Ne', 'Po', 'Út', 'St', 'Čt', 'Pá', 'So'];
const CZECH_MONTHS_GENITIVE = [
  'ledna', 'února', 'března', 'dubna', 'května', 'června',
  'července', 'srpna', 'září', 'října', 'listopadu', 'prosince',
];

function buildDayLabel(dateStr: string): string {
  const [y, m, d] = dateStr.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  const weekday = CZECH_WEEKDAYS[date.getDay()];
  const month = CZECH_MONTHS_GENITIVE[m - 1];
  return `${weekday} ${d}. ${month}`;
}

interface AgendaDayGroupProps {
  day: AgendaDay;
  isToday: boolean;
  onEventClick: (id: number) => void;
}

export function AgendaDayGroup({ day, isToday, onEventClick }: AgendaDayGroupProps) {
  const headerClass = [
    'agenda-day-group__header',
    isToday ? 'agenda-day-group__header--today' : '',
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div className="agenda-day-group">
      <div className={headerClass}>{buildDayLabel(day.date)}</div>
      <div className="agenda-day-group__events">
        {day.events.length === 0 ? (
          <p className="agenda-day-group__empty">Žádné akce</p>
        ) : (
          day.events.map((event) => (
            <AgendaEventCard
              key={event.id}
              event={event}
              onClick={() => onEventClick(event.id)}
            />
          ))
        )}
      </div>
    </div>
  );
}
