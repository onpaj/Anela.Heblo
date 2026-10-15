import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import type { ApiClient } from '../generated/api-client';
import { marketingAdsKeys, type ChangeEventParams, type PageParams } from './marketingAds/keys';
import {
  mapAgentRun,
  mapAuditEvent,
  mapChangeEvent,
  mapPaged,
  type AdAuditEventWire,
  type AdChangeEventWire,
  type AgentRunWire,
} from './marketingAds/mappers';
import type { AdAuditEvent, AdChangeEvent, AgentRun, PagedResult } from './marketingAds/types';

type ChangeEventsArgs = Parameters<ApiClient['marketingAdsActivity_ListChangeEvents']>;

export const useAdAuditEventsQuery = (params: PageParams, enabled = true) =>
  useQuery<PagedResult<AdAuditEvent>, Error>({
    queryKey: marketingAdsKeys.audit(params),
    queryFn: async () => {
      // proposalId, eventType, page, pageSize
      const response = await getAuthenticatedApiClient().marketingAdsActivity_ListAuditEvents(
        undefined,
        undefined,
        params.page,
        params.pageSize,
      );
      return mapPaged<AdAuditEventWire, AdAuditEvent>(response, mapAuditEvent);
    },
    enabled,
    placeholderData: keepPreviousData,
  });

export const useAdChangeEventsQuery = (params: ChangeEventParams, enabled = true) =>
  useQuery<PagedResult<AdChangeEvent>, Error>({
    queryKey: marketingAdsKeys.changeEvents(params),
    queryFn: async () => {
      // platform, accountExternalId, origin, actorKind, from, to (server default = last 30 days), page, pageSize
      const response = await getAuthenticatedApiClient().marketingAdsActivity_ListChangeEvents(
        (params.platform ?? undefined) as ChangeEventsArgs[0],
        undefined,
        (params.origin ?? undefined) as ChangeEventsArgs[2],
        (params.actorKind ?? undefined) as ChangeEventsArgs[3],
        undefined,
        undefined,
        params.page,
        params.pageSize,
      );
      // This response names its list `events`, not `items`.
      return mapPaged<AdChangeEventWire, AdChangeEvent>(
        { items: response.events, totalCount: response.totalCount },
        mapChangeEvent,
      );
    },
    enabled,
    placeholderData: keepPreviousData,
  });

export const useAgentRunsQuery = (params: PageParams, enabled = true) =>
  useQuery<PagedResult<AgentRun>, Error>({
    queryKey: marketingAdsKeys.agentRuns(params),
    queryFn: async () => {
      const response = await getAuthenticatedApiClient().marketingAdsActivity_ListAgentRuns(params.page, params.pageSize);
      return mapPaged<AgentRunWire, AgentRun>(response, mapAgentRun);
    },
    enabled,
    placeholderData: keepPreviousData,
  });
