import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  useRunExpeditionListPrintFix,
} from '../useExpeditionListArchive';
import { ReprintExpeditionListRequest } from '../../generated/api-client';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  getApiBaseUrl: jest.fn(() => 'https://api.example.test'),
  getAuthenticatedFetch: jest.fn(),
  QUERY_KEYS: {
    expeditionListArchive: ['expedition-list-archive'],
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
  return React.createElement(
    QueryClientProvider,
    { client: queryClient },
    children,
  );
};

describe('useExpeditionDates', () => {
  let mockGetDates: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetDates = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionListArchive_GetDates: mockGetDates,
    } as any);
  });

  it('calls expeditionListArchive_GetDates with the given page and pageSize and returns the mapped response', async () => {
    mockGetDates.mockResolvedValue({
      dates: ['2024-12-10', '2024-12-09'],
      totalCount: 2,
      page: 3,
      pageSize: 50,
    });

    const { result } = renderHook(() => useExpeditionDates(3, 50), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetDates).toHaveBeenCalledTimes(1);
    expect(mockGetDates).toHaveBeenCalledWith(3, 50);
    expect(result.current.data).toEqual({
      dates: ['2024-12-10', '2024-12-09'],
      totalCount: 2,
      page: 3,
      pageSize: 50,
    });
  });
});
