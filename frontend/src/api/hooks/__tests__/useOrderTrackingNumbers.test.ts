import { renderHook, waitFor } from '@testing-library/react';
import { useOrderTrackingNumbers } from '../useOrderTrackingNumbers';
import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client');

describe('useOrderTrackingNumbers', () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    const mock = createMockApiClient();
    mockFetch = mock.mockFetch;
    mockAuthenticatedApiClient(mock.mockClient);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('returns the per-package tracking numbers from a successful response', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: true, trackingNumbers: ['TR-1', 'TR-2'] }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('126000034', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(['TR-1', 'TR-2']);
  });

  it('returns an empty array when the response has no tracking numbers', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: true, trackingNumbers: null }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('returns an empty array when the response is not successful', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: false, errorCode: 'Exception' }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('returns an empty array when the HTTP response is not ok', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 500 });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('returns an empty array when a network error occurs', async () => {
    mockFetch.mockRejectedValue(new Error('Network failure'));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('does not fetch when disabled', () => {
    const { wrapper } = createQueryClientWrapper();
    renderHook(() => useOrderTrackingNumbers('ORD-1', false), { wrapper });
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
