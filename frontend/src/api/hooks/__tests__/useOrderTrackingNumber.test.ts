import { renderHook, waitFor } from '@testing-library/react';
import { useOrderTrackingNumber } from '../useOrderTrackingNumber';
import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client');

describe('useOrderTrackingNumber', () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    const mock = createMockApiClient();
    mockFetch = mock.mockFetch;
    mockAuthenticatedApiClient(mock.mockClient);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('returns the tracking number from a successful response', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: true, trackingNumber: '2421907688' }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('126000034', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBe('2421907688');
  });

  it('returns null when the response has no tracking number', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: true, trackingNumber: null }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('returns null when the response is not successful', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: false, errorCode: 'Exception' }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('returns null when the HTTP response is not ok', async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 500,
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('returns null when a network error occurs', async () => {
    mockFetch.mockRejectedValue(new Error('Network failure'));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('does not fetch when disabled', () => {
    const { wrapper } = createQueryClientWrapper();
    renderHook(() => useOrderTrackingNumber('ORD-1', false), { wrapper });
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
