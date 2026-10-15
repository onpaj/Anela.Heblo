import type { AdProposalDetail, AdProposalSummary } from '../../../../api/hooks/marketingAds/types';

const DIFF_LINES = ['Přidat vylučující slovo „zdarma“ (přesná shoda) do kampaně „Brand CZ“ [111]'];

export const buildProposalSummary = (overrides: Partial<AdProposalSummary> = {}): AdProposalSummary => ({
  id: '7',
  currentVersion: 2,
  status: 'Pending',
  platform: 'GoogleAds',
  accountExternalId: '123-456-7890',
  actionType: 'AddNegativeKeyword',
  actionCount: 1,
  createdByPrincipal: 'heblo-marketing-agent',
  createdByKind: 'Agent',
  createdAt: new Date('2026-10-07T08:00:00Z'),
  expiresAt: new Date('2026-10-10T08:00:00Z'),
  diffLines: DIFF_LINES,
  ...overrides,
});

export const buildProposalDetail = (overrides: Partial<AdProposalDetail> = {}): AdProposalDetail => ({
  ...buildProposalSummary(),
  reasoning: 'Hledání „zdarma“ za 30 dní nepřineslo žádnou konverzi.',
  limits: { outcome: 'Allowed', reason: null },
  approvals: [],
  results: [],
  versions: [
    { version: 1, createdBy: 'heblo-marketing-agent', createdAt: new Date('2026-10-07T07:00:00Z'), actionCount: 1 },
    { version: 2, createdBy: 'heblo-marketing-agent', createdAt: new Date('2026-10-07T08:00:00Z'), actionCount: 1 },
  ],
  ...overrides,
});
