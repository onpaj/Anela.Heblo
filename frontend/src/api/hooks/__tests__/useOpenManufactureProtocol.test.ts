import { renderHook, act } from '@testing-library/react';
import { useOpenManufactureProtocol } from '../useManufactureOrders';
import { getAuthenticatedApiClient } from '../../client';

jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

describe('useOpenManufactureProtocol', () => {
  let mockFetch: jest.Mock;
  let mockApiClient: any;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockApiClient = {
      baseUrl: 'http://localhost:5001',
      http: { fetch: mockFetch },
    };
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);

    URL.createObjectURL = jest.fn().mockReturnValue('blob:mock-url');
    URL.revokeObjectURL = jest.fn();
    window.open = jest.fn();
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.clearAllMocks();
    jest.useRealTimers();
  });

  test('calls the correct URL', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      blob: jest.fn().mockResolvedValueOnce(new Blob(['pdf'], { type: 'application/pdf' })),
    });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(42);
    });

    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5001/api/manufactureorder/42/protocol.pdf',
      { method: 'GET' }
    );
  });

  test('opens the blob URL in a new tab', async () => {
    const mockBlob = new Blob(['pdf'], { type: 'application/pdf' });
    mockFetch.mockResolvedValueOnce({
      ok: true,
      blob: jest.fn().mockResolvedValueOnce(mockBlob),
    });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(42);
    });

    expect(URL.createObjectURL).toHaveBeenCalledWith(mockBlob);
    expect(window.open).toHaveBeenCalledWith('blob:mock-url', '_blank', 'noopener,noreferrer');
  });

  test('schedules URL revocation after 10 seconds', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      blob: jest.fn().mockResolvedValueOnce(new Blob(['pdf'])),
    });

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
    let resolveBlob: (b: Blob) => void;
    const blobPromise = new Promise<Blob>((res) => { resolveBlob = res; });
    mockFetch.mockResolvedValueOnce({
      ok: true,
      blob: jest.fn().mockReturnValueOnce(blobPromise),
    });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    expect(result.current.isLoading).toBe(false);

    const openPromise = act(async () => {
      await result.current.openProtocol(42);
    });

    resolveBlob!(new Blob(['pdf']));
    await openPromise;

    expect(result.current.isLoading).toBe(false);
  });

  test('sets error when HTTP response is not ok', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 404 });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(99);
    });

    expect(result.current.error).not.toBeNull();
    expect(result.current.error?.message).toBe('HTTP error! status: 404');
    expect(window.open).not.toHaveBeenCalled();
  });

  test('sets error when fetch throws', async () => {
    mockFetch.mockRejectedValueOnce(new Error('Network failure'));

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(1);
    });

    expect(result.current.error?.message).toBe('Network failure');
  });
});
