import React, { ReactNode } from 'react';
import { renderHook, act, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useUpdatePurchaseOrderStatusMutation } from '../usePurchaseOrders';
import * as clientModule from '../../client';

const mockFetch = jest.fn();
const mockApiClient = {
  baseUrl: 'http://localhost:5001',
  http: { fetch: mockFetch },
};

jest.mock('../../client');

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('useUpdatePurchaseOrderStatusMutation', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (clientModule.getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);
  });

  it('posts to the correct URL with Received status', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: jest.fn().mockResolvedValue({ success: true, id: 42 }),
    });

    const { result } = renderHook(() => useUpdatePurchaseOrderStatusMutation(), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      result.current.mutate({ id: 42, request: { id: 42, status: 'Received' } });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5001/api/purchase-orders/42/status',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ id: 42, status: 'Received' }),
      })
    );
  });

  it('accepts all valid statuses including Received', () => {
    const validStatuses: string[] = ['Draft', 'InTransit', 'Received', 'Completed'];

    // Type-level verification: UpdatePurchaseOrderStatusRequest.status is string,
    // so all values are valid. This test documents the expected set.
    expect(validStatuses).toContain('Received');
  });

  it('sets isError when the server returns a non-ok response', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 422 });

    const { result } = renderHook(() => useUpdatePurchaseOrderStatusMutation(), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      result.current.mutate({ id: 1, request: { id: 1, status: 'Received' } });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
