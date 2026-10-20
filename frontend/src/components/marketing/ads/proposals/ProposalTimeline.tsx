import React from 'react';
import type { AdProposalApproval, AdProposalVersionInfo } from '../../../../api/hooks/marketingAds/types';
import { formatDateTime } from '../../../../utils/formatters';
import { CHANNEL_LABELS, DECISION_LABELS, labelFor } from '../shared/adLabels';

interface ProposalTimelineProps {
  approvals: AdProposalApproval[];
  versions: AdProposalVersionInfo[];
  currentVersion: number;
}

const ITEM = 'text-xs text-gray-700 dark:text-graphite-muted';
const HEADING = 'text-sm font-semibold text-gray-900 dark:text-graphite-text mb-1';

const ProposalTimeline: React.FC<ProposalTimelineProps> = ({ approvals, versions, currentVersion }) => (
  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
    <section>
      <h3 className={HEADING}>Rozhodnutí</h3>
      {approvals.length === 0 ? (
        <p className={ITEM}>Zatím žádné rozhodnutí.</p>
      ) : (
        <ul className="space-y-1">
          {approvals.map((approval, index) => (
            <li key={`${approval.principal}-${approval.version}-${index}`} className={ITEM}>
              {`${labelFor(DECISION_LABELS, approval.decision)} · ${approval.principal} · ${labelFor(CHANNEL_LABELS, approval.channel)} · v${approval.version} · ${formatDateTime(approval.at)}`}
              {approval.version < currentVersion && (
                <span className="ml-1 text-orange-700 dark:text-orange-300">(neplatné — starší verze)</span>
              )}
              {approval.reason && <div className="italic">{approval.reason}</div>}
            </li>
          ))}
        </ul>
      )}
    </section>
    <section>
      <h3 className={HEADING}>Verze</h3>
      <ul className="space-y-1">
        {versions.map((version) => (
          <li key={version.version} className={ITEM}>
            {`v${version.version} · ${version.createdBy} · ${formatDateTime(version.createdAt)}`}
            {version.version === currentVersion && <span className="ml-1 font-medium">(aktuální)</span>}
          </li>
        ))}
      </ul>
    </section>
  </div>
);

export default ProposalTimeline;
