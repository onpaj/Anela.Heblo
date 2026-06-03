import React, { type ReactNode } from 'react';
import { ScanLine } from 'lucide-react';
import TransportBoxStateBadge from '../../transport/box-detail/components/TransportBoxStateBadge';

interface SubjectHeaderProps {
  /** scanned code "in hand"; absence renders the empty prompt */
  code?: string | null;
  /** transport-box state key (drives the badge via stateColors/stateLabels) */
  state?: string;
  /** key facts line(s) — item count, expiry, etc. */
  facts?: ReactNode;
  /** prompt shown before the first scan */
  emptyPrompt?: string;
}

export const SubjectHeader: React.FC<SubjectHeaderProps> = ({
  code, state, facts, emptyPrompt = 'Naskenujte kód',
}) => {
  if (!code) {
    return (
      <div data-testid="subject-empty"
           className="flex items-center gap-3 px-4 py-4 text-neutral-gray">
        <ScanLine className="h-6 w-6" />
        <span className="text-base font-medium">{emptyPrompt}</span>
      </div>
    );
  }
  return (
    <div data-testid="subject-header"
         className="flex items-center justify-between gap-3 px-4 py-3 bg-white border-b border-border-light">
      <div className="min-w-0">
        <div className="font-mono text-lg font-semibold text-neutral-slate truncate">{code}</div>
        {facts && <div className="text-sm text-neutral-gray truncate">{facts}</div>}
      </div>
      {state && <TransportBoxStateBadge state={state} size="md" />}
    </div>
  );
};
