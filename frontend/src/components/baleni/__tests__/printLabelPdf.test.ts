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
      value: { print: printMock, addEventListener: jest.fn(), removeEventListener: jest.fn() },
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

  it('does not invoke onAfterPrint when packageName is missing (no-op)', async () => {
    const onAfterPrint = jest.fn();

    printLabelPdf('250001', labelWithoutPackage, onAfterPrint);
    await flushAsync();

    expect(onAfterPrint).not.toHaveBeenCalled();
  });

  it('registers an afterprint listener and invokes onAfterPrint exactly once on afterprint', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    const createElementSpy = jest.spyOn(document, 'createElement');
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    const onAfterPrint = jest.fn();
    printLabelPdf('250001', labelWithPackage, onAfterPrint);
    await flushAsync();

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;

    const printMock = jest.fn();
    const addEventListener = jest.fn();
    const removeEventListener = jest.fn();
    Object.defineProperty(iframe, 'contentWindow', {
      value: { print: printMock, addEventListener, removeEventListener },
      configurable: true,
    });

    iframe.onload!(new Event('load'));

    // The afterprint listener should be registered BEFORE print() is called.
    expect(addEventListener).toHaveBeenCalledTimes(1);
    expect(addEventListener.mock.calls[0][0]).toBe('afterprint');
    expect(printMock).toHaveBeenCalledTimes(1);
    expect(addEventListener.mock.invocationCallOrder[0])
      .toBeLessThan(printMock.mock.invocationCallOrder[0]);

    // onAfterPrint should not fire until the browser triggers afterprint.
    expect(onAfterPrint).not.toHaveBeenCalled();

    // Trigger the captured handler — should fire callback and self-remove.
    const handler = addEventListener.mock.calls[0][1] as () => void;
    handler();

    expect(onAfterPrint).toHaveBeenCalledTimes(1);
    expect(removeEventListener).toHaveBeenCalledWith('afterprint', handler);

    jest.restoreAllMocks();
  });

  it('invokes onAfterPrint immediately when fetch fails (fallback path)', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const onAfterPrint = jest.fn();

    printLabelPdf('250001', labelWithPackage, onAfterPrint);
    await flushAsync();

    expect(window.open).toHaveBeenCalledWith(
      expectedProxyUrl,
      '_blank',
      'noopener,noreferrer',
    );
    expect(onAfterPrint).toHaveBeenCalledTimes(1);
  });

  it('invokes onAfterPrint immediately when fetch returns non-ok (fallback path)', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404 });
    const onAfterPrint = jest.fn();

    printLabelPdf('250001', labelWithPackage, onAfterPrint);
    await flushAsync();

    expect(window.open).toHaveBeenCalledWith(
      expectedProxyUrl,
      '_blank',
      'noopener,noreferrer',
    );
    expect(onAfterPrint).toHaveBeenCalledTimes(1);
  });

  it('invokes onAfterPrint via the 60s safety net when afterprint never fires, and only once even if afterprint fires later', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    const createElementSpy = jest.spyOn(document, 'createElement');
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);
    jest.spyOn(document.body, 'removeChild').mockImplementation(() => null as any);
    const setTimeoutSpy = jest.spyOn(window, 'setTimeout');

    const onAfterPrint = jest.fn();
    printLabelPdf('250001', labelWithPackage, onAfterPrint);
    await flushAsync();

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;

    const printMock = jest.fn();
    const addEventListener = jest.fn();
    const removeEventListener = jest.fn();
    Object.defineProperty(iframe, 'contentWindow', {
      value: { print: printMock, addEventListener, removeEventListener },
      configurable: true,
    });

    iframe.onload!(new Event('load'));

    // print() was called but the browser never fires afterprint.
    expect(printMock).toHaveBeenCalledTimes(1);
    expect(onAfterPrint).not.toHaveBeenCalled();

    // Fire the 60s cleanup callback — safety net should invoke onAfterPrint.
    const cleanupCall = setTimeoutSpy.mock.calls.find(([, delay]) => delay && delay >= 1000);
    expect(cleanupCall).toBeDefined();
    const cleanupFn = cleanupCall![0] as () => void;
    cleanupFn();

    expect(onAfterPrint).toHaveBeenCalledTimes(1);

    // If afterprint fires LATER (after the safety net already fired), do not double-invoke.
    const handler = addEventListener.mock.calls[0][1] as () => void;
    handler();

    expect(onAfterPrint).toHaveBeenCalledTimes(1);

    jest.restoreAllMocks();
  });

  it('invokes onAfterPrint when window regains focus after print() (fast path)', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    const createElementSpy = jest.spyOn(document, 'createElement');
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    const onAfterPrint = jest.fn();
    printLabelPdf('250001', labelWithPackage, onAfterPrint);
    await flushAsync();

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;
    Object.defineProperty(iframe, 'contentWindow', {
      value: { print: jest.fn(), addEventListener: jest.fn(), removeEventListener: jest.fn() },
      configurable: true,
    });

    iframe.onload!(new Event('load'));

    expect(onAfterPrint).not.toHaveBeenCalled();

    // Browser closes the print dialog → focus returns to the main window.
    window.dispatchEvent(new Event('focus'));

    expect(onAfterPrint).toHaveBeenCalledTimes(1);

    // Re-firing focus (e.g., user clicks back into the page) must not double-call.
    window.dispatchEvent(new Event('focus'));
    expect(onAfterPrint).toHaveBeenCalledTimes(1);

    jest.restoreAllMocks();
  });

  it('falls back to window.open and fires onAfterPrint when post-fetch processing throws', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      blob: async () => {
        throw new Error('blob extraction failed');
      },
    });
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');
    const onAfterPrint = jest.fn();

    printLabelPdf('250001', labelWithPackage, onAfterPrint);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).toHaveBeenCalledWith(
      expectedProxyUrl,
      '_blank',
      'noopener,noreferrer',
    );
    expect(onAfterPrint).toHaveBeenCalledTimes(1);

    appendChildSpy.mockRestore();
  });
});
