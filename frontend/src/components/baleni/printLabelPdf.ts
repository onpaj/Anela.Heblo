import { getAuthenticatedApiClient } from '../../api/client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const buildProxyUrl = (orderCode: string, packageNumber: number): string => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  return `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/packages/${packageNumber}/label.pdf`;
};

export interface PrintAttemptResult {
  /** True once the label PDF was fetched and handed to the print iframe. */
  printed: boolean;
  /** HTTP status of the label fetch when it completed (absent on network errors). */
  status?: number;
}

const silentPrintViaBlob = async (url: string, onAfterPrint?: () => void): Promise<PrintAttemptResult> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  let response: Response;
  try {
    response = await apiClient.http.fetch(url);
  } catch {
    return { printed: false };
  }
  if (!response.ok) return { printed: false, status: response.status };

  try {
    const blob = await response.blob();
    const blobUrl = URL.createObjectURL(blob);
    const iframe = document.createElement('iframe');
    iframe.style.display = 'none';
    iframe.src = blobUrl;
    let called = false;
    iframe.onload = () => {
      const win = iframe.contentWindow;
      const fire = () => {
        if (called) return;
        called = true;
        win?.removeEventListener('afterprint', afterprintHandler);
        window.removeEventListener('focus', focusHandler);
        onAfterPrint?.();
      };
      // Primary signal: focus returns to the main window once the print dialog closes
      // (the scanner input refocuses, which fires window 'focus'). This is the most
      // reliable cross-browser cue. `afterprint` is kept as a backup since some
      // browsers/drivers don't fire it for iframe-printed PDFs.
      const afterprintHandler = () => fire();
      const focusHandler = () => fire();
      win?.addEventListener('afterprint', afterprintHandler);
      window.addEventListener('focus', focusHandler);
      win?.print();
      // Keep iframe attached until print dialog closes; revoke blob URL after a delay
      // so the browser can still resolve it while the print preview is open. The 60s
      // timeout doubles as a last-resort safety net for `onAfterPrint`.
      setTimeout(() => {
        document.body.removeChild(iframe);
        URL.revokeObjectURL(blobUrl);
        fire();
      }, 60_000);
    };
    document.body.appendChild(iframe);
    return { printed: true, status: response.status };
  } catch {
    return { printed: false, status: response.status };
  }
};

export const fetchAndPrintLabel = (
  orderCode: string,
  packageNumber: number,
  onAfterPrint?: () => void,
): Promise<PrintAttemptResult> => {
  const url = buildProxyUrl(orderCode, packageNumber);
  return silentPrintViaBlob(url, onAfterPrint);
};

export interface ReadinessWaitOptions {
  /** Called on each not-ready (404) attempt, before the backoff wait. 0-based attempt index. */
  onWaiting?: (attempt: number) => void;
  /** Called by the underlying print iframe when the print dialog closes (success path only). */
  onAfterPrint?: () => void;
  /** Cancel in-flight waits. On abort, returns { printed: false } immediately. */
  signal?: AbortSignal;
}

export interface PrintWithReadinessResult {
  printed: boolean;
  status?: number;
  timedOut?: boolean;
}

export const READINESS_BACKOFF_MS = [1000, 2000, 3000, 5000, 10000];
export const READINESS_TIMEOUT_MS = 30_000;

export const printLabelWithReadiness = async (
  orderCode: string,
  packageNumber: number,
  options: ReadinessWaitOptions = {},
): Promise<PrintWithReadinessResult> => {
  const { onWaiting, onAfterPrint, signal } = options;
  const startedAt = Date.now();
  let attempt = 0;

  while (true) {
    if (signal?.aborted) return { printed: false };

    const result = await fetchAndPrintLabel(orderCode, packageNumber, onAfterPrint);

    if (result.printed) return { printed: true, status: result.status };

    if (result.status !== 404) return { printed: false, status: result.status };

    // Label not ready (404). Notify caller, then wait with exponential backoff.
    onWaiting?.(attempt);

    const delayMs = READINESS_BACKOFF_MS[Math.min(attempt, READINESS_BACKOFF_MS.length - 1)];
    await new Promise<void>((resolve) => setTimeout(resolve, delayMs));

    if (signal?.aborted) return { printed: false };

    const elapsed = Date.now() - startedAt;
    if (elapsed >= READINESS_TIMEOUT_MS) return { printed: false, timedOut: true };

    attempt++;
  }
};

export interface PrintLabelOptions {
  /** 1-based position of the package within the order's shipment labels. */
  packageNumber?: number;
}

export const printLabelPdf = (
  orderCode: string,
  label: PrintLabelOptions,
  onAfterPrint?: () => void,
  onError?: (status?: number) => void,
): void => {
  if (!label.packageNumber || !Number.isFinite(label.packageNumber)) return;

  void printLabelWithReadiness(orderCode, label.packageNumber, {
    onAfterPrint,
  }).then((result) => {
    if (!result.printed) {
      // The label could not be fetched or became permanently unavailable.
      // Do NOT fall back to window.open(proxyUrl): a bare navigation can't carry the
      // bearer token, so it would 401 and mask the real reason. Surface the status
      // instead, and don't fire onAfterPrint (which callers treat as success).
      onError?.(result.status);
    }
  });
};
