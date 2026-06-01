import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useImportFromOutlook } from '../useMarketingCalendar';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    marketingCalendar: ['marketing-calendar'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe('useImportFromOutlook', () => {
  let mockImportFromOutlook: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockImportFromOutlook = jest.fn();
    mockGetAuthenticatedApiClient.mockResolvedValue({
      marketingCalendar_ImportFromOutlook: mockImportFromOutlook,
    } as any);
  });

  it('calls marketingCalendar_ImportFromOutlook with correct payload', async () => {
    const mockResponse = { created: 3, skipped: 1, failed: 0 };
    mockImportFromOutlook.mockResolvedValue(mockResponse);

    const { result } = renderHook(() => useImportFromOutlook(), {
      wrapper: createWrapper,
    });

    const fromUtc = new Date('2026-01-01');
    const toUtc = new Date('2026-01-31T23:59:59Z');

    await act(async () => {
      await result.current.mutateAsync({ fromUtc, toUtc, dryRun: false });
    });

    expect(mockImportFromOutlook).toHaveBeenCalledWith({
      fromUtc,
      toUtc,
      dryRun: false,
    });
  });

  it('calls marketingCalendar_ImportFromOutlook with dryRun: true when specified', async () => {
    const mockResponse = { created: 0, skipped: 0, failed: 0 };
    mockImportFromOutlook.mockResolvedValue(mockResponse);

    const { result } = renderHook(() => useImportFromOutlook(), {
      wrapper: createWrapper,
    });

    const fromUtc = new Date('2026-06-01');
    const toUtc = new Date('2026-06-30T23:59:59Z');

    await act(async () => {
      await result.current.mutateAsync({ fromUtc, toUtc, dryRun: true });
    });

    expect(mockImportFromOutlook).toHaveBeenCalledWith({
      fromUtc,
      toUtc,
      dryRun: true,
    });
  });

  it('returns the response from the API', async () => {
    const mockResponse = { created: 5, skipped: 2, failed: 1 };
    mockImportFromOutlook.mockResolvedValue(mockResponse);

    const { result } = renderHook(() => useImportFromOutlook(), {
      wrapper: createWrapper,
    });

    let returnedData: unknown;
    await act(async () => {
      returnedData = await result.current.mutateAsync({
        fromUtc: new Date('2026-01-01'),
        toUtc: new Date('2026-01-31T23:59:59Z'),
      });
    });

    expect(returnedData).toEqual(mockResponse);
  });

  it('invalidates actions and calendar query keys on success', async () => {
    mockImportFromOutlook.mockResolvedValue({ created: 1, skipped: 0, failed: 0 });

    const queryClient = new QueryClient({
      defaultOptions: { mutations: { retry: false } },
    });
    const invalidateSpy = jest.spyOn(queryClient, 'invalidateQueries');

    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);

    const { result } = renderHook(() => useImportFromOutlook(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        fromUtc: new Date('2026-01-01'),
        toUtc: new Date('2026-01-31T23:59:59Z'),
      });
    });

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          queryKey: expect.arrayContaining(['marketing-calendar', 'actions']),
        }),
      );
    });

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          queryKey: expect.arrayContaining(['marketing-calendar', 'calendar']),
        }),
      );
    });
  });
});
