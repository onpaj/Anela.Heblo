import React from 'react';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../../client';
import { useMonthlyStatementsQuery, useCloseMonthMutation } from '../useOvertime';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { overtime: ['overtime'] },
}));

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe('useOvertime', () => {
  afterEach(() => jest.resetAllMocks());

  test('useMonthlyStatementsQuery fetches statements for the month', async () => {
    const mockClient = {
      overtime_GetMonthlyStatements: jest.fn().mockResolvedValue({
        success: true, year: 2026, month: 8, isClosed: false, statements: [],
      }),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);

    const { result } = renderHook(() => useMonthlyStatementsQuery(2026, 8), { wrapper: createWrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockClient.overtime_GetMonthlyStatements).toHaveBeenCalledWith(2026, 8);
  });

  test('useCloseMonthMutation passes force flag', async () => {
    const mockClient = {
      overtime_CloseMonth: jest.fn().mockResolvedValue({ success: true, closedCount: 3 }),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);

    const { result } = renderHook(() => useCloseMonthMutation(), { wrapper: createWrapper });
    await act(async () => {
      await result.current.mutateAsync({ year: 2026, month: 8, force: true });
    });

    expect(mockClient.overtime_CloseMonth).toHaveBeenCalledWith(2026, 8, true);
  });
});
