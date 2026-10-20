import React from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useAdAuditEventsQuery, useAdChangeEventsQuery, useAgentRunsQuery } from '../useMarketingAdsActivity';

const mockAudit = jest.fn();
const mockChanges = jest.fn();
const mockRuns = jest.fn();
jest.mock('../../client', () => ({
  getAuthenticatedApiClient: () => ({
    marketingAdsActivity_ListAuditEvents: (...args: unknown[]) => mockAudit(...args),
    marketingAdsActivity_ListChangeEvents: (...args: unknown[]) => mockChanges(...args),
    marketingAdsActivity_ListAgentRuns: (...args: unknown[]) => mockRuns(...args),
  }),
}));

const createWrapper = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return Wrapper;
};

describe('useMarketingAdsActivity', () => {
  it('loads audit events and maps proposal ids to strings', async () => {
    mockAudit.mockResolvedValue({ items: [{ id: 1, proposalId: 7, eventType: 'Approved', occurredAt: '2026-10-07T08:00:00Z' }], totalCount: 1 });

    const { result } = renderHook(() => useAdAuditEventsQuery({ page: 1, pageSize: 50 }), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockAudit).toHaveBeenCalledWith(undefined, undefined, 1, 50);
    expect(result.current.data?.items[0].proposalId).toBe('7');
  });

  it('passes platform, origin and actor kind filters, with null as undefined', async () => {
    mockChanges.mockResolvedValue({ events: [], totalCount: 0 });

    const { result } = renderHook(
      () => useAdChangeEventsQuery({ platform: 'GoogleAds', origin: 'OutOfBand', actorKind: null, page: 1, pageSize: 50 }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockChanges).toHaveBeenCalledWith('GoogleAds', undefined, 'OutOfBand', undefined, undefined, undefined, 1, 50);
  });

  it('reads change events from the events property and maps them', async () => {
    mockChanges.mockResolvedValue({
      events: [{ id: 5, entityExternalId: '111', oldValueJson: '"a"', newValueJson: '"b"', occurredAt: '2026-10-07T08:00:00Z' }],
      totalCount: 1,
    });

    const { result } = renderHook(
      () => useAdChangeEventsQuery({ platform: null, origin: null, actorKind: null, page: 1, pageSize: 50 }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.totalCount).toBe(1);
    expect(result.current.data?.items[0]).toEqual(
      expect.objectContaining({ id: '5', entityExternalId: '111', oldValue: '"a"', newValue: '"b"' }),
    );
  });

  it('does not load agent runs while disabled', () => {
    renderHook(() => useAgentRunsQuery({ page: 1, pageSize: 50 }, false), { wrapper: createWrapper() });

    expect(mockRuns).not.toHaveBeenCalled();
  });

  it('loads agent runs when enabled', async () => {
    mockRuns.mockResolvedValue({ items: [{ id: 'g3', agentName: 'paid-search', proposalsCreated: 2, inputTokens: 10, outputTokens: 5 }], totalCount: 1 });

    const { result } = renderHook(() => useAgentRunsQuery({ page: 1, pageSize: 50 }), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockRuns).toHaveBeenCalledWith(1, 50);
    expect(result.current.data?.items[0].agentName).toBe('paid-search');
    expect(result.current.data?.items[0].approxTokens).toBe(15);
  });
});
