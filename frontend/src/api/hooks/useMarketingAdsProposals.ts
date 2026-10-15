import { useCallback } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { ApproveAdProposalRequest, RejectAdProposalRequest, type ApiClient } from '../generated/api-client';
import { marketingAdsKeys, type ProposalListParams } from './marketingAds/keys';
import {
  mapPaged,
  mapProposalDetail,
  mapProposalSummary,
  toApiId,
  type AdProposalSummaryWire,
} from './marketingAds/mappers';
import type { AdProposalDetail, AdProposalSummary, PagedResult } from './marketingAds/types';

const PROPOSAL_LIST_REFETCH_MS = 60 * 1000;

type ListProposalsArgs = Parameters<ApiClient['marketingAdsProposals_ListProposals']>;

export interface ProposalRef {
  id: string;
}

export interface ProposalDecision extends ProposalRef {
  version: number;
}

export interface ProposalRejection extends ProposalDecision {
  reason: string;
}

export const useAdProposalsQuery = (params: ProposalListParams) =>
  useQuery<PagedResult<AdProposalSummary>, Error>({
    queryKey: marketingAdsKeys.proposalList(params),
    queryFn: async () => {
      const client = getAuthenticatedApiClient();
      // status, platform, mine, page, pageSize, oldestFirst (server default = newest first)
      const response = await client.marketingAdsProposals_ListProposals(
        (params.status ?? undefined) as ListProposalsArgs[0],
        undefined,
        undefined,
        params.page,
        params.pageSize,
        undefined,
      );
      return mapPaged<AdProposalSummaryWire, AdProposalSummary>(response, mapProposalSummary);
    },
    refetchInterval: PROPOSAL_LIST_REFETCH_MS,
  });

/**
 * The detail never refetches on its own (no focus refetch, no interval): the approve button sends
 * the version the user is reading, so the content must not change underneath them. A newer
 * version is picked up only through useRefreshAdProposals (explicit "Obnovit detail"), or by
 * reopening it: gcTime 0 drops a closed detail, so reopening loads it before anything is read.
 */
export const useAdProposalQuery = (id: string | null) =>
  useQuery<AdProposalDetail, Error>({
    queryKey: marketingAdsKeys.proposalDetail(id ?? ''),
    queryFn: async () => {
      const client = getAuthenticatedApiClient();
      const response = await client.marketingAdsProposals_GetProposal(toApiId(id ?? ''));
      if (!response.proposal) {
        throw new Error('Návrh nebyl nalezen.');
      }
      return mapProposalDetail(response.proposal);
    },
    enabled: id !== null && id !== '',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    staleTime: Infinity,
    gcTime: 0,
  });

/** Refetches the lists and the one decided/viewed detail — never another open detail (its version must not move under its reader). */
const useInvalidateProposal = () => {
  const queryClient = useQueryClient();
  return useCallback(
    (id: string, alsoAudit: boolean) => {
      const keys = alsoAudit
        ? [marketingAdsKeys.proposalLists(), marketingAdsKeys.proposalDetail(id), marketingAdsKeys.auditAll()]
        : [marketingAdsKeys.proposalLists(), marketingAdsKeys.proposalDetail(id)];
      return Promise.all(keys.map((queryKey) => queryClient.invalidateQueries({ queryKey })));
    },
    [queryClient],
  );
};

export const useRefreshAdProposals = (id: string): (() => void) => {
  const invalidate = useInvalidateProposal();
  return useCallback(() => {
    void invalidate(id, false);
  }, [invalidate, id]);
};

/** Returned from onSuccess, so the mutation stays pending until the refetch lands and the buttons cannot re-enable on stale data. */
const useInvalidateAfterDecision = () => {
  const invalidate = useInvalidateProposal();
  return (_data: void, { id }: ProposalRef) => invalidate(id, true);
};

export const useApproveAdProposalMutation = () => {
  const invalidate = useInvalidateAfterDecision();
  return useMutation<void, unknown, ProposalDecision>({
    mutationFn: async ({ id, version }) => {
      const client = getAuthenticatedApiClient(false);
      const apiId = toApiId(id);
      await client.marketingAdsProposals_ApproveProposal(apiId, new ApproveAdProposalRequest({ id: apiId, version }));
    },
    onSuccess: invalidate,
  });
};

export const useRejectAdProposalMutation = () => {
  const invalidate = useInvalidateAfterDecision();
  return useMutation<void, unknown, ProposalRejection>({
    mutationFn: async ({ id, version, reason }) => {
      const client = getAuthenticatedApiClient(false);
      const apiId = toApiId(id);
      await client.marketingAdsProposals_RejectProposal(apiId, new RejectAdProposalRequest({ id: apiId, version, reason }));
    },
    onSuccess: invalidate,
  });
};

export const useRevertAdProposalMutation = () => {
  const invalidate = useInvalidateAfterDecision();
  return useMutation<void, unknown, ProposalRef>({
    mutationFn: async ({ id }) => {
      const client = getAuthenticatedApiClient(false);
      await client.marketingAdsProposals_RevertProposal(toApiId(id));
    },
    onSuccess: invalidate,
  });
};
