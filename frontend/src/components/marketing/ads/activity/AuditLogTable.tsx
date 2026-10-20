import React from 'react';
import { Link } from 'react-router-dom';
import type { AdAuditEvent } from '../../../../api/hooks/marketingAds/types';
import { formatDateTime } from '../../../../utils/formatters';
import { AUDIT_EVENT_LABELS, CHANNEL_LABELS, CREATOR_KIND_LABELS, labelFor } from '../shared/adLabels';

const HEAD = 'px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase';
const CELL = 'px-3 py-2 text-sm text-gray-700 dark:text-graphite-muted align-top';

const AuditLogTable: React.FC<{ items: AdAuditEvent[] }> = ({ items }) => (
  <table data-testid="audit-table" className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
    <thead className="bg-gray-50 dark:bg-graphite-surface-2">
      <tr>
        <th className={HEAD}>Čas</th>
        <th className={HEAD}>Událost</th>
        <th className={HEAD}>Návrh</th>
        <th className={HEAD}>Kdo</th>
        <th className={HEAD}>Kanál</th>
      </tr>
    </thead>
    <tbody className="divide-y divide-gray-200 dark:divide-graphite-border">
      {items.map((event) => (
        <tr key={event.id}>
          <td className={CELL}>{formatDateTime(event.occurredAt)}</td>
          <td className={`${CELL} text-gray-900 dark:text-graphite-text`}>{labelFor(AUDIT_EVENT_LABELS, event.eventType)}</td>
          <td className={CELL}>
            {event.proposalId ? (
              <Link
                to={`/marketing/ads/proposals?tab=history&proposal=${event.proposalId}`}
                className="text-indigo-600 dark:text-graphite-accent hover:underline"
              >
                {`#${event.proposalId}`}
              </Link>
            ) : (
              '—'
            )}
            {event.version !== null && <span className="ml-1 text-xs">{`v${event.version}`}</span>}
          </td>
          <td className={CELL}>{`${event.actorPrincipal} (${labelFor(CREATOR_KIND_LABELS, event.actorKind)})`}</td>
          <td className={CELL}>{labelFor(CHANNEL_LABELS, event.channel)}</td>
        </tr>
      ))}
    </tbody>
  </table>
);

export default AuditLogTable;
