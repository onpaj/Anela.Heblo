import { printLabelPdf } from '../printLabelPdf';
import { getAuthenticatedApiClient } from '../../../api/client';
import type { ShipmentLabelDto } from '../../../api/generated/api-client';

jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const flushAsync = () => new Promise(resolve => setTimeout(resolve, 0));

const labelWithPackage: ShipmentLabelDto = {
  shipmentGuid: 'abc-guid-123',
  packageName: 'Zásilka 1',
  labelUrl: 'https://carrier.example.com/label.pdf',
} as ShipmentLabelDto;

const labelWithoutPackage: ShipmentLabelDto = {
  shipmentGuid: 'abc-guid-123',
} as ShipmentLabelDto;

const expectedProxyUrl =
  'http://api.test/api/packaging/orders/250001/packages/Z%C3%A1silka%201/label.pdf';

let originalOpen: typeof window.open;
let fetchMock: jest.Mock;

beforeEach(() => {
  jest.clearAllMocks();
  URL.createObjectURL = jest.fn().mockReturnValue('blob:test-url');
  URL.revokeObjectURL = jest.fn();
  originalOpen = window.open;
  window.open = jest.fn() as unknown as typeof window.open;

  fetchMock = jest.fn();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    baseUrl: 'http://api.test',
    http: { fetch: fetchMock },
  });
});

afterEach(() => {
  window.open = originalOpen;
});

describe('printLabelPdf', () => {
  it('fetches the backend proxy URL built from orderCode + packageName (same origin)', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    printLabelPdf('250001', labelWithPackage);
    await flushAsync();

    expect(fetchMock).toHaveBeenCalledWith(expectedProxyUrl);
    expect(window.open).not.toHaveBeenCalled();

    jest.restoreAllMocks();
  });

  it('mounts a hidden iframe with the blob URL, calls print on load, defers cleanup', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    const createElementSpy = jest.spyOn(document, 'createElement');
    const appendChildSpy = jest
      .spyOn(document.body, 'appendChild')
      .mockImplementation(() => null as any);
    const removeChildSpy = jest
      .spyOn(document.body, 'removeChild')
      .mockImplementation(() => null as any);
    const setTimeoutSpy = jest.spyOn(window, 'setTimeout');

    printLabelPdf('250001', labelWithPackage);
    await flushAsync();

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;
    expect(iframe.style.display).toBe('none');
    expect(iframe.src).toBe('blob:test-url');
    expect(appendChildSpy).toHaveBeenCalledWith(iframe);

    const printMock = jest.fn();
    Object.defineProperty(iframe, 'contentWindow', {
      value: { print: printMock },
      configurable: true,
    });
    iframe.onload!(new Event('load'));

    expect(printMock).toHaveBeenCalledTimes(1);

    // Cleanup is deferred so the print preview can finish reading the blob URL.
    expect(removeChildSpy).not.toHaveBeenCalled();
    expect(URL.revokeObjectURL).not.toHaveBeenCalled();
    expect(setTimeoutSpy).toHaveBeenCalled();

    // Invoke the scheduled cleanup callback synchronously.
    const cleanupCall = setTimeoutSpy.mock.calls.find(([, delay]) => delay && delay >= 1000);
    expect(cleanupCall).toBeDefined();
    const cleanupFn = cleanupCall![0] as () => void;
    cleanupFn();

    expect(removeChildSpy).toHaveBeenCalledWith(iframe);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test-url');

    jest.restoreAllMocks();
  });

  it('falls back to window.open with the proxy URL when fetch throws', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithPackage);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).toHaveBeenCalledWith(
      expectedProxyUrl,
      '_blank',
      'noopener,noreferrer',
    );

    appendChildSpy.mockRestore();
  });

  it('falls back to window.open when fetch returns non-ok', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404 });
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithPackage);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).toHaveBeenCalledWith(
      expectedProxyUrl,
      '_blank',
      'noopener,noreferrer',
    );

    appendChildSpy.mockRestore();
  });

  it('is a no-op when packageName is missing', async () => {
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithoutPackage);
    await flushAsync();

    expect(fetchMock).not.toHaveBeenCalled();
    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).not.toHaveBeenCalled();

    appendChildSpy.mockRestore();
  });
});
