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

describe('useExpeditionListsByDate', () => {
  let mockGetByDate: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetByDate = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionListArchive_GetByDate: mockGetByDate,
    } as any);
  });

  it('passes the date through to the typed method and maps Date/undefined to string/null', async () => {
    mockGetByDate.mockResolvedValue({
      items: [
        {
          blobPath: '2024/12/10/file.pdf',
          fileName: 'expedice-2024-12-10.pdf',
          listId: 'L-1',
          createdOn: new Date('2024-12-10T10:00:00.000Z'),
          contentLength: 1024,
        },
        {
          blobPath: '2024/12/10/other.pdf',
          fileName: 'expedice-other.pdf',
          listId: 'L-2',
          createdOn: undefined,
          contentLength: undefined,
        },
      ],
    });

    const { result } = renderHook(() => useExpeditionListsByDate('2024-12-10'), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetByDate).toHaveBeenCalledWith('2024-12-10');
    expect(result.current.data).toEqual({
      items: [
        {
          blobPath: '2024/12/10/file.pdf',
          fileName: 'expedice-2024-12-10.pdf',
          listId: 'L-1',
          createdOn: '2024-12-10T10:00:00.000Z',
          contentLength: 1024,
        },
        {
          blobPath: '2024/12/10/other.pdf',
          fileName: 'expedice-other.pdf',
          listId: 'L-2',
          createdOn: null,
          contentLength: null,
        },
      ],
    });
  });

  it('does not call the API when date is empty (enabled: !!date)', async () => {
    renderHook(() => useExpeditionListsByDate(''), { wrapper: createWrapper });
    // Give React Query a microtask to evaluate `enabled`
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(mockGetByDate).not.toHaveBeenCalled();
  });
});

describe('useReprintExpeditionList', () => {
  let mockReprint: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockReprint = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionListArchive_Reprint: mockReprint,
    } as any);
  });

  it('instantiates ReprintExpeditionListRequest, calls the typed method, and returns the mapped response', async () => {
    mockReprint.mockResolvedValue({ success: true, errorMessage: null });

    const { result } = renderHook(() => useReprintExpeditionList(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync({ blobPath: '2024/12/10/file.pdf' });

    expect(mockReprint).toHaveBeenCalledTimes(1);
    const calledWith = mockReprint.mock.calls[0][0];
    expect(calledWith).toBeInstanceOf(ReprintExpeditionListRequest);
    expect(calledWith.blobPath).toBe('2024/12/10/file.pdf');
    expect(response).toEqual({ success: true, errorMessage: null });
  });

  it('rethrows SwaggerException-like errors so callers can surface a toast', async () => {
    mockReprint.mockRejectedValue({ status: 500, message: 'Internal Server Error' });

    const { result } = renderHook(() => useReprintExpeditionList(), {
      wrapper: createWrapper,
    });

    await expect(
      result.current.mutateAsync({ blobPath: '2024/12/10/file.pdf' }),
    ).rejects.toMatchObject({ status: 500 });
  });
});
