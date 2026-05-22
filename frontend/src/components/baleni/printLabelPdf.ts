import { getAuthenticatedApiClient } from '../../api/client';
import { ShipmentLabelDto } from '../../api/generated/api-client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const buildProxyUrl = (orderCode: string, packageName: string): string => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  return `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/packages/${encodeURIComponent(packageName)}/label.pdf`;
};

const openInNewTab = (url: string): void => {
  window.open(url, '_blank', 'noopener,noreferrer');
};

const silentPrintViaBlob = async (url: string, onAfterPrint?: () => void): Promise<boolean> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  let response: Response;
  try {
    response = await apiClient.http.fetch(url);
  } catch {
    return false;
  }
  if (!response.ok) return false;

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
    return true;
  } catch {
    return false;
  }
};

export const printLabelPdf = (
  orderCode: string,
  label: ShipmentLabelDto,
  onAfterPrint?: () => void,
): void => {
  if (!label.packageName) return;
  const proxyUrl = buildProxyUrl(orderCode, label.packageName);

  void silentPrintViaBlob(proxyUrl, onAfterPrint).then((printed) => {
    if (!printed) {
      openInNewTab(proxyUrl);
      onAfterPrint?.();
    }
  });
};
