import { renderHook, waitFor } from '@testing-library/react';
import { useOrderTrackingNumber } from '../useOrderTrackingNumber';
import { getAuthenticatedApiClient } from '../../client';
import { createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client', () => ({
  ...jest.requireActual('../../client'),
  getAuthenticatedApiClient: jest.fn(),
}));

describe('useOrderTrackingNumber', () => {
  let mockPackaging_GetOrderTrackingNumber: jest.Mock;

  beforeEach(() => {
    mockPackaging_GetOrderTrackingNumber = jest.fn();
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      packaging_GetOrderTrackingNumber: mockPackaging_GetOrderTrackingNumber,
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('returns the tracking number from a successful response', async () => {
    mockPackaging_GetOrderTrackingNumber.mockResolvedValue({
      success: true,
      trackingNumber: '2421907688',
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('126000034', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBe('2421907688');
    expect(getAuthenticatedApiClient).toHaveBeenCalledWith(false);
    expect(mockPackaging_GetOrderTrackingNumber).toHaveBeenCalledWith('126000034');
  });

  it('returns null when the response has no tracking number', async () => {
    mockPackaging_GetOrderTrackingNumber.mockResolvedValue({ success: true, trackingNumber: null });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('returns null when the response is not successful', async () => {
    mockPackaging_GetOrderTrackingNumber.mockResolvedValue({ success: false, errorCode: 'Exception' });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('returns null when the underlying request throws (e.g. non-2xx response)', async () => {
    mockPackaging_GetOrderTrackingNumber.mockRejectedValue(new Error('HTTP 500'));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('returns null when a network error occurs', async () => {
    mockPackaging_GetOrderTrackingNumber.mockRejectedValue(new Error('Network failure'));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('does not fetch when disabled', () => {
    const { wrapper } = createQueryClientWrapper();
    renderHook(() => useOrderTrackingNumber('ORD-1', false), { wrapper });
    expect(mockPackaging_GetOrderTrackingNumber).not.toHaveBeenCalled();
  });
});
