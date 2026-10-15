import React from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { onlineManager, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SwaggerException } from '../../generated/api-client';
import { isVersionConflict } from '../marketingAds/errors';
import {
  useAdProposalQuery,
  useAdProposalsQuery,
  useRefreshAdProposals,
  useApproveAdProposalMutation,
  useRejectAdProposalMutation,
  useRevertAdProposalMutation,
} from '../useMarketingAdsProposals';

const mockList = jest.fn();
const mockGet = jest.fn();
const mockApprove = jest.fn();
const mockReject = jest.fn();
const mockRevert = jest.fn();
jest.mock('../../client', () => ({
  getAuthenticatedApiClient: () => ({
    marketingAdsProposals_ListProposals: (...args: unknown[]) => mockList(...args),
    marketingAdsProposals_GetProposal: (...args: unknown[]) => mockGet(...args),
    marketingAdsProposals_ApproveProposal: (...args: unknown[]) => mockApprove(...args),
    marketingAdsProposals_RejectProposal: (...args: unknown[]) => mockReject(...args),
    marketingAdsProposals_RevertProposal: (...args: unknown[]) => mockRevert(...args),
  }),
}));

const createWrapper = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return Wrapper;
};

describe('useMarketingAdsProposals', () => {
  it('lists proposals with status, page and page size and maps them to view models', async () => {
    // Arrange
    mockList.mockResolvedValue({
      items: [{ id: 7, currentVersion: 2, status: 'Pending', createdAt: '2026-10-07T08:00:00Z' }],
      totalCount: 1,
    });

    // Act
    const { result } = renderHook(() => useAdProposalsQuery({ status: 'Pending', page: 1, pageSize: 25 }), {
      wrapper: createWrapper(),
    });

    // Assert
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockList).toHaveBeenCalledWith('Pending', undefined, undefined, 1, 25, undefined);
    expect(result.current.data?.items[0].id).toBe('7');
    expect(result.current.data?.items[0].createdAt).toBeInstanceOf(Date);
    expect(result.current.data?.totalCount).toBe(1);
  });

  it('passes undefined status for the history tab', async () => {
    mockList.mockResolvedValue({ items: [], totalCount: 0 });

    const { result } = renderHook(() => useAdProposalsQuery({ status: null, page: 2, pageSize: 25 }), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockList).toHaveBeenCalledWith(undefined, undefined, undefined, 2, 25, undefined);
  });

  it('does not fetch a detail when no proposal is selected', () => {
    renderHook(() => useAdProposalQuery(null), { wrapper: createWrapper() });

    expect(mockGet).not.toHaveBeenCalled();
  });

  it('fetches a proposal by numeric API id and maps the nested detail', async () => {
    mockGet.mockResolvedValue({
      proposal: {
        summary: { id: 7, currentVersion: 3 },
        versions: [{ version: 3, reasoning: 'Proč', diffLines: ['řádek'], actionCount: 1 }],
      },
    });

    const { result } = renderHook(() => useAdProposalQuery('7'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGet).toHaveBeenCalledWith(7);
    expect(result.current.data?.currentVersion).toBe(3);
    expect(result.current.data?.reasoning).toBe('Proč');
    expect(result.current.data?.diffLines).toEqual(['řádek']);
  });

  it('fails the detail query when the response carries no proposal', async () => {
    mockGet.mockResolvedValue({});

    const { result } = renderHook(() => useAdProposalQuery('7'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('detail query does not refetch on window focus, so the version cannot change under the reader', async () => {
    mockGet.mockResolvedValue({ proposal: { summary: { id: 7, currentVersion: 3 } } });
    const { result } = renderHook(() => useAdProposalQuery('7'), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    mockGet.mockClear();

    act(() => {
      window.dispatchEvent(new Event('visibilitychange'));
      window.dispatchEvent(new Event('focus'));
    });

    expect(mockGet).not.toHaveBeenCalled();
  });

  it('detail query does not refetch on reconnect, but an explicit refresh refetches it', async () => {
    mockGet.mockResolvedValue({ proposal: { summary: { id: 7, currentVersion: 3 } } });
    const { result } = renderHook(
      () => ({ detail: useAdProposalQuery('7'), refresh: useRefreshAdProposals() }),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.detail.isSuccess).toBe(true));
    mockGet.mockClear();

    act(() => {
      onlineManager.setOnline(false);
      onlineManager.setOnline(true);
    });
    expect(mockGet).not.toHaveBeenCalled();

    act(() => result.current.refresh());
    await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(1));
  });

  it('approves with the proposal id and the given version in the request body', async () => {
    mockApprove.mockResolvedValue({ success: true });
    const { result } = renderHook(() => useApproveAdProposalMutation(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ id: '7', version: 3 });
    });

    expect(mockApprove).toHaveBeenCalledWith(7, expect.objectContaining({ id: 7, version: 3 }));
  });

  it('surfaces a 409 from approve unchanged so the caller can detect a stale version', async () => {
    const conflict = new SwaggerException('Conflict', 409, '', {}, null);
    mockApprove.mockRejectedValue(conflict);
    const { result } = renderHook(() => useApproveAdProposalMutation(), { wrapper: createWrapper() });

    await act(async () => {
      await expect(result.current.mutateAsync({ id: '7', version: 3 })).rejects.toBe(conflict);
    });

    expect(isVersionConflict(result.current.error)).toBe(true);
  });

  it('rejects with version and reason', async () => {
    mockReject.mockResolvedValue({ success: true });
    const { result } = renderHook(() => useRejectAdProposalMutation(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ id: '7', version: 3, reason: 'Mimo strategii' });
    });

    expect(mockReject).toHaveBeenCalledWith(7, expect.objectContaining({ id: 7, version: 3, reason: 'Mimo strategii' }));
  });

  it('reverts by id', async () => {
    mockRevert.mockResolvedValue({ success: true });
    const { result } = renderHook(() => useRevertAdProposalMutation(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ id: '7' });
    });

    expect(mockRevert).toHaveBeenCalledWith(7);
  });
});
