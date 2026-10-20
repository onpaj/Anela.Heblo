// View models for the marketing-ads UI (spec 2026-10-07 sections 6 and 8).
// Hooks map generated DTOs into these, so components never depend on generated class names
// or on whether a date arrived as a Date (generated client) or an ISO string (raw JSON).
// Enum-like values stay plain strings: the server may add values (action types are additive)
// and the UI must show an unknown value rather than break.

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

export const PROPOSAL_STATUS = {
  Pending: 'Pending',
  Approved: 'Approved',
  Executing: 'Executing',
  Executed: 'Executed',
  PartiallyExecuted: 'PartiallyExecuted',
  Failed: 'Failed',
  Rejected: 'Rejected',
  Expired: 'Expired',
  Reverted: 'Reverted',
} as const;

export const LIMITS_OUTCOME = {
  Allowed: 'Allowed',
  NeedsSecondApprover: 'NeedsSecondApprover',
  NeedsWebApproval: 'NeedsWebApproval',
  Denied: 'Denied',
} as const;

export const AD_PLATFORMS = ['GoogleAds', 'MetaAds', 'Sklik'] as const;
export const AUTONOMY_MODES = ['ProposeOnly', 'AutoWithinLimits', 'Auto'] as const;
export const CHANGE_ORIGINS = ['OutOfBand', 'Heblo'] as const;
export const CHANGE_ACTOR_KINDS = ['User', 'PlatformAutomation', 'Unknown', 'Heblo'] as const;

export interface AdProposalSummary {
  id: string;
  currentVersion: number;
  status: string;
  platform: string;
  accountExternalId: string;
  actionType: string;
  actionCount: number;
  createdByPrincipal: string;
  createdByKind: string;
  createdAt: Date | null;
  expiresAt: Date | null;
  /** Human-readable change lines rendered by the server (Czech); shown as text nodes only. */
  diffLines: string[];
}

export interface AdLimitsEvaluation {
  outcome: string;
  reason: string | null;
}

export interface AdProposalApproval {
  version: number;
  principal: string;
  channel: string;
  decision: string;
  at: Date | null;
  reason: string | null;
}

export interface AdActionResult {
  actionIndex: number;
  outcome: string;
  beforeValue: string | null;
  afterValue: string | null;
  error: string | null;
  executedAt: Date | null;
  validateOnly: boolean;
  revertState: string;
  revertError: string | null;
}

export interface AdProposalVersionInfo {
  version: number;
  createdBy: string;
  createdAt: Date | null;
  actionCount: number;
}

export interface AdProposalDetail extends AdProposalSummary {
  reasoning: string;
  limits: AdLimitsEvaluation | null;
  approvals: AdProposalApproval[];
  results: AdActionResult[];
  versions: AdProposalVersionInfo[];
}

export interface AdAuditEvent {
  id: string;
  proposalId: string | null;
  version: number | null;
  eventType: string;
  actorPrincipal: string;
  actorKind: string;
  channel: string;
  occurredAt: Date | null;
  payloadJson: string | null;
}

export interface AdChangeEvent {
  id: string;
  platform: string;
  accountExternalId: string;
  occurredAt: Date | null;
  actor: string | null;
  actorKind: string;
  entityLevel: string | null;
  entityExternalId: string | null;
  entityName: string | null;
  changeType: string;
  /** Raw JSON text from the platform; show as text, never parse into HTML. */
  oldValue: string | null;
  newValue: string | null;
  source: string;
  origin: string;
}

export interface AgentRun {
  id: string;
  agentName: string;
  principal: string;
  startedAt: Date | null;
  finishedAt: Date | null;
  status: string;
  summary: string | null;
  proposalsCreated: number;
  approxCostCzk: number | null;
  approxTokens: number | null;
}

export interface AdAutonomySetting {
  platform: string;
  actionType: string;
  mode: string;
  approvedWithoutEditRate: number | null;
  decidedCount: number;
}

export interface AdLimitSetting {
  platform: string;
  actionType: string;
  key: string;
  value: number;
}

export interface AdKillSwitchOverride {
  platform: string;
  executionEnabled: boolean;
}

export interface AdKillSwitchState {
  executionEnabled: boolean;
  platformOverrides: AdKillSwitchOverride[];
}

export interface AdsSettings {
  autonomy: AdAutonomySetting[];
  limits: AdLimitSetting[];
  killSwitch: AdKillSwitchState;
}
