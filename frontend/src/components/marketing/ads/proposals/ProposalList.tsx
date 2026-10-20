import React from 'react';
import type { AdProposalSummary } from '../../../../api/hooks/marketingAds/types';
import { formatDateTime } from '../../../../utils/formatters';
import { ACTION_TYPE_LABELS, CREATOR_KIND_LABELS, labelFor } from '../shared/adLabels';
import { PlatformBadge, ProposalStatusBadge } from '../shared/Badges';

interface ProposalListProps {
  items: AdProposalSummary[];
  selectedId: string | null;
  emptyText: string;
  onSelect: (id: string) => void;
}

const ProposalList: React.FC<ProposalListProps> = ({ items, selectedId, emptyText, onSelect }) => {
  if (items.length === 0) {
    return (
      <p data-testid="proposal-list-empty" className="p-4 text-sm text-gray-500 dark:text-graphite-muted">
        {emptyText}
      </p>
    );
  }

  return (
    <ul data-testid="proposal-list" className="divide-y divide-gray-200 dark:divide-graphite-border">
      {items.map((proposal) => {
        const isSelected = proposal.id === selectedId;
        return (
          <li key={proposal.id}>
            <button
              type="button"
              aria-pressed={isSelected}
              onClick={() => onSelect(proposal.id)}
              className={`w-full text-left px-4 py-3 hover:bg-gray-50 dark:hover:bg-white/5 ${isSelected ? 'bg-indigo-50 dark:bg-graphite-accent/10' : ''}`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">
                  {`${labelFor(ACTION_TYPE_LABELS, proposal.actionType)} · ${proposal.actionCount}×`}
                </span>
                <ProposalStatusBadge status={proposal.status} />
              </div>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-gray-500 dark:text-graphite-muted">
                <PlatformBadge platform={proposal.platform} />
                <span>{proposal.accountExternalId}</span>
                <span>{`· ${labelFor(CREATOR_KIND_LABELS, proposal.createdByKind)} ${proposal.createdByPrincipal}`}</span>
                <span>{`· v${proposal.currentVersion}`}</span>
                <span>{`· ${formatDateTime(proposal.createdAt)}`}</span>
              </div>
            </button>
          </li>
        );
      })}
    </ul>
  );
};

export default ProposalList;
