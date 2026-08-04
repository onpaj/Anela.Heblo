import { renderHook, waitFor } from '@testing-library/react';
import { useOrderTrackingNumbers } from '../useOrderTrackingNumbers';
import { getAuthenticatedApiClient } from '../../client';
import { createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client', () => ({
  ...jest.requireActual('../../client'),
  getAuthenticatedApiClient: jest.fn(),
}));

describe('useOrderTrackingNumbers', () => {
  let mockPackaging_GetOrderTrackingNumbers: jest.Mock;

  beforeEach(() => {
    mockPackaging_GetOrderTrackingNumbers = jest.fn();
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      packaging_GetOrderTrackingNumbers: mockPackaging_GetOrderTrackingNumbers,
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('returns the per-package tracking numbers from a successful response', async () => {
    mockPackaging_GetOrderTrackingNumbers.mockResolvedValue({
      success: true,
      trackingNumbers: ['TR-1', 'TR-2'],
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('126000034', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(['TR-1', 'TR-2']);
    expect(getAuthenticatedApiClient).toHaveBeenCalledWith(false);
    expect(mockPackaging_GetOrderTrackingNumbers).toHaveBeenCalledWith('126000034');
  });

  it('returns an empty array when the response has no tracking numbers', async () => {
    mockPackaging_GetOrderTrackingNumbers.mockResolvedValue({ success: true, trackingNumbers: null });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('returns an empty array when the response is not successful', async () => {
    mockPackaging_GetOrderTrackingNumbers.mockResolvedValue({ success: false, errorCode: 'Exception' });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('returns an empty array when the underlying request throws (e.g. non-2xx response)', async () => {
    mockPackaging_GetOrderTrackingNumbers.mockRejectedValue(new Error('HTTP 500'));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('returns an empty array when a network error occurs', async () => {
    mockPackaging_GetOrderTrackingNumbers.mockRejectedValue(new Error('Network failure'));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumbers('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('does not fetch when disabled', () => {
    const { wrapper } = createQueryClientWrapper();
    renderHook(() => useOrderTrackingNumbers('ORD-1', false), { wrapper });
    expect(mockPackaging_GetOrderTrackingNumbers).not.toHaveBeenCalled();
  });
});
