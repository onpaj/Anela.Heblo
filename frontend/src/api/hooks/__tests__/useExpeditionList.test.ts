import { renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useRunExpeditionListPrintFix } from '../useExpeditionList';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
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

describe('useRunExpeditionListPrintFix', () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      baseUrl: 'https://api.example.test',
      http: { fetch: mockFetch },
    } as any);
  });

  it('POSTs to /api/expedition-list/run-fix and returns the parsed JSON response', async () => {
    const responseBody = { totalCount: 7 };
    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(responseBody),
    });

    const { result } = renderHook(() => useRunExpeditionListPrintFix(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync();

    expect(mockFetch).toHaveBeenCalledTimes(1);
    expect(mockFetch).toHaveBeenCalledWith(
      'https://api.example.test/api/expedition-list/run-fix',
      { method: 'POST', headers: { 'Content-Type': 'application/json' } },
    );
    expect(response).toEqual(responseBody);
  });

  it('throws an error with the backend errorMessage when the response is not ok', async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 500,
      json: jest.fn().mockResolvedValue({ errorMessage: 'Internal Server Error' }),
    });

    const { result } = renderHook(() => useRunExpeditionListPrintFix(), {
      wrapper: createWrapper,
    });

    await expect(result.current.mutateAsync()).rejects.toThrow('Internal Server Error');
  });

  it('falls back to a generic HTTP error message when the error body is unparseable', async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 503,
      json: jest.fn().mockRejectedValue(new SyntaxError('Unexpected token')),
    });

    const { result } = renderHook(() => useRunExpeditionListPrintFix(), {
      wrapper: createWrapper,
    });

    await expect(result.current.mutateAsync()).rejects.toThrow('HTTP error! status: 503');
  });
});
