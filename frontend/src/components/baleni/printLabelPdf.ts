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
  const proxyUrl = buildProxyUrl(orderCode, label.packageNumber);

  void silentPrintViaBlob(proxyUrl, onAfterPrint).then((result) => {
    if (!result.printed) {
      // The label could not be fetched (e.g. 404 = carrier label not generated yet).
      // Do NOT fall back to window.open(proxyUrl): a bare navigation can't carry the
      // bearer token, so it would 401 and mask the real reason. Surface the status
      // instead, and don't fire onAfterPrint (which callers treat as success).
      onError?.(result.status);
    }
  });
};
