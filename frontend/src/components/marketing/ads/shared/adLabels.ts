// Czech labels for server enum values. Unknown values fall back to the raw value (labelFor), so a
// value added on the server later (spec 4.3: action types are additive) is visible, never blank.

type LabelMap = Readonly<Record<string, string>>;

export const PLATFORM_LABELS: LabelMap = { GoogleAds: 'Google Ads', MetaAds: 'Meta', Sklik: 'Sklik' };

export const PROPOSAL_STATUS_LABELS: LabelMap = {
  Pending: 'Čeká na schválení',
  Approved: 'Schváleno',
  Executing: 'Provádí se',
  Executed: 'Provedeno',
  PartiallyExecuted: 'Částečně provedeno',
  Failed: 'Selhalo',
  Rejected: 'Zamítnuto',
  Expired: 'Vypršelo',
  Reverted: 'Vráceno zpět',
};

export const ACTION_TYPE_LABELS: LabelMap = {
  AddNegativeKeyword: 'Vylučující klíčová slova',
  PauseAd: 'Pozastavení reklam',
};

export const ENTITY_LEVEL_LABELS: LabelMap = {
  Campaign: 'kampaň',
  AdGroup: 'sestava',
  Keyword: 'klíčové slovo',
  NegativeKeyword: 'vylučující slovo',
  Ad: 'reklama',
};

export const MATCH_TYPE_LABELS: LabelMap = { Exact: 'přesná shoda', Phrase: 'frázová shoda', Broad: 'volná shoda' };

export const LIMITS_OUTCOME_LABELS: LabelMap = {
  Allowed: 'V limitech',
  NeedsSecondApprover: 'Vyžaduje druhého schvalovatele',
  NeedsWebApproval: 'Vyžaduje schválení ve webu',
  Denied: 'Zamítnuto limity',
};

export const AUTONOMY_MODE_LABELS: LabelMap = {
  ProposeOnly: 'Jen návrhy',
  AutoWithinLimits: 'Automaticky v limitech',
  Auto: 'Automaticky',
};

export const CHANNEL_LABELS: LabelMap = { Web: 'Web', Mcp: 'Claude (MCP)', System: 'Systém' };

export const CREATOR_KIND_LABELS: LabelMap = { Agent: 'Agent', Human: 'Člověk', System: 'Systém' };

export const DECISION_LABELS: LabelMap = { Approved: 'Schváleno', Rejected: 'Zamítnuto' };

export const ACTION_OUTCOME_LABELS: LabelMap = {
  Succeeded: 'Provedeno',
  Failed: 'Selhalo',
  StaleState: 'Mezitím změněno',
  Skipped: 'Přeskočeno',
};

export const CHANGE_ACTOR_KIND_LABELS: LabelMap = {
  User: 'Uživatel',
  PlatformAutomation: 'Automatizace platformy',
  Unknown: 'Neznámý',
  Heblo: 'Heblo',
};

export const CHANGE_ORIGIN_LABELS: LabelMap = { OutOfBand: 'Mimo Heblo', Heblo: 'Heblo' };

export const CHANGE_SOURCE_LABELS: LabelMap = {
  PlatformChangeLog: 'Historie změn platformy',
  SnapshotDiff: 'Porovnání snímků',
};

export const AUDIT_EVENT_LABELS: LabelMap = {
  Submitted: 'Odesláno',
  Revised: 'Nová verze',
  Approved: 'Schváleno',
  Rejected: 'Zamítnuto',
  Expired: 'Vypršelo',
  ExecutionStarted: 'Zahájeno provádění',
  ActionExecuted: 'Akce provedena',
  ActionFailed: 'Akce selhala',
  Reverted: 'Vráceno zpět',
  SettingChanged: 'Změna nastavení',
  KillSwitchChanged: 'Změna hlavního vypínače',
};

export const REVERT_STATE_LABELS: LabelMap = {
  NotReverted: 'Nevráceno',
  Reverted: 'Vráceno',
  RevertFailed: 'Vrácení selhalo',
};

export const AGENT_RUN_STATUS_LABELS: LabelMap = { Running: 'Běží', Succeeded: 'Dokončeno', Failed: 'Selhalo' };

export const labelFor = (labels: LabelMap, value: string | null | undefined): string => {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  return labels[value] ?? value;
};
