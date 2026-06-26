import {
  printLabelPdf,
  fetchAndPrintLabel,
  printLabelWithReadiness,
  READINESS_BACKOFF_MS,
  READINESS_TIMEOUT_MS,
} from '../printLabelPdf';
import type { PrintLabelOptions } from '../printLabelPdf';
import { getAuthenticatedApiClient } from '../../../api/client';

jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const flushAsync = () => new Promise(resolve => setTimeout(resolve, 0));

const labelWithPackage: PrintLabelOptions = {
  packageNumber: 1,
};

const labelWithoutPackage: PrintLabelOptions = {};

const expectedProxyUrl =
  'http://api.test/api/packaging/orders/250001/packages/1/label.pdf';

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
  it('fetches the backend proxy URL built from orderCode + packageNumber (same origin)', async () => {
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

  it('does NOT navigate to the proxy URL when fetch throws (no unauthenticated 401 tab)', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');
    const onError = jest.fn();

    printLabelPdf('250001', labelWithPackage, undefined, onError);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledWith(undefined);

    appendChildSpy.mockRestore();
  });

  it('reports the status via onError when fetch returns a non-retryable error (no window.open)', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 500 });
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');
    const onError = jest.fn();

    printLabelPdf('250001', labelWithPackage, undefined, onError);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledWith(500);

    appendChildSpy.mockRestore();
  });

  it('is a no-op when packageNumber is missing', async () => {
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');

    printLabelPdf('250001', labelWithoutPackage);
    await flushAsync();

    expect(fetchMock).not.toHaveBeenCalled();
    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).not.toHaveBeenCalled();

    appendChildSpy.mockRestore();
  });

  it('does not invoke onAfterPrint when packageNumber is missing (no-op)', async () => {
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

  it('invokes onError (not onAfterPrint) when fetch fails', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const onAfterPrint = jest.fn();
    const onError = jest.fn();

    printLabelPdf('250001', labelWithPackage, onAfterPrint, onError);
    await flushAsync();

    expect(window.open).not.toHaveBeenCalled();
    expect(onAfterPrint).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledWith(undefined);
  });

  it('invokes onError with the status (not onAfterPrint) when fetch returns a non-retryable error', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 500 });
    const onAfterPrint = jest.fn();
    const onError = jest.fn();

    printLabelPdf('250001', labelWithPackage, onAfterPrint, onError);
    await flushAsync();

    expect(window.open).not.toHaveBeenCalled();
    expect(onAfterPrint).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledWith(500);
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

  it('invokes onError with the status when post-fetch processing throws (no window.open)', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      status: 200,
      blob: async () => {
        throw new Error('blob extraction failed');
      },
    });
    const appendChildSpy = jest.spyOn(document.body, 'appendChild');
    const onAfterPrint = jest.fn();
    const onError = jest.fn();

    printLabelPdf('250001', labelWithPackage, onAfterPrint, onError);
    await flushAsync();

    expect(appendChildSpy).not.toHaveBeenCalled();
    expect(window.open).not.toHaveBeenCalled();
    expect(onAfterPrint).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledWith(200);

    appendChildSpy.mockRestore();
  });

  it('retries on 404 then calls onError with undefined once READINESS_TIMEOUT_MS is exceeded', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404 });
    jest.useFakeTimers();
    const onError = jest.fn();

    printLabelPdf('250001', labelWithPackage, undefined, onError);

    // Pump the retry loop past the timeout by alternating microtask flushes and timer advances
    for (let i = 0; i < 15; i++) {
      await Promise.resolve();
      jest.advanceTimersByTime(READINESS_BACKOFF_MS[READINESS_BACKOFF_MS.length - 1] + 1);
    }
    await Promise.resolve();

    expect(onError).toHaveBeenCalledWith(undefined);

    jest.useRealTimers();
  });
});

describe('fetchAndPrintLabel', () => {
  it('returns printed: true and wires onAfterPrint when fetch succeeds', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      status: 200,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    const onAfterPrint = jest.fn();
    const result = await fetchAndPrintLabel('250001', 1, onAfterPrint);

    expect(result).toEqual({ printed: true, status: 200 });
    expect(fetchMock).toHaveBeenCalledWith(
      'http://api.test/api/packaging/orders/250001/packages/1/label.pdf',
    );

    jest.restoreAllMocks();
  });

  it('returns printed: false with status when fetch returns non-ok', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404 });

    const result = await fetchAndPrintLabel('250001', 2);

    expect(result).toEqual({ printed: false, status: 404 });
  });

  it('returns printed: false without status when fetch throws', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await fetchAndPrintLabel('250001', 1);

    expect(result).toEqual({ printed: false });
  });

  it('builds the proxy URL from orderCode and packageNumber', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    await fetchAndPrintLabel('ORD/2025/99', 3);

    expect(fetchMock).toHaveBeenCalledWith(
      'http://api.test/api/packaging/orders/ORD%2F2025%2F99/packages/3/label.pdf',
    );

    jest.restoreAllMocks();
  });
});

describe('printLabelWithReadiness', () => {
  /**
   * Pump the event loop one "retry step":
   *  - flush microtasks (the fetch promise chain resolves)
   *  - advance fake timers by `delayMs` (fires the backoff setTimeout)
   *  - flush microtasks again (the await-on-timer continuation runs)
   */
  const pumpStep = async (delayMs: number) => {
    for (let i = 0; i < 5; i++) await Promise.resolve();
    jest.advanceTimersByTime(delayMs);
    for (let i = 0; i < 5; i++) await Promise.resolve();
  };

  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
    jest.restoreAllMocks();
  });

  it('returns printed: true immediately when label is ready on the first attempt', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      status: 200,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    const result = await printLabelWithReadiness('250001', 1);

    expect(result).toEqual({ printed: true, status: 200 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('retries on 404 and returns printed: true when label becomes ready on second attempt', async () => {
    fetchMock
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValue({
        ok: true,
        status: 200,
        blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
      });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    const onWaiting = jest.fn();
    const promise = printLabelWithReadiness('250001', 1, { onWaiting });

    await pumpStep(READINESS_BACKOFF_MS[0] + 1);

    const result = await promise;

    expect(result).toEqual({ printed: true, status: 200 });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(onWaiting).toHaveBeenCalledWith(0);
  });

  it('calls onWaiting with increasing attempt indices on each 404', async () => {
    fetchMock
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValue({
        ok: true,
        blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
      });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    const onWaiting = jest.fn();
    const promise = printLabelWithReadiness('250001', 1, { onWaiting });

    // 3 retry steps, then the 4th attempt succeeds
    for (let i = 0; i < 3; i++) {
      await pumpStep(READINESS_BACKOFF_MS[Math.min(i, READINESS_BACKOFF_MS.length - 1)] + 1);
    }
    await promise;

    expect(onWaiting).toHaveBeenCalledTimes(3);
    expect(onWaiting).toHaveBeenNthCalledWith(1, 0);
    expect(onWaiting).toHaveBeenNthCalledWith(2, 1);
    expect(onWaiting).toHaveBeenNthCalledWith(3, 2);
  });

  it('uses the last backoff value (10000ms) once attempts exceed READINESS_BACKOFF_MS length', async () => {
    // 6 failures then success
    fetchMock
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValueOnce({ ok: false, status: 404 })
      .mockResolvedValue({
        ok: true,
        blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
      });
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    const setTimeoutSpy = jest.spyOn(global, 'setTimeout');
    const promise = printLabelWithReadiness('250001', 1);

    for (let i = 0; i < 6; i++) {
      await pumpStep(READINESS_BACKOFF_MS[Math.min(i, READINESS_BACKOFF_MS.length - 1)] + 1);
    }
    await promise;

    // Backoff delays used (only the ones >= 1000ms to exclude the iframe 60s timer)
    const backoffDelays = setTimeoutSpy.mock.calls
      .map(([, delay]) => delay)
      .filter((d) => typeof d === 'number' && d >= 1000 && d < 60_000);
    // 5th and 6th backoffs (indices 4 and 5) should both be the max value
    expect(backoffDelays[4]).toBe(READINESS_BACKOFF_MS[READINESS_BACKOFF_MS.length - 1]);
    expect(backoffDelays[5]).toBe(READINESS_BACKOFF_MS[READINESS_BACKOFF_MS.length - 1]);
  });

  it('returns timedOut: true when READINESS_TIMEOUT_MS is exceeded', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404 });

    const promise = printLabelWithReadiness('250001', 1);

    // Pump enough steps to exceed 30s total
    for (let i = 0; i < 10; i++) {
      await pumpStep(READINESS_BACKOFF_MS[Math.min(i, READINESS_BACKOFF_MS.length - 1)] + 1);
    }

    const result = await promise;

    expect(result).toEqual({ printed: false, timedOut: true });
  });

  it('returns printed: false (no timedOut) when aborted via AbortSignal before first attempt', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404 });

    const controller = new AbortController();
    controller.abort();

    const result = await printLabelWithReadiness('250001', 1, { signal: controller.signal });

    expect(result).toEqual({ printed: false });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('returns printed: false when aborted during backoff wait', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404 });

    const controller = new AbortController();
    const onWaiting = jest.fn().mockImplementation(() => {
      controller.abort();
    });

    const promise = printLabelWithReadiness('250001', 1, {
      signal: controller.signal,
      onWaiting,
    });

    // First 404 → onWaiting fires → abort → backoff timer fires → abort check returns
    await pumpStep(READINESS_BACKOFF_MS[0] + 1);

    const result = await promise;

    expect(result).toEqual({ printed: false });
    expect(onWaiting).toHaveBeenCalledTimes(1);
  });

  it('does not retry on non-404 status and returns printed: false with status', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 503 });

    const onWaiting = jest.fn();
    const result = await printLabelWithReadiness('250001', 1, { onWaiting });

    expect(result).toEqual({ printed: false, status: 503 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(onWaiting).not.toHaveBeenCalled();
  });

  it('does not retry on network error (no status) and returns printed: false', async () => {
    fetchMock.mockRejectedValue(new TypeError('Network error'));

    const onWaiting = jest.fn();
    const result = await printLabelWithReadiness('250001', 1, { onWaiting });

    expect(result).toEqual({ printed: false });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(onWaiting).not.toHaveBeenCalled();
  });

  it('passes onAfterPrint through to silentPrintViaBlob on successful attempt', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      status: 200,
      blob: async () => new Blob(['%PDF'], { type: 'application/pdf' }),
    });
    const createElementSpy = jest.spyOn(document, 'createElement');
    jest.spyOn(document.body, 'appendChild').mockImplementation(() => null as any);

    const onAfterPrint = jest.fn();
    await printLabelWithReadiness('250001', 1, { onAfterPrint });

    const iframe = createElementSpy.mock.results[createElementSpy.mock.results.length - 1]
      .value as HTMLIFrameElement;

    const addEventListener = jest.fn();
    Object.defineProperty(iframe, 'contentWindow', {
      value: { print: jest.fn(), addEventListener, removeEventListener: jest.fn() },
      configurable: true,
    });

    iframe.onload!(new Event('load'));

    const handler = addEventListener.mock.calls[0][1] as () => void;
    handler();

    expect(onAfterPrint).toHaveBeenCalledTimes(1);
  });
});
