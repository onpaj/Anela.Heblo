import type { AdAutonomySetting } from '../../../../api/hooks/marketingAds/types';
import { ACTION_TYPE_LABELS, AUTONOMY_MODE_LABELS, PLATFORM_LABELS, labelFor } from '../shared/adLabels';

const MAIN_SWITCH_CLAUSE = 'provedou se, jen když to dovolí hlavní vypínač.';

// Server semantics (AdAutonomyService): both automatic modes auto-approve only proposals the limits allow,
// and execution still needs the kill switch (global AND platform) on.
const MODE_CONSEQUENCES: Readonly<Record<string, string>> = {
  ProposeOnly: 'Každý návrh bude čekat na schválení člověkem.',
  AutoWithinLimits: `Návrhy, které projdou limity, schválí systém automaticky; ${MAIN_SWITCH_CLAUSE}`,
  Auto: `Na serveru se zatím chová jako „Automaticky v limitech“: systém schválí jen návrhy, které projdou limity; ${MAIN_SWITCH_CLAUSE}`,
};

export const autonomyRowLabel = (row: AdAutonomySetting): string =>
  `${labelFor(PLATFORM_LABELS, row.platform)} – ${labelFor(ACTION_TYPE_LABELS, row.actionType)}`;

export const describeModeChange = (row: AdAutonomySetting, mode: string): string => {
  const transition = `${autonomyRowLabel(row)}: ${labelFor(AUTONOMY_MODE_LABELS, row.mode)} → ${labelFor(AUTONOMY_MODE_LABELS, mode)}`;
  const consequence = MODE_CONSEQUENCES[mode];
  return consequence ? `${transition}. ${consequence}` : transition;
};
