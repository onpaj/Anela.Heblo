import { renderHook, waitFor } from '@testing-library/react';
import { useLabelSample } from '../useLabelSample';
import { usePackagesQuery } from '../../../../api/hooks/usePackages';
import { fetchLabelPdf } from '../../printLabelPdf';

jest.mock('../../../../api/hooks/usePackages', () => ({ usePackagesQuery: jest.fn() }));
jest.mock('../../printLabelPdf', () => ({
  fetchLabelPdf: jest.fn(),
  buildProxyUrl: (orderCode: string, packageNumber: number, offsets: object) =>
    `${orderCode}/${packageNumber}?${JSON.stringify(offsets)}`,
}));

const mockPackages = usePackagesQuery as jest.MockedFunction<typeof usePackagesQuery>;
const mockFetch = fetchLabelPdf as jest.MockedFunction<typeof fetchLabelPdf>;

const OFFSET = { xMm: 0, yMm: -5 };

const packagesReturning = (orderCodes: string[]) =>
  mockPackages.mockReturnValue({
    isLoading: false,
    error: null,
    data: {
      items: orderCodes.map((orderCode) => ({ orderCode, packageNumber: '1' })),
      totalCount: orderCodes.length,
      pageNumber: 1,
      pageSize: 5,
    },
  } as any);

const pdfResponse = (status: number) =>
  ({ ok: status === 200, status, blob: async () => new Blob(['%PDF']) }) as Response;

beforeEach(() => {
  mockFetch.mockReset();
  URL.createObjectURL = jest.fn().mockReturnValue('blob:preview');
  URL.revokeObjectURL = jest.fn();
});

describe('useLabelSample', () => {
  it('asks for the carrier\'s most recent packages', () => {
    packagesReturning([]);
    renderHook(() => useLabelSample('PPL', OFFSET));

    expect(mockPackages).toHaveBeenCalledWith(
      expect.objectContaining({ carrier: 'PPL', sortBy: 'PackedAt', sortDescending: true, pageNumber: 1 }),
    );
  });

  it('skips labels the carrier no longer serves and previews the first that loads', async () => {
    packagesReturning(['OLD-1', 'OK-2', 'OK-3']);
    mockFetch.mockResolvedValueOnce(pdfResponse(404)).mockResolvedValueOnce(pdfResponse(200));

    const { result } = renderHook(() => useLabelSample('Zasilkovna', OFFSET));

    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.sample).toEqual({ orderCode: 'OK-2', packageNumber: 1 });
    expect(result.current.previewUrl).toBe('blob:preview');
    expect(mockFetch).toHaveBeenCalledTimes(2);
    // Probes are poll-marked and carry the draft offset for this carrier only.
    expect(mockFetch).toHaveBeenLastCalledWith(`OK-2/1?${JSON.stringify({ Zasilkovna: OFFSET })}`, true);
  });

  it('reports none when no recent label loads', async () => {
    packagesReturning(['OLD-1']);
    mockFetch.mockResolvedValue(pdfResponse(404));

    const { result } = renderHook(() => useLabelSample('GLS', OFFSET));

    await waitFor(() => expect(result.current.status).toBe('none'));
    expect(result.current.sample).toBeNull();
  });

  it('reports an error when the label download fails', async () => {
    packagesReturning(['ORD-1']);
    mockFetch.mockResolvedValue(pdfResponse(503));

    const { result } = renderHook(() => useLabelSample('PPL', OFFSET));

    await waitFor(() => expect(result.current.status).toBe('error'));
  });

  it('re-fetches only the working sample when the offset changes', async () => {
    packagesReturning(['OLD-1', 'OK-2']);
    mockFetch.mockImplementation(async (url: string) => pdfResponse(url.startsWith('OK-2') ? 200 : 404));

    const { result, rerender } = renderHook(({ offset }) => useLabelSample('PPL', offset), {
      initialProps: { offset: OFFSET },
    });
    await waitFor(() => expect(result.current.status).toBe('ready'));
    mockFetch.mockClear();

    rerender({ offset: { xMm: 1, yMm: -5 } });

    await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
    expect(mockFetch).toHaveBeenCalledWith(`OK-2/1?${JSON.stringify({ PPL: { xMm: 1, yMm: -5 } })}`, true);
  });
});
