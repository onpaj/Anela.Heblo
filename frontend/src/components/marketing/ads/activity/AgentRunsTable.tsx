import React from 'react';
import type { AgentRun } from '../../../../api/hooks/marketingAds/types';
import { formatDateTime } from '../../../../utils/formatters';
import { AGENT_RUN_STATUS_LABELS, labelFor } from '../shared/adLabels';

const HEAD = 'px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase';
const CELL = 'px-3 py-2 text-sm text-gray-700 dark:text-graphite-muted align-top';

const czk = new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 2 });
const integer = new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 0 });

// Intl uses a no-break space as the thousands separator in cs-CZ; normalise it so the text is
// predictable in tests and copy/paste.
const normaliseSpaces = (value: string): string => value.replace(/[\u00a0\u202f]/g, ' ');

const costText = (run: AgentRun): string => {
  const parts: string[] = [];
  if (run.approxCostCzk !== null) {
    parts.push(normaliseSpaces(czk.format(run.approxCostCzk)));
  }
  if (run.approxTokens !== null) {
    parts.push(`${normaliseSpaces(integer.format(run.approxTokens))} tokenů`);
  }
  return parts.length > 0 ? parts.join(' · ') : '—';
};

const AgentRunsTable: React.FC<{ items: AgentRun[] }> = ({ items }) => (
  <table data-testid="agent-runs-table" className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
    <thead className="bg-gray-50 dark:bg-graphite-surface-2">
      <tr>
        <th className={HEAD}>Agent</th>
        <th className={HEAD}>Začátek</th>
        <th className={HEAD}>Konec</th>
        <th className={HEAD}>Stav</th>
        <th className={HEAD}>Návrhů</th>
        <th className={HEAD}>Náklad</th>
        <th className={HEAD}>Shrnutí</th>
      </tr>
    </thead>
    <tbody className="divide-y divide-gray-200 dark:divide-graphite-border">
      {items.map((run) => (
        <tr key={run.id}>
          <td className={`${CELL} text-gray-900 dark:text-graphite-text`}>
            <div>{run.agentName}</div>
            <div className="text-xs">{run.principal}</div>
          </td>
          <td className={CELL}>{formatDateTime(run.startedAt)}</td>
          <td className={CELL}>{formatDateTime(run.finishedAt)}</td>
          <td className={CELL}>{labelFor(AGENT_RUN_STATUS_LABELS, run.status)}</td>
          <td className={CELL}>{run.proposalsCreated}</td>
          <td className={CELL}>{costText(run)}</td>
          <td className={`${CELL} whitespace-pre-wrap break-words`}>{run.summary ?? '—'}</td>
        </tr>
      ))}
    </tbody>
  </table>
);

export default AgentRunsTable;
