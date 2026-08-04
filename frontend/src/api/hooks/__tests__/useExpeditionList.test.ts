import { renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useRunExpeditionListPrintFix, usePrintExpeditionOrder } from '../useExpeditionList';
import { PrintExpeditionOrderRequest } from '../../generated/api-client';
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
  let mockRunFix: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockRunFix = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionList_RunFix: mockRunFix,
    } as any);
  });

  it('calls expeditionList_RunFix with no arguments and returns the mapped totalCount', async () => {
    mockRunFix.mockResolvedValue({ totalCount: 7, skippedCount: 1 });

    const { result } = renderHook(() => useRunExpeditionListPrintFix(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync();

    expect(mockRunFix).toHaveBeenCalledTimes(1);
    expect(mockRunFix).toHaveBeenCalledWith();
    expect(response).toEqual({ totalCount: 7 });
  });

  it('defaults totalCount to 0 when the response omits it', async () => {
    mockRunFix.mockResolvedValue({});

    const { result } = renderHook(() => useRunExpeditionListPrintFix(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync();

    expect(response).toEqual({ totalCount: 0 });
  });

  it('rejects when the typed client call throws', async () => {
    mockRunFix.mockRejectedValue({ status: 500, message: 'Internal Server Error' });

    const { result } = renderHook(() => useRunExpeditionListPrintFix(), {
      wrapper: createWrapper,
    });

    await expect(result.current.mutateAsync()).rejects.toMatchObject({ status: 500 });
  });
});

describe('usePrintExpeditionOrder', () => {
  let mockPrintOrder: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockPrintOrder = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionList_PrintOrder: mockPrintOrder,
    } as any);
  });

  it('instantiates PrintExpeditionOrderRequest, calls the typed method, and returns the mapped response', async () => {
    mockPrintOrder.mockResolvedValue({ success: true, errorCode: undefined, params: undefined });

    const { result } = renderHook(() => usePrintExpeditionOrder(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync({ orderCode: '0001234' });

    expect(mockPrintOrder).toHaveBeenCalledTimes(1);
    const calledWith = mockPrintOrder.mock.calls[0][0];
    expect(calledWith).toBeInstanceOf(PrintExpeditionOrderRequest);
    expect(calledWith.orderCode).toBe('0001234');
    expect(response).toEqual({ success: true, errorCode: undefined, params: undefined });
  });

  it('resolves with a business failure (success: false) instead of throwing', async () => {
    mockPrintOrder.mockResolvedValue({
      success: false,
      errorCode: 'OrderNotFound',
      params: { orderCode: '0001234' },
    });

    const { result } = renderHook(() => usePrintExpeditionOrder(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync({ orderCode: '0001234' });

    expect(response).toEqual({
      success: false,
      errorCode: 'OrderNotFound',
      params: { orderCode: '0001234' },
    });
  });
});
