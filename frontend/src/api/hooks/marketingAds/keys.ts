export interface PageParams {
  page: number;
  pageSize: number;
}

export interface ProposalListParams extends PageParams {
  status: string | null;
}

export interface ChangeEventFilter {
  platform: string | null;
  origin: string | null;
  actorKind: string | null;
}

export type ChangeEventParams = ChangeEventFilter & PageParams;

const ROOT = ['marketing-ads'] as const;

export const marketingAdsKeys = {
  all: ROOT,
  proposals: () => [...ROOT, 'proposals'] as const,
  proposalLists: () => [...ROOT, 'proposals', 'list'] as const,
  proposalList: (params: ProposalListParams) => [...ROOT, 'proposals', 'list', params] as const,
  proposalDetail: (id: string) => [...ROOT, 'proposals', 'detail', id] as const,
  auditAll: () => [...ROOT, 'audit'] as const,
  audit: (params: PageParams) => [...ROOT, 'audit', params] as const,
  changeEvents: (params: ChangeEventParams) => [...ROOT, 'change-events', params] as const,
  agentRuns: (params: PageParams) => [...ROOT, 'agent-runs', params] as const,
  settings: () => [...ROOT, 'settings'] as const,
};
