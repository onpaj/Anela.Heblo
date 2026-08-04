import { renderHook, act } from '@testing-library/react';
import { useOpenManufactureProtocol } from '../useManufactureOrders';
import { getAuthenticatedApiClient } from '../../client';

jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

// btoa('pdf') — matches the base64 pdfBytes shape returned by the generated
// GetManufactureProtocolResponse (pdfBytes?: string; fileName?: string), not FileResponse.
const MOCK_PDF_BYTES_BASE64 = 'cGRm';

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
      pdfBytes: MOCK_PDF_BYTES_BASE64,
      fileName: 'protocol.pdf',
    });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(42);
    });

    expect(mockGetProtocolPdf).toHaveBeenCalledWith(42);
  });

  test('opens the blob URL in a new tab', async () => {
    mockGetProtocolPdf.mockResolvedValueOnce({
      pdfBytes: MOCK_PDF_BYTES_BASE64,
      fileName: 'protocol.pdf',
    });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(42);
    });

    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
    const createdBlob = (URL.createObjectURL as jest.Mock).mock.calls[0][0];
    expect(createdBlob).toBeInstanceOf(Blob);
    expect(createdBlob.type).toBe('application/pdf');
    expect(window.open).toHaveBeenCalledWith('blob:mock-url', '_blank', 'noopener,noreferrer');
  });

  test('schedules URL revocation after 10 seconds', async () => {
    mockGetProtocolPdf.mockResolvedValueOnce({
      pdfBytes: MOCK_PDF_BYTES_BASE64,
      fileName: 'protocol.pdf',
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
    let resolveFetch: (v: { pdfBytes: string; fileName: string }) => void;
    const fetchPromise = new Promise<{ pdfBytes: string; fileName: string }>((res) => {
      resolveFetch = res;
    });
    mockGetProtocolPdf.mockReturnValueOnce(fetchPromise);

    const { result } = renderHook(() => useOpenManufactureProtocol());

    expect(result.current.isLoading).toBe(false);

    const openPromise = act(async () => {
      await result.current.openProtocol(42);
    });

    resolveFetch!({ pdfBytes: MOCK_PDF_BYTES_BASE64, fileName: 'protocol.pdf' });
    await openPromise;

    expect(result.current.isLoading).toBe(false);
  });

  test('sets error when response has no pdfBytes', async () => {
    mockGetProtocolPdf.mockResolvedValueOnce({ fileName: 'protocol.pdf' });

    const { result } = renderHook(() => useOpenManufactureProtocol());

    await act(async () => {
      await result.current.openProtocol(99);
    });

    expect(result.current.error).not.toBeNull();
    expect(result.current.error?.message).toBe(
      'Manufacture protocol PDF response did not include pdfBytes',
    );
    expect(window.open).not.toHaveBeenCalled();
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
