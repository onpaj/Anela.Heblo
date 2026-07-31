import { renderHook, act } from '@testing-library/react';
import { useOpenManufactureProtocol } from '../useManufactureOrders';
import { getAuthenticatedApiClient } from '../../client';

jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

describe('useOpenManufactureProtocol', () => {
  let mockGetProtocolPdf: jest.Mock;

  beforeEach(() => {
    mockGetProtocolPdf = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      manufactureOrder_GetProtocolPdf: mockGetProtocolPdf,
    } as any);

    URL.createObjectURL = jest.fn().mockReturnValue('blob:mock-url');
    URL.revokeObjectURL = jest.fn();
    window.open = jest.fn();
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.clearAllMocks();
    jest.useRealTimers();
  });

  test('calls with the correct order id', async () => {
    mockGetProtocolPdf.mockResolvedValueOnce({
      data: new Blob(['pdf'], { type: 'application/pdf' }),
      status: 200,
    });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(42);
    });

    expect(mockGetProtocolPdf).toHaveBeenCalledWith(42);
  });

  test('opens the blob URL in a new tab', async () => {
    const mockBlob = new Blob(['pdf'], { type: 'application/pdf' });
    mockGetProtocolPdf.mockResolvedValueOnce({ data: mockBlob, status: 200 });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(42);
    });

    expect(URL.createObjectURL).toHaveBeenCalledWith(mockBlob);
    expect(window.open).toHaveBeenCalledWith('blob:mock-url', '_blank', 'noopener,noreferrer');
  });

  test('schedules URL revocation after 10 seconds', async () => {
    mockGetProtocolPdf.mockResolvedValueOnce({ data: new Blob(['pdf']), status: 200 });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(42);
    });

    expect(URL.revokeObjectURL).not.toHaveBeenCalled();

    act(() => {
      jest.advanceTimersByTime(10000);
    });

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
  });

  test('sets isLoading to true during fetch and false after', async () => {
    let resolveFetch: (v: { data: Blob; status: number }) => void;
    const fetchPromise = new Promise<{ data: Blob; status: number }>((res) => {
      resolveFetch = res;
    });
    mockGetProtocolPdf.mockReturnValueOnce(fetchPromise);

    const { result } = renderHook(() => useOpenManufactureProtocol());

    expect(result.current.isLoading).toBe(false);

    const openPromise = act(async () => {
      await result.current.openProtocol(42);
    });

    resolveFetch!({ data: new Blob(['pdf']), status: 200 });
    await openPromise;

    expect(result.current.isLoading).toBe(false);
  });

  test('sets error when HTTP response is not ok', async () => {
    mockGetProtocolPdf.mockRejectedValueOnce(new Error('An unexpected server error occurred.'));

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(99);
    });

    expect(result.current.error).not.toBeNull();
    expect(result.current.error?.message).toBe('An unexpected server error occurred.');
    expect(window.open).not.toHaveBeenCalled();
  });

  test('sets error when fetch throws', async () => {
    mockGetProtocolPdf.mockRejectedValueOnce(new Error('Network failure'));

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(1);
    });

    expect(result.current.error?.message).toBe('Network failure');
  });
});
