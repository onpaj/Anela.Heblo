import React from 'react';
import type { AdChangeEvent } from '../../../../api/hooks/marketingAds/types';
import { formatDateTime } from '../../../../utils/formatters';
import {
  CHANGE_ACTOR_KIND_LABELS,
  CHANGE_SOURCE_LABELS,
  ENTITY_LEVEL_LABELS,
  labelFor,
} from '../shared/adLabels';
import { OriginBadge, PlatformBadge } from '../shared/Badges';

const HEAD = 'px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase';
const CELL = 'px-3 py-2 text-sm text-gray-700 dark:text-graphite-muted align-top';

const entityText = (event: AdChangeEvent): string => {
  if (!event.entityLevel && !event.entityName && !event.entityExternalId) {
    return '—';
  }
  const level = event.entityLevel ? `${labelFor(ENTITY_LEVEL_LABELS, event.entityLevel)} ` : '';
  return `${level}${event.entityName ?? event.entityExternalId ?? ''}`;
};

const ChangeEventsTable: React.FC<{ items: AdChangeEvent[] }> = ({ items }) => (
  <table data-testid="change-events-table" className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
    <thead className="bg-gray-50 dark:bg-graphite-surface-2">
      <tr>
        <th className={HEAD}>Čas</th>
        <th className={HEAD}>Platforma</th>
        <th className={HEAD}>Objekt</th>
        <th className={HEAD}>Změna</th>
        <th className={HEAD}>Aktér</th>
        <th className={HEAD}>Původ</th>
        <th className={HEAD}>Zdroj</th>
      </tr>
    </thead>
    <tbody className="divide-y divide-gray-200 dark:divide-graphite-border">
      {items.map((event) => (
        <tr key={event.id}>
          <td className={CELL}>{formatDateTime(event.occurredAt)}</td>
          <td className={CELL}>
            <PlatformBadge platform={event.platform} />
            <div className="font-mono text-xs text-gray-400 dark:text-graphite-faint">{event.accountExternalId}</div>
          </td>
          <td className={`${CELL} text-gray-900 dark:text-graphite-text`}>{entityText(event)}</td>
          <td className={CELL}>
            <div>{event.changeType}</div>
            <div className="text-xs break-all">{`${event.oldValue ?? '—'} → ${event.newValue ?? '—'}`}</div>
          </td>
          <td className={CELL}>
            <div>{event.actor ?? '—'}</div>
            <div className="text-xs">{labelFor(CHANGE_ACTOR_KIND_LABELS, event.actorKind)}</div>
          </td>
          <td className={CELL}>
            <OriginBadge origin={event.origin} />
          </td>
          <td className={CELL}>{labelFor(CHANGE_SOURCE_LABELS, event.source)}</td>
        </tr>
      ))}
    </tbody>
  </table>
);

export default ChangeEventsTable;
