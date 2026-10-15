import type {
  AdActionResult,
  AdAuditEvent,
  AdChangeEvent,
  AdProposalApproval,
  AdProposalDetail,
  AdProposalSummary,
  AdProposalVersionInfo,
  AdAutonomySetting,
  AdsSettings,
  AgentRun,
  PagedResult,
} from './types';

// ---- Wire shapes ---------------------------------------------------------------------------
// Structural mirrors of the generated DTOs (all fields optional, enums as string). A generated
// class is assignable to these as long as its field names match, so a renamed field surfaces as
// a compile error in the hook that passes it here — fix the field name in the interface below.

export type WireDate = Date | string | null | undefined;
type WireId = number | string | null | undefined;

export interface AdProposalSummaryWire {
  id?: WireId;
  currentVersion?: number;
  status?: string;
  platform?: string;
  accountExternalId?: string;
  actionType?: string;
  actionCount?: number;
  createdByPrincipal?: string;
  createdByKind?: string;
  createdAt?: WireDate;
  expiresAt?: WireDate;
  diffLines?: string[] | null;
}

export interface AdLimitsEvaluationWire {
  decision?: string;
  reason?: string | null;
}

export interface AdProposalApprovalWire {
  version?: number;
  principal?: string;
  channel?: string;
  decision?: string;
  at?: WireDate;
  reason?: string | null;
}

export interface AdActionExecutionWire {
  version?: number;
  actionIndex?: number;
  state?: string;
  beforeValue?: string | null;
  afterValue?: string | null;
  error?: string | null;
  validateOnly?: boolean;
  executedAt?: WireDate;
  revertState?: string;
  revertError?: string | null;
}

export interface AdProposalVersionWire {
  version?: number;
  actionCount?: number;
  reasoning?: string | null;
  createdBy?: string;
  createdAt?: WireDate;
  diffLines?: string[] | null;
}

export interface AdProposalDetailWire {
  summary?: AdProposalSummaryWire | null;
  versions?: AdProposalVersionWire[] | null;
  approvals?: AdProposalApprovalWire[] | null;
  executions?: AdActionExecutionWire[] | null;
  currentEvaluation?: AdLimitsEvaluationWire | null;
}

export interface AdAuditEventWire {
  id?: WireId;
  proposalId?: WireId;
  version?: number | null;
  eventType?: string;
  actorPrincipal?: string;
  actorKind?: string;
  channel?: string;
  occurredAt?: WireDate;
  payloadJson?: string | null;
}

export interface AdChangeEventWire {
  id?: WireId;
  platform?: string;
  accountExternalId?: string;
  occurredAt?: WireDate;
  actor?: string | null;
  actorKind?: string;
  entityLevel?: string | null;
  entityExternalId?: string | null;
  entityName?: string | null;
  changeType?: string;
  oldValueJson?: string | null;
  newValueJson?: string | null;
  source?: string;
  origin?: string;
}

export interface AgentRunWire {
  id?: WireId;
  agentName?: string;
  principal?: string;
  startedAt?: WireDate;
  finishedAt?: WireDate;
  status?: string;
  summary?: string | null;
  proposalsCreated?: number;
  inputTokens?: number | null;
  outputTokens?: number | null;
  costCzk?: number | null;
}

export interface AdAutonomySettingWire {
  platform?: string;
  actionType?: string;
  mode?: string;
}

export interface AdLimitSettingWire {
  platform?: string;
  actionType?: string;
  key?: string;
  value?: number;
}

export interface AdKillSwitchWire {
  scope?: string;
  executionEnabled?: boolean;
}

export interface AdApprovalRateWire {
  platform?: string;
  actionType?: string;
  decided?: number;
  approvedWithoutEdit?: number;
  rate?: number | null;
}

export interface AdsSettingsWire {
  autonomy?: AdAutonomySettingWire[] | null;
  limits?: AdLimitSettingWire[] | null;
  killSwitches?: AdKillSwitchWire[] | null;
  isExecutionEnabledGlobally?: boolean;
  approvalRates?: AdApprovalRateWire[] | null;
}

export interface PagedWire<W> {
  items?: W[] | null;
  totalCount?: number | null;
}

// ---- Primitive helpers ---------------------------------------------------------------------

export const toDate = (value: WireDate): Date | null => {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
};

/**
 * The view model keeps ids opaque (strings, as they also arrive from the URL). This is the one
 * place that converts back to the API's id type (proposal ids are numbers).
 */
export const toApiId = (id: string): number => Number(id);

const GLOBAL_KILL_SWITCH_SCOPE = 'Global';

const text = (value: string | null | undefined): string => value ?? '';

const textOrNull = (value: string | null | undefined): string | null =>
  value === null || value === undefined || value === '' ? null : value;

const count = (value: number | null | undefined): number =>
  typeof value === 'number' && Number.isFinite(value) ? value : 0;

const countOrNull = (value: number | null | undefined): number | null =>
  typeof value === 'number' && Number.isFinite(value) ? value : null;

const idText = (value: WireId): string => (value === null || value === undefined ? '' : String(value));

const idOrNull = (value: WireId): string | null =>
  value === null || value === undefined || value === '' ? null : String(value);

const lines = (value: string[] | null | undefined): string[] => [...(value ?? [])];

// ---- Mappers -------------------------------------------------------------------------------

export const mapPaged = <W, V>(wire: PagedWire<W>, map: (item: W) => V): PagedResult<V> => ({
  items: (wire.items ?? []).map((item) => map(item)),
  totalCount: count(wire.totalCount),
});

const mapApproval = (wire: AdProposalApprovalWire): AdProposalApproval => ({
  version: count(wire.version),
  principal: text(wire.principal),
  channel: text(wire.channel),
  decision: text(wire.decision),
  at: toDate(wire.at),
  reason: textOrNull(wire.reason),
});

const mapExecution = (wire: AdActionExecutionWire): AdActionResult => ({
  actionIndex: count(wire.actionIndex),
  outcome: text(wire.state),
  beforeValue: textOrNull(wire.beforeValue),
  afterValue: textOrNull(wire.afterValue),
  error: textOrNull(wire.error),
  executedAt: toDate(wire.executedAt),
  validateOnly: wire.validateOnly ?? false,
  revertState: text(wire.revertState),
  revertError: textOrNull(wire.revertError),
});

const mapVersion = (wire: AdProposalVersionWire): AdProposalVersionInfo => ({
  version: count(wire.version),
  createdBy: text(wire.createdBy),
  createdAt: toDate(wire.createdAt),
  actionCount: count(wire.actionCount),
});

export const mapProposalSummary = (wire: AdProposalSummaryWire): AdProposalSummary => ({
  id: idText(wire.id),
  currentVersion: count(wire.currentVersion),
  status: text(wire.status),
  platform: text(wire.platform),
  accountExternalId: text(wire.accountExternalId),
  actionType: text(wire.actionType),
  actionCount: count(wire.actionCount),
  createdByPrincipal: text(wire.createdByPrincipal),
  createdByKind: text(wire.createdByKind),
  createdAt: toDate(wire.createdAt),
  expiresAt: toDate(wire.expiresAt),
  diffLines: lines(wire.diffLines),
});

export const mapProposalDetail = (wire: AdProposalDetailWire): AdProposalDetail => {
  const summary = mapProposalSummary(wire.summary ?? {});
  const currentVersion = (wire.versions ?? []).find((v) => v.version === summary.currentVersion);
  const evaluation = wire.currentEvaluation;

  return {
    ...summary,
    reasoning: text(currentVersion?.reasoning),
    diffLines: currentVersion ? lines(currentVersion.diffLines) : summary.diffLines,
    limits: evaluation ? { outcome: text(evaluation.decision), reason: textOrNull(evaluation.reason) } : null,
    approvals: (wire.approvals ?? []).map(mapApproval),
    results: (wire.executions ?? [])
      .filter((execution) => execution.version === summary.currentVersion)
      .map(mapExecution),
    versions: (wire.versions ?? []).map(mapVersion),
  };
};

export const mapAuditEvent = (wire: AdAuditEventWire): AdAuditEvent => ({
  id: idText(wire.id),
  proposalId: idOrNull(wire.proposalId),
  version: countOrNull(wire.version),
  eventType: text(wire.eventType),
  actorPrincipal: text(wire.actorPrincipal),
  actorKind: text(wire.actorKind),
  channel: text(wire.channel),
  occurredAt: toDate(wire.occurredAt),
  payloadJson: textOrNull(wire.payloadJson),
});

export const mapChangeEvent = (wire: AdChangeEventWire): AdChangeEvent => ({
  id: idText(wire.id),
  platform: text(wire.platform),
  accountExternalId: text(wire.accountExternalId),
  occurredAt: toDate(wire.occurredAt),
  actor: textOrNull(wire.actor),
  actorKind: text(wire.actorKind),
  entityLevel: textOrNull(wire.entityLevel),
  entityExternalId: textOrNull(wire.entityExternalId),
  entityName: textOrNull(wire.entityName),
  changeType: text(wire.changeType),
  oldValue: textOrNull(wire.oldValueJson),
  newValue: textOrNull(wire.newValueJson),
  source: text(wire.source),
  origin: text(wire.origin),
});

const sumTokens = (input: number | null | undefined, output: number | null | undefined): number | null => {
  const parts = [countOrNull(input), countOrNull(output)].filter((v): v is number => v !== null);
  return parts.length === 0 ? null : parts.reduce((total, v) => total + v, 0);
};

export const mapAgentRun = (wire: AgentRunWire): AgentRun => ({
  id: idText(wire.id),
  agentName: text(wire.agentName),
  principal: text(wire.principal),
  startedAt: toDate(wire.startedAt),
  finishedAt: toDate(wire.finishedAt),
  status: text(wire.status),
  summary: textOrNull(wire.summary),
  proposalsCreated: count(wire.proposalsCreated),
  approxCostCzk: countOrNull(wire.costCzk),
  approxTokens: sumTokens(wire.inputTokens, wire.outputTokens),
});

const mapAutonomy = (row: AdAutonomySettingWire, rates: AdApprovalRateWire[]): AdAutonomySetting => {
  const rate = rates.find((r) => r.platform === row.platform && r.actionType === row.actionType);
  return {
    platform: text(row.platform),
    actionType: text(row.actionType),
    mode: text(row.mode),
    approvedWithoutEditRate: countOrNull(rate?.rate),
    decidedCount: count(rate?.decided),
  };
};

export const mapSettings = (wire: AdsSettingsWire): AdsSettings => {
  const rates = wire.approvalRates ?? [];
  return {
    autonomy: (wire.autonomy ?? []).map((row) => mapAutonomy(row, rates)),
    limits: (wire.limits ?? []).map((row) => ({
      platform: text(row.platform),
      actionType: text(row.actionType),
      key: text(row.key),
      value: count(row.value),
    })),
    // Computed server-side (global row exists AND is on); a missing flag fails closed (spec 6.6).
    killSwitch: {
      executionEnabled: wire.isExecutionEnabledGlobally ?? false,
      platformOverrides: (wire.killSwitches ?? [])
        .filter((row) => row.scope !== GLOBAL_KILL_SWITCH_SCOPE)
        .map((row) => ({ platform: text(row.scope), executionEnabled: row.executionEnabled ?? false })),
    },
  };
};
