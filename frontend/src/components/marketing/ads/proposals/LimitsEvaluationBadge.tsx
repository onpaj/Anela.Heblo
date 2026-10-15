import React from 'react';
import type { AdLimitsEvaluation } from '../../../../api/hooks/marketingAds/types';
import { LIMITS_OUTCOME_LABELS, labelFor } from '../shared/adLabels';
import { BLUE_PILL, GREEN_PILL, NEUTRAL_PILL, Pill, RED_PILL, YELLOW_PILL } from '../shared/Badges';

const OUTCOME_PILLS: Readonly<Record<string, string>> = {
  Allowed: GREEN_PILL,
  NeedsSecondApprover: YELLOW_PILL,
  NeedsWebApproval: BLUE_PILL,
  Denied: RED_PILL,
};

const LimitsEvaluationBadge: React.FC<{ limits: AdLimitsEvaluation | null }> = ({ limits }) => (
  <div data-testid="limits-evaluation" className="flex flex-wrap items-center gap-2 text-sm">
    <span className="text-gray-600 dark:text-graphite-muted">Limity:</span>
    {limits ? (
      <>
        <Pill className={OUTCOME_PILLS[limits.outcome] ?? NEUTRAL_PILL}>{labelFor(LIMITS_OUTCOME_LABELS, limits.outcome)}</Pill>
        {limits.reason && <span className="text-gray-600 dark:text-graphite-muted">{limits.reason}</span>}
      </>
    ) : (
      <span className="text-gray-500 dark:text-graphite-muted">Vyhodnocení není k dispozici</span>
    )}
  </div>
);

export default LimitsEvaluationBadge;
